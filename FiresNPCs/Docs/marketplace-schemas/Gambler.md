# Gambler — Marketplace config schema

## Config location
- Folder: `Configs/Gamblers/*.cfg` (recursive merge — `SearchOption.AllDirectories`, all files merged)
- Autoload watch key / on-change handler: `[Market_Autoload(Server, Normal, "OnInit", new[]{"GP"}, new[]{"OnGamblerProfilesFileChange"})]` — watch key `GP`.

## Section header
- `[name]` means: gambler **profile name** (the section key). Stored stripped of `[`, `]`, all spaces, **lowercased**. Default key before any header is `"default"`.
- `[name=int]` flag means: **MAXROLLS**, an INTEGER (not a bool!). Parsed via `int.Parse(strArray[1])` when the bracket contents split on `=` into >1 parts. A header with no `=` sets `MAXROLLS = 1`. This is the max number of rolls/pulls allowed per interaction for this profile.

## Data-line grammar
**One profile = exactly ONE data line** (each data line REPLACES the profile's `BigData`, it does not append — see Gotchas). After `.Replace(" ","")` then `.Split(',')`:

- Tokens come in **pairs**: `prefab , range , prefab , range , ...`. The line must have an **even** token count; an odd count logs `Gambler {file} line {n} has error, number of data is wrong` and the line is skipped.
- For each pair, build an `Item`:
  - `Prefab = tokens[i]`
  - `tokens[i+1]` is a **range string** split on `-`:
    - 1 part (`"N"`): `Min = Max = int.Parse(N)` (fixed amount).
    - 2 parts (`"A-B"`): `Min = int.Parse(A)`, `Max = int.Parse(B)`.
    - (A 3+ part `A-B-C` would take `[0]` and `[1]` as Min/Max; extra ignored — the `else` branch only reads indices 0 and 1.)
- The **first** parsed `Item` (index 0) is removed from the list and becomes `RequiredItem` (the cost/entry item). The **remaining** items become `Data` (the reward pool).
- So the line layout is effectively: `CostPrefab,CostRange , Reward1Prefab,Reward1Range , Reward2Prefab,Reward2Range , ...`.

### Example lines
```cfg
[lucky_wheel=3]
Coins,100,SwordBronze,1,ShieldWood,1-2,Wood,5-20

[default]
Coins,50-50,AmberPearl,1-3
```
(First example: cost = `Coins×100`, MAXROLLS=3, rewards = `SwordBronze×1`, `ShieldWood×1..2`, `Wood×5..20`.)

## Domain object
- Synced container: `CustomSyncedValue<Dictionary<string, Gambler_DataTypes.BigData>> SyncedGamblerData` (ServerSync key `"gamblerData"`). Key = profile name; value = a single `BigData` (NOT a list).
- `BigData` (implements `ISerializableParameter`):
  - `Item RequiredItem` (the entry cost)
  - `List<Item> Data` (reward pool)
  - `int MAXROLLS`
  - Wire order: `MAXROLLS`, `Data.Count`, each `Data` item (`Prefab, Min, Max`), then `RequiredItem` (`Prefab, Min, Max`).
- `Item`: `string Prefab`, `int Min`, `int Max` (+ client-only `icon`, `LocalizedName`, `rawName` via `SetData`).

## Persistence
- Gambler tables: **config-file + ServerSync only** (`SyncedGamblerData`). Not in LiteDB.
- No per-player Gambler state persists in LiteDB — rolls run against the player's live inventory (consume `RequiredItem`, grant from `Data`). No cooldown/usage state is read from the cfg.

## NPC linkage
- Opened by `NPCType.Gambler`. `OpenUIForType` reads `GET_NPC_Profile()` ZDO string, **lowercases** it, calls `Gambler_UI.Show(profile)` → looks up `SyncedGamblerData.Value[profile]`.

## Gotchas / risks for the parser
- **`[name=int]` is MAXROLLS, not a bool** — this is the one module where the section flag is an integer. Use `int.Parse`, default 1.
- **Last line wins per profile**: each valid data line does `SyncedGamblerData.Value[key] = new BigData{...}` (assignment, not list-append). If a profile has multiple data lines, only the **last** survives. (Contrast Trader/Teleporter/Banker which append.)
- **Reward-pool cap = 19**: after reading all files, `ClientClearData()` truncates each profile's `Data` to the first 19 entries (`RemoveRange(19, count-19)`). The `RequiredItem` (the removed index-0) does not count toward this. So at most 1 cost + 19 rewards survive sync.
- Odd token count → logged-and-skipped (the only soft error); even count with a bad `int.Parse` inside a pair will **throw** (no try/catch around the inner parse) — that propagates up, unlike the graceful odd-count path. Our importer should guard each parse.
- The range delimiter is `-` (hyphen). Negative numbers would break range parsing (a leading `-` makes an empty first split part).
- `.Replace(" ","")` strips all spaces; prefab names can't contain spaces.
- Section keys lowercased; NPC lookup lowercased — match case-insensitively.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
