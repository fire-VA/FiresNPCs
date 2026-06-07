# Buffer — Marketplace config schema

Buffer is split across **two folders** with **two different grammars**, parsed by one
`Buffer_Main_Server.ReadBufferConfigs()`:

- `Configs/Buffers/*.cfg` — the **buff database** (defines each buff: stats, cost, duration, icon). Parsed by `ReadBufferDatabase`.
- `Configs/BufferProfiles/*.cfg` — **which buffs a Buffer NPC sells**, by referencing buff `UniqueName`s. Parsed by `ReadBufferProfiles`.

Read order: profiles first, then database (so a profile referencing an unknown UniqueName just yields no resolved buff client-side).

## Config location
- Folders:
  - `Configs/BufferProfiles/*.cfg` (recursive merge — `SearchOption.AllDirectories`) → `Market_Paths.BufferProfilesFolder`
  - `Configs/Buffers/*.cfg` (recursive merge) → `Market_Paths.BufferDatabaseFolder`
- Autoload watch keys / on-change handlers (from `[Market_Autoload(... new[]{"BP","BD"}, new[]{"OnBufferProfilesFileChange","OnBufferProfilesFileChange"})]`):
  - `"BP"` → `OnBufferProfilesFileChange`
  - `"BD"` → `OnBufferProfilesFileChange`
  - Both handlers call the same `ReadBufferConfigs()` (full re-read of **both** folders), so a change in either folder re-reads everything. `OnInit` = `OnInit` → `ReadBufferConfigs()`.

## Section header

### BufferProfiles (`Configs/BufferProfiles/`)
- `[name]` → current section key = `name` stripped of `[`, `]`, all spaces, then **`.ToLower()`**. Default key before any header = `"default"`.
- **No `[name=bool]` flag is used.** A `=` inside the brackets is not parsed specially — it would just become part of the key string (and get lowercased). There is no `NeedToKnow`-style boolean here.
- **First-write-wins per key:** `if (!SyncedBufferProfiles.Value.ContainsKey(key))` — only the **first** non-comment data line under a given section key is stored. Subsequent data lines under the same key (even in another file) are silently ignored. So a profile section is effectively a single line.

### Buffers database (`Configs/Buffers/`)
- `[name]` → sets `UniqueName` (`str1`) = line stripped of `[`, `]`, all spaces. **NOT lowercased** (contrast with profiles). This is the buff's identity used for profile cross-reference.
- No `=bool` flag.
- After one buff record is consumed, `str1` is reset to `null`; a new `[header]` must precede the next record.

## Data-line grammar

### BufferProfiles grammar (one line per section)
The single stored data line is `profile.Replace(" ", "")` — a **comma-separated list of buff `UniqueName`s**. It is stored verbatim (as a string) on the server; the client splits it on `,` and resolves each token against the buff database by `UniqueName`.

```cfg
# This NPC profile (lowercased key "warrior") sells three buffs
[warrior]
StrengthPotion,SpeedPotion,IronSkin
```

- Field: CSV of `UniqueName` tokens (case-sensitive match against database `UniqueName`).
- Empty tokens (e.g. trailing comma) are skipped client-side.
- Whitespace inside is irrelevant — all spaces stripped before storage.

### Buffers database grammar (fixed 7-line record)
Each buff is a `[UniqueName]` header followed by **exactly 7 consecutive non-comment data lines**, parsed positionally as `profiles[index]` … `profiles[index+6]`:

| Offset | Field | C# type | Coercion |
|---|---|---|---|
| `[header]` | `UniqueName` | `string` | brackets + spaces stripped, NOT lowercased |
| `index+0` | `Name` | `string` | raw line (display name; spaces preserved as written) |
| `index+1` | `Duration` | `int` | `int.Parse(line.Replace(" ",""))` (seconds) |
| `index+2` | `SpritePrefab` | `string` | `line.Replace(" ","")` — prefab name for the icon (item/creature/cached sprite) |
| `index+3` | `NeededPrefab,NeededPrefabCount` | `string,int` | `line.Replace(" ","").Split(',')`; `[0]`=prefab, `int.Parse([1])`=count |
| `index+4` | **modifier list** | see below | `line.Replace(" ","")` split on `,` then each on `=` |
| `index+5` | `StartEffectPrefab` | `string` | `line.Replace(" ","")` — VFX prefab spawned on apply |
| `index+6` | `BuffGroup` | `string` | `line.Trim(' ')` (only ends trimmed; **interior spaces preserved**) |

**Bounds guard:** `if (index + 5 > profiles.Count) break;` — note this checks `+5` even though `+6` is accessed. A record whose `BuffGroup` (offset +6) is the very last possible line is fine, but a truncated file can throw `IndexOutOfRangeException` on `profiles[index+6]` (caught per-record, logged `Buffer error {file} in profile {UniqueName}`, record skipped). Replicate the `+5` guard but defend `+6` too.

**Modifier line (offset +4)** grammar: comma-separated `Flag=value` pairs.
- Split whole line on `,`.
- Each token split on `=`; only tokens with exactly 2 parts are used.
- Left = a `WhatToModify` enum name, parsed with `Enum.TryParse<WhatToModify>(name, ignoreCase: true, out result)` (case-insensitive). Unknown names are silently skipped.
- Right = `float.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)` — **invariant culture, `.` decimal separator**.
- For each recognized flag: `Flags |= flag` and `ModifyList[flag] = value`.

`WhatToModify` (`[Flags]` enum) accepted names and meaning (applied in `BufferMain` StatusEffect overrides):

| Name | Bit | Effect in `BufferMain` | Default if unset |
|---|---|---|---|
| `ModifyAttack` | 1 | `hit.ApplyModifier(v)` (multiplier) | 1 |
| `ModifyHealthRegen` | 2 | `regen *= v` | 1 |
| `ModifyStaminaRegen` | 4 | `staminaRegen *= v` | 1 |
| `ModifyRaiseSkills` | 8 | `skillGain *= v` | 1 |
| `ModifySpeed` | 16 | `speed *= v` | 1 |
| `ModifyNoise` | 32 | `noise *= v` | 1 |
| `ModifyMaxCarryWeight` | 64 | `limit += v` (**additive**) | 0 |
| `ModifyStealth` | 128 | `stealth *= v` | 1 |
| `RunStaminaDrain` | 256 | run **and** jump stamina `*= v` | 1 |
| `DamageReduction` | 512 | `hit.ApplyModifier(clamp01(1 - v))` (fraction) | 0 |

`ModifyList` is seeded with the defaults above before parsing, so only flags you list in offset +4 deviate.

```cfg
# Buffers database — one buff
[StrengthPotion]
Strength Potion
600
SwordIron
Coins,50
ModifyAttack=1.25,DamageReduction=0.1
sfx_imp_start
combat
```
That is: UniqueName `StrengthPotion`, display "Strength Potion", lasts 600s, icon from `SwordIron`, costs 50 `Coins`, +25% attack & 10% damage reduction, start VFX `sfx_imp_start`, buff-group `combat`.

## Domain object

### `Buffer_DataTypes.BufferBuffData : ISerializableParameter`
Fields: `string UniqueName, Name; int Duration; Dictionary<WhatToModify,float> ModifyList; string NeededPrefab; int NeededPrefabCount; WhatToModify Flags; string SpritePrefab, StartEffectPrefab, BuffGroup`. Plus client-only runtime: `BufferMain Main` (a `StatusEffect` ScriptableObject built in `Init()`), resolved `NeededItemName`, `Icon`, `NeededItemIcon`, `StartEffect`, `bool? IsValid`.

ZPackage wire order (authoritative field order): UniqueName, Name, Duration, then the 10 `ModifyList` floats in enum order (Attack, HealthRegen, StaminaRegen, RaiseSkills, Speed, Noise, MaxCarryWeight, Stealth, RunStaminaDrain, DamageReduction), NeededPrefab, NeededPrefabCount, `(int)Flags`, SpritePrefab, StartEffectPrefab, BuffGroup.

### Synced containers (`Buffer_DataTypes`)
- `SyncedBufferProfiles : CustomSyncedValue<Dictionary<string,string>>` key `"bufferProfiles"` — profile-key → raw CSV-of-UniqueNames string.
- `SyncedBufferBuffs : CustomSyncedValue<List<BufferBuffData>>` key `"bufferBuffs"` — the flat buff database.
- Client derives `ClientSideBufferProfiles : Dictionary<string, List<BufferBuffData>>` in `OnBufferUpdate()` by splitting each profile string on `,`, finding the matching `BufferBuffData` by `UniqueName`, and including it only if `IsValid == true` (i.e. its `NeededPrefab` resolved in `ZNetScene`).

## Persistence
- **ServerSync / `CustomSyncedValue` only.** No LiteDB collection, no player `customData`. The buff definitions live entirely in the `.cfg` files and are pushed to clients via `Marketplace.configSync`.
- **Buff application** is runtime `StatusEffect` state (vanilla SEMan), persisted only as long as vanilla persists status effects — not a Marketplace-owned store. Purchasing a buff consumes `NeededPrefabCount` of `NeededPrefab` from the player inventory (client `CanTake()`/`EnoughMaterials()` gate).
- A buff cannot stack within the same non-empty `BuffGroup` (`CanTake()` rejects if the player already has a `BufferMain` with the same group).

## NPC linkage
- `NPCType.Buffer` (Market_NPC enum value `Buffer`). On interact, `OpenUIForType` calls `Buffer_UI.Show(profile, npcName)`.
- The NPC's ZDO `Profile` string is **lowercased** (`profile = profile.ToLower()`) before the call, so it matches the **lowercased BufferProfiles section keys**. Buffer-database `UniqueName`s are case-sensitive and only referenced indirectly (inside the profile's CSV).

## Gotchas / risks for the parser
- **Two grammars, one feature.** Don't merge the folders into one parser — `Buffers/` is positional 7-line records, `BufferProfiles/` is one CSV line per section.
- **Case asymmetry:** profile section keys are lowercased; buff `UniqueName` (database header) is **not**. The profile CSV tokens must match the database `UniqueName` exactly (case-sensitive).
- **Profiles are first-write-wins, single-line:** only the first data line under a profile section is kept; later lines (any file) are dropped. A multi-line profile section will lose all but its first line.
- **Positional record fragility:** the 7-line database record is purely positional and uses the **same `index` cursor** advancing line-by-line; comment/blank lines between the 7 lines are skipped by the outer `if`, but the offset math (`index+1..+6`) does **not** account for interleaved comments — a `#`/blank line inside a record will misalign fields. Treat records as 7 *consecutive* non-skippable lines and forbid interior comments to match behavior safely.
- **The `+5` bounds check under-guards `+6`** (BuffGroup). Defend the last line yourself.
- `Duration`/`NeededPrefabCount` strip spaces then `int.Parse` (throws → record skipped). Modifier floats are **invariant-culture** — never locale.
- `Name` (offset +0) and `BuffGroup` (offset +6) preserve interior spaces (Name keeps them entirely; BuffGroup only trims ends). All other fields strip every space.
- A profile referencing a non-existent or unresolvable (`IsValid != true`) UniqueName simply contributes nothing — no error.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
