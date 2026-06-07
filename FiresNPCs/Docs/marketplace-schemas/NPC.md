# NPC — Marketplace schema

> The NPC is **not a `.cfg`**. It is a ZDO-driven prefab named `MarketPlaceNPC` (asset loaded from the
> mod's AssetBundle, registered into `ZNetScene.m_namedPrefabs` under hash `"MarketPlaceNPC".GetStableHashCode()`).
> All per-NPC state — type, profile, dialogue, full appearance — lives in **ZDO fields keyed by `"KG…"` strings**.
> The world save (`ZDOMan`) is the persistence; there is no per-NPC config file in the world. The
> `Marketplace_SavedNPCs/*.yml` files are only **client-side hammer-placement templates** (see Persistence).

Source: `Marketplace/Modules/NPC/Market_NPC.cs`, `NPC/NPC_DataTypes.cs`, `NPC/NPC_MapController.cs`,
`NPC/NPC_MapPins.cs`, `Hammer/MarketplaceHammer.cs`, and the public extension API in
`API/Marketplace_API.cs` (the **authoritative ZDO key list** — every `GET_NPC_*`/`SET_NPC_*` wraps one key).

---

## Location / how loaded

- **Runtime object:** `MarketPlaceNPC` prefab + `Market_NPC.NPCcomponent` MonoBehaviour
  (`Hoverable, Interactable, IDestructible`). On `ZNetScene.Awake` the prefab is force-registered:
  `m_namedPrefabs.Add("MarketPlaceNPC".GetStableHashCode(), NPC)`.
- **Placement:** admin (`IsDebug_Marketplace`) uses the Marketplace hammer. Placing a hammer piece named
  `…MARKETNPC_HAMMER_BUILDED` instantiates `MarketPlaceNPC` at that point and immediately calls the RPC
  setters from the chosen saved-NPC template (`MarketplaceHammer.HookBuild.PlacedPiece`).
- **No `[section]` parsing.** NPCs are never read from a `Configs/` folder. The only file-backed input is
  `Marketplace_SavedNPCs/*.yml|*.yaml` (hammer templates, client only).

### `NPCType` enum (`Market_NPC.NPCType`) — all values + which module each opens

`OpenUIForType` switches on the type (`Market_NPC.cs`). Note: `NPCType` has **12** members; the public
`Marketplace_API.API_NPCType` mirrors the first 11 (it is **missing `Mail`** — index 11 — so setting Mail
via the API is not possible, only via the in-game UI / saved template).

| idx | NPCType    | Opens (client)                              |
|-----|------------|---------------------------------------------|
| 0   | None       | nothing (plain decorative / patrol NPC)     |
| 1   | Trader     | `Trader_UI.Show(profile, npcName)`          |
| 2   | Info       | `ServerInfo_UI.Show(profile, npcName)`      |
| 3   | Teleporter | `Teleporter_Main_Client.ShowTeleporterUI(profile)` |
| 4   | Feedback   | `Feedback_UI.Show()` (no profile)           |
| 5   | Banker     | `Banker_UI.Show(profile, npcName)`          |
| 6   | Gambler    | `Gambler_UI.Show(profile)`                  |
| 7   | Quests     | `Quests_UIs.QuestUI.Show(profile, npcName)` |
| 8   | Buffer     | `Buffer_UI.Show(profile, npcName)`          |
| 9   | Transmog   | `Transmogrification_UI.Show(profile, npcName)` |
| 10  | Marketplace| `Marketplace_UI.Show()` (needs local userID)|
| 11  | Mail       | `CentralizedMailSystem_UI.Show(true)`       |

The string overload `OpenUIForType(string type, …)` also accepts `"Dialogue"`/`"Dialog"` → loads a dialogue
profile directly (used by the `OpenUI` dialogue command), otherwise `Enum.Parse<NPCType>(type, ignoreCase:true)`.

**Interact precedence** (`NPCcomponent.Interact`): C+Use → SaveNPC (debug); LeftAlt+Use → Fashion editor
(debug); Alt+Use → Main editor (debug); else if NPC is a **quest reward target** → claim reward; else if
`KGnpcDialogue` set → open dialogue; else → `OpenUIForType`. So **a dialogue overrides the type UI** when present.

---

## Data model / grammar

Two structs back the NPC. They are NOT serialized to `.cfg`; they are an in-memory transfer shape (and the
YAML shape of saved-NPC templates). Each field maps 1:1 to a ZDO key (see ZDO keys section).

### `NPC_DataTypes.NPC_Main` (struct)

| field        | type                  | meaning |
|--------------|-----------------------|---------|
| `Type`       | `Market_NPC.NPCType`  | stored as `int` |
| `NameOverride`| `string`             | display name (rich-text allowed; `$mpasn_<Type>` localized fallback if empty) |
| `Profile`    | `string`              | profile name → the module config section to open (lowercased on set) |
| `Prefab`     | `string`              | model override prefab name; `""` = default robed NPC. Supports `@humanoid` suffix (use Player animator) and space-separated random pool, and `_Female` suffix (use female body mesh) |
| `Dialogue`   | `string`              | dialogue UID to open on interact (lowercased on set); overrides Type UI |
| `PinIcon`    | `string`              | map-pin icon name (resolved via `Utils.TryFindIcon`) |
| `IMAGE`      | `string`              | base64 PNG screenshot, **saved-template only** (preview thumbnail, not synced to ZDO) |

### `NPC_DataTypes.NPC_Fashion` (struct, `ISerializableParameter`) — all 26 fields, all `string`

Sent over RPC `"KGmarket fashion"`; `FashionApply` parses each into the correct ZDO type. All declared
fields are `string`; the parenthetical is the **parsed/stored** type.

| #  | field                  | parsed as | notes |
|----|------------------------|-----------|-------|
| 1  | `LeftItem`             | string    | left-hand item prefab |
| 2  | `RightItem`            | string    | right-hand item prefab |
| 3  | `HelmetItem`           | string    | helmet prefab |
| 4  | `ChestItem`            | string    | chest armor prefab |
| 5  | `LegsItem`             | string    | legs armor prefab |
| 6  | `CapeItem`             | string    | cape prefab |
| 7  | `HairItem`             | string    | hair variant suffix (prefixed `"Hair"`) |
| 8  | `HairColor`            | string    | HTML color (`ColorUtility.TryParseHtmlString`, e.g. `#aabbcc`) |
| 9  | `ModelScale`           | float     | default 1; clamped ≥ 0.1; multiplies model localScale |
| 10 | `LeftItemHidden`       | string    | back-slot left (weapon→backMelee, else backShield) |
| 11 | `RightItemHidden`      | string    | back-slot right (backMelee) |
| 12 | `InteractAnimation`    | string    | animator trigger on interact |
| 13 | `GreetAnimation`       | string    | animator trigger when player approaches (<10 m) |
| 14 | `ByeAnimation`         | string    | animator trigger when player leaves (>12 m) |
| 15 | `GreetText`            | string    | floating text on approach |
| 16 | `ByeText`              | string    | floating text on leave |
| 17 | `SkinColor`            | string    | HTML color for body skin |
| 18 | `CraftingAnimation`    | int       | `KGcraftingAnimation` animator "crafting" int; 0 = look-at IK enabled, non-0 = static crafting pose |
| 19 | `BeardItem`            | string    | beard variant suffix (prefixed `"Beard"`) |
| 20 | `BeardColor`           | string    | HTML color |
| 21 | `InteractAudioClip`    | string    | audio clip key (from `AssetStorage.NPC_AudioClips`) |
| 22 | `TextSize`             | float     | default 3; nameplate canvas scale |
| 23 | `TextHeight`           | float     | default 0; nameplate Y offset (base 3.4) |
| 24 | `PeriodicAnimation`    | string    | trigger fired every PeriodicAnimationTime |
| 25 | `PeriodicAnimationTime`| float     | seconds; ≤0 disables |
| 26 | `PeriodicSound`        | string    | audio clip key |
| —  | `PeriodicSoundTime`    | float     | seconds; ≤0 disables (27th, the field after #26) |

> Counting note: the struct declares **27** string fields total (`PeriodicSoundTime` included). The task's
> "26 fields" excludes the final `PeriodicSoundTime`; both are documented above. `ModelScale` is technically
> field #9 in declaration order, listed in the table by storage type.

**`NPC_Fashion.ApplyRandom()`** (called on hammer placement): a subset of string fields supports a
**space-separated random pool** — `field.RandomSplitSpace()` picks one token at random. Applied to: LeftItem,
RightItem, HelmetItem, ChestItem, LegsItem, CapeItem, HairItem, HairColor, LeftItemHidden, RightItemHidden,
InteractAnimation, GreetAnimation, ByeAnimation, SkinColor, CraftingAnimation, BeardItem, BeardColor,
PeriodicAnimation. (Texts, times, scale, sounds are NOT randomized.) `Prefab` is likewise `RandomSplitSpace()`d.

### Patrol data (not in NPC_Fashion)

`KGmarket PatrolData` string format (parsed in `InitPatrolData`): `x,z,x,z,…@maxSpeed`.
- Before `@`: comma list of float pairs (waypoints in world XZ), spaces stripped, `en-US` culture.
- After optional `@`: a single float max-speed (default 1). NPC ping-pongs along the waypoints
  (`KGmarket LatestPatrolPoint`, `KGmarket GoBack`), pausing when a player is within 3 m. Needs ≥2 points.

---

## ZDO keys

The exact ZDO string keys (from `Marketplace_API` extension methods + `Market_NPC.FashionApply` + patrol).
**Keys with embedded spaces are literal** (e.g. `"KGmarket PatrolData"`). Stored type in brackets; defaults
where the getter supplies one.

| ZDO key                    | type   | default | field |
|----------------------------|--------|---------|-------|
| `KGmarketNPC`              | int    | 0       | NPCType. `InitNpc` clamps `≥ TypeNames.Length` → 0 |
| `KGnpcProfile`             | string | `"default"` | Profile (getter default `"default"`) |
| `KGnpcModelOverride`       | string | ""      | Prefab/model override |
| `KGnpcNameOverride`        | string | ""      | NameOverride |
| `KGnpcDialogue`            | string | ""      | Dialogue UID |
| `KGmarket PatrolData`      | string | ""      | patrol string (note literal space) |
| `KGmarketPinned`           | bool   | false   | IsPinned (map pin shown) |
| `KGmarketPinIcon`          | string | ""      | PinIcon |
| `KGleftItem`               | string | ""      | LeftItem |
| `KGrightItem`              | string | ""      | RightItem |
| `KGhelmetItem`             | string | ""      | HelmetItem |
| `KGchestItem`              | string | ""      | ChestItem |
| `KGlegsItem`               | string | ""      | LegsItem |
| `KGcapeItem`               | string | ""      | CapeItem |
| `KGhairItem`               | string | ""      | HairItem |
| `KGhairItemColor`          | string | ""      | HairColor |
| `KGLeftItemBack`           | string | ""      | LeftItemHidden (note capital L/B) |
| `KGRightItemBack`          | string | ""      | RightItemHidden (note capital R/B) |
| `KGinteractAnimation`      | string | ""      | InteractAnimation |
| `KGgreetingAnimation`      | string | ""      | GreetAnimation |
| `KGbyeAnimation`           | string | ""      | ByeAnimation |
| `KGgreetingText`           | string | ""      | GreetText |
| `KGbyeText`                | string | ""      | ByeText |
| `KGskinColor`              | string | ""      | SkinColor |
| `KGcraftingAnimation`      | int    | 0       | CraftingAnimation (parsed from string; non-int → 0) |
| `KGbeardItem`              | string | ""      | BeardItem |
| `KGbeardColor`             | string | ""      | BeardColor |
| `KGinteractSound`          | string | ""      | InteractAudioClip |
| `KGtextSize`               | float  | 3       | TextSize |
| `KGtextHeight`             | float  | 0       | TextHeight |
| `KGperiodicAnimation`      | string | ""      | PeriodicAnimation |
| `KGperiodicAnimationTime`  | float  | 0       | PeriodicAnimationTime |
| `KGperiodicSound`          | string | ""      | PeriodicSound |
| `KGperiodicSoundTime`      | float  | 0       | PeriodicSoundTime |
| `KGnpcScale`               | float  | 1       | ModelScale |

**Patrol-runtime keys (set by the NPC itself, not part of templates):**

| ZDO key                    | type | meaning |
|----------------------------|------|---------|
| `KGmarket LatestPatrolPoint` | int  | current waypoint index |
| `KGmarket GoBack`            | bool | ping-pong direction flag |

`Market_NPC.cs` also defines unused const names `"KGmarket PatrolData"`, `"KGmarket LatestPatrolPoint"`,
`"KGmarket GoBack"` (PatrolData/LatestPatrolPoint/GoBackPatrol).

### RPCs registered on the component (`NPCcomponent.Awake`)

| RPC name                  | args                                  | handler |
|---------------------------|---------------------------------------|---------|
| `KGMarket changeNpcType`  | `int type, bool pinned, string pinIcon` | `ChangeNpcType` → sets `KGmarketNPC`, `KGmarketPinned`, `KGmarketPinIcon` |
| `KGmarket changeprofile`  | `string profile, string dialogue`     | `ChangeProfile` → sets `KGnpcProfile` (lower, "default" if blank), `KGnpcDialogue` (lower) |
| `KGmarket overridename`   | `string name`                         | `OverrideName` → sets `KGnpcNameOverride` |
| `KGmarket overridemodel`  | `string model`                        | `OverrideModel` → sets `KGnpcModelOverride` |
| `KGmarket fashion`        | `NPC_Fashion`                         | `FashionApply` → sets all fashion ZDO keys |
| `KGmarket GetPatrolData`  | `string data`                         | `GetPatrolData` → sets `KGmarket PatrolData`/LatestPatrolPoint/GoBack |
| `ValheimMap_Update`       | `int targetRevision`                  | request re-init after ZDO revision reaches target |

Only the owner writes ZDO; the dialogue commands `SetNPCModelGlobal`/`SetNPCNameGlobal`/`SetNPCPatrol`
broadcast to `ZRoutedRpc.Everybody` so the owner persists.

---

## Persistence

- **Authoritative state = the world save (ZDOMan).** Once placed, an NPC is a ZDO with prefab
  `MarketPlaceNPC`; all `KG…` fields ride in that ZDO and reload with the zone. Map pins are discovered by
  `ZDOMan.GetAllZDOsWithPrefabIterative("MarketPlaceNPC", …)` then filtered to `GET_NPC_IsPinned()`.
- **`Marketplace_SavedNPCs/*.yml` = client-side hammer templates only.** Path
  `BepInEx/config/Marketplace_SavedNPCs` (`Market_Paths.NPC_Saved`). `SaveNPC` (C+Use in debug) serializes a
  `MarketplaceHammer.Wrapper { bool isPinned; NPC_Main main; NPC_Fashion fashion; }` via YamlDotNet
  `SerializerBuilder().Build().Serialize(graph)`. Filename = `"{Type} {Name} {Profile}.yml"` (rich-text
  stripped). `main.IMAGE` holds a base64 PNG preview. On hammer reload (`/mreloadnpcs`) each yml/yaml file is
  read and turned into a placeable piece; placing it runs the RPC setters (`ChangeNpcType`, `ChangeProfile`,
  `OverrideName`, `OverrideModel`, `ApplyRandom`+`FashionApply`). These files are NEVER read on the server and
  do not define live NPCs — they are a build palette.
- **Not synced via config files at all.** There is no `Configs/NPC/*.cfg`.

---

## Linkage

- **`Profile` string → module config section.** When the NPC opens (`OpenUIForType`), `profile.ToLower()` is
  passed to the matching module UI (`Trader_UI.Show(profile,…)`, etc.). That profile name is the **`[section]`
  header** inside the corresponding `Configs/<Module>/*.cfg` (see Trader/Banker/etc. docs). Default `"default"`.
- **`Dialogue` string → dialogue UID.** `KGnpcDialogue` (lowercased) matches a `RawDialogue.UID`
  (`[uid]` header in `Configs/Dialogues/*.cfg`). If set, interacting opens the dialogue instead of the type UI;
  dialogue options can then call `OpenUI` to reach the type UI. See `Dialogues.md`.
- **`PinIcon` / `InteractAudioClip` / `PeriodicSound`** resolve against client asset caches
  (`Utils.TryFindIcon`, `AssetStorage.NPC_AudioClips`) — names, not paths.

---

## Gotchas / risks

- **ZDO keys contain spaces** (`KGmarket PatrolData`, `KGmarket LatestPatrolPoint`, `KGmarket GoBack`). Hash
  with the space included or pins/patrol break.
- **`KGLeftItemBack` / `KGRightItemBack` use mixed-case** (capital L/R and B); all other item keys are
  lowercase-first. Easy to typo.
- **`API_NPCType` is missing `Mail`** (the API enum has 11 values, the real enum 12). Don't assume the two are
  identical — index 11 = Mail only exists on `Market_NPC.NPCType`.
- **CraftingAnimation is dual-purpose**: stored as int `KGcraftingAnimation`; value 0 attaches a head look-at
  IK component, non-0 sets the model animator "crafting" integer and skips look-at.
- **Random pools**: a space inside LeftItem/Prefab/etc. is NOT a literal — `RandomSplitSpace` treats it as a
  random choice list, applied only at hammer placement (`ApplyRandom`), not on `FashionApply` of an existing NPC.
- **Floats parse with `InvariantCulture` / `en-US`** (patrol). Replacement importer must not use a comma decimal.
- **Hair/Beard get a literal prefix**: the stored value is the suffix; the model lookup is `"Hair"+value` /
  `"Beard"+value`.
- **Saved-NPC ymls are not authoritative** — importing them as live NPCs would be wrong; they're a build menu.
  Live NPC fidelity = reproduce the ZDO keys exactly.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
