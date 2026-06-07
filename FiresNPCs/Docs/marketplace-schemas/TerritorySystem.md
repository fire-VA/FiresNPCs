# TerritorySystem — Marketplace config schema

Covers the `Territories/` folder **and** the `AdditionalTerritories/` folder. Decompiled from
`Marketplace/Modules/TerritorySystem/TerritorySystem_DataTypes.cs` and `..._Main_Server.cs`
(kg.Marketplace v9.8.1.0).

## Config location
- **Folder:** `BepInEx/config/Marketplace/Configs/Territories/` — read recursively
  (`Directory.GetFiles(TerritoriesFolder, "*.cfg", SearchOption.AllDirectories)`), all `.cfg`
  files **merged**. Format: custom CFG (hand-parsed, NOT a serializer).
- **`AdditionalTerritories/` folder (`Configs/AdditionalTerritories/`):** created on init
  (`Market_Paths.cs` `Directory.CreateDirectory`) **but in v9.8.1.0 it is NOT read by any code.**
  It is not registered in `Marketplace.FoldersToFiles`, has no `Market_Autoload` watch key, and
  `ReadServerTerritoryDatabase()` only enumerates `TerritoriesFolder`. The name
  "AdditionalTerritories" is a red herring: the **`AdditionalTerritoryFlags`** (the "additional"
  flag set) are parsed **inline from the same `Territories/*.cfg` flag list** — see grammar below.
  → Our parser should create the empty folder for parity but read territories only from
  `Territories/`. (Flag: re-verify if a newer Marketplace version wires this folder in.)
- **Two server-side YAML sidecars** live inside `Territories/` (written/read by the server, not
  user-authored CFG):
  - `_InternalMarketplace_TerritoriesForSale.yml` — `List<Territory>` (YamlDotNet `Serializer`),
    the persisted "ForSale" territories created/bought via RPC at runtime.
  - `_InternalMarketplace_TerritoriesFlagOverrides.yml` — `Dictionary<string, TerritoryOverride>`
    where `TerritoryOverride { string TerritoryFlags; string TerritoryColor; }`, keyed by
    territory **Name**. When present and non-empty, the override's `TerritoryColor` / `TerritoryFlags`
    string **replaces** the color line / flags line from the CFG for that named territory.
- **Autoload / watch:** `[Market_Autoload(Server, Normal, "OnInit", {"TD"}, {"OnTerritoryConfigChange"})]`.
  Watch key `"TD"` → `FoldersToFiles[TerritoriesFolder] = "TD"`. On any change in `Territories/`,
  `OnTerritoryConfigChange()` → `ReadServerTerritoryDatabase()` re-reads + re-syncs.

## Section header / structure
Per-line scan (`ProcessTerritoryConfig`). Skips blank/whitespace lines and `#` comments.
- **Section header:** a line starting with `[`. Stripped of `[`/`]`, then split on `@`:
  - `[name]` → territory **Name = name**, **Priority = 1**.
  - `[name@N]` → Name = name, **Priority = N** (`int.Parse`). Higher priority wins overlap
    (`Territories.Sort` descending by Priority; `GetCurrentTerritory` returns first match).
  - **NOTE:** unlike other modules this is `@`-delimited, NOT the shared `[name=bool]` form, and the
    name is **NOT lowercased** (case preserved; used as the display name and override key).
- **Data block:** every territory is **exactly 5 consecutive non-comment data lines** following the
  header (relative offsets `index1+0 .. index1+4`). The loop only consumes line[index1] when it
  parses as a `TerritoryShape`; the other 4 lines are read by absolute offset. So a territory =
  header + 5 lines, in fixed order:

```
[MyZone@5]
Circle               # line 0: shape
500, -200, 75        # line 1: geometry
255,0,0,true,FromCenter,exp:2.0,heightbounds:0-500   # line 2: color/render
NoBuild,PvpOnly,PeriodicDamage=5,MoveSpeedMultiplier=1.5   # line 3: flags
ALL                  # line 4: permitted
```

If fewer than 4 lines remain after the header start (`index1 + 4 > profiles.Count`), parsing
`break`s. Per-territory errors are caught and logged (`Error loading zone profile {name} ...`), the
territory skipped.

## Data-line grammar (the 5 lines)

### Line 0 — Shape (`TerritoryShape` enum, case-insensitive)
```
Circle | Square | Rectangle
```

### Line 1 — Geometry (CSV, spaces stripped)
- **Circle / Square:** `centerX, centerY, radius` → `X=[centerX]`, `Y=[centerY]`, `Radius=radius`
  (3 ints). (X/Y are world coords; "Y" here is world **Z**.)
- **Rectangle:** `x, z, width, height` → `X=[x, width]`, `Y=[z, height]` (4 ints).
  Inside test: `x in [X0, X0+X1]`, `z in [Y0, Y0+Y1]`.

### Line 2 — Color / render options (CSV, spaces stripped, tokens consumed in this order)
Tokens are scanned and removed before the remainder is read as RGB triples:
- **`true` / `false`** (first one found) → `ShowExternalWater` (default `true`). Removed.
- **A `GradientType` name** (token that is not an int and parses as the enum) → `GradientType`
  (default `LeftRight`). Removed. Values: `FromCenter, ToCenter, LeftRight, RightLeft, TopBottom,
  BottomTop, BottomRightTopLeft, TopRightBottomLeft, BottomLeftTopRight, TopLeftBottomRight`.
- **`exp:<float>`** → `ExponentialValue` (default `1.0`, invariant-culture float). Removed.
- **`heightbounds:<min>-<max>`** → `HeightBounds = (min, max)` ints (default `(-100000, 100000)`).
  Removed. (Inside test also requires `pos.y` within these bounds.)
- **Remainder = RGB color triples**: consumed 3 tokens at a time → `Color32(r,g,b,255)` into
  `Colors`. Multiple triples = a gradient (used when shape ≠ Rectangle and `Colors.Count > 1`).
  If the override sidecar has a `TerritoryColor` for this Name, it replaces this whole line.

Example: `255,0,0, 0,0,255, true, FromCenter, exp:2, heightbounds:0-500` → two colors (red→blue),
external water on, center gradient, exponent 2, height 0..500.

### Line 3 — Flags (comma-separated; spaces outside quotes removed via `ReplaceSpacesOutsideQuotes()`)
Each token is `Flag` or `Flag=value`. Tried first as `AdditionalTerritoryFlags`, else as
`TerritoryFlags` (both case-insensitive, `Enum.IsDefined` checked). `OR`-accumulated into
`Flags` / `AdditionalFlags`. Some flags also consume their `=value`:

**`TerritoryFlags` (`[Flags]` int) — ALL values (other mods query these via the public API):**
| Flag | Value | `=value` consumed |
|---|---|---|
| None | 0 | — |
| PushAway | 1 | — |
| NoBuild | 2 | — |
| NoPickaxe | 4 | — |
| NoInteract | 8 | — |
| NoAttack | 16 | — |
| PvpOnly | 32 | — |
| PveOnly | 64 | — |
| PeriodicHeal | 128 | float → `PeriodicHealValue` |
| PeriodicDamage | 256 | float → `PeriodicDamageValue` |
| IncreasedPlayerDamage | 512 | float → `IncreasedPlayerDamageValue` |
| IncreasedMonsterDamage | 1024 | float → `IncreasedMonsterDamageValue` |
| NoMonsters | 2048 | — |
| CustomEnvironment | 4096 | `value.Split(',')` → `CustomEnvironment` string[] |
| MoveSpeedMultiplier | 8192 | float → `MoveSpeedMultiplier` |
| NoDeathPenalty | 16384 | — |
| NoPortals | 32768 | — |
| PeriodicHealALL | 65536 | float → `PeriodicHealValue` (shared with PeriodicHeal) |
| ForceGroundHeight | 131072 | float → `OverridenHeight` |
| ForceBiome | 262144 | int → `OverridenBiome` (Heightmap.Biome int) |
| AddGroundHeight | 524288 | float → `OverridenHeight` |
| NoBuildDamage | 1048576 | — |
| MonstersAddStars | 2097152 | int → `AddMonsterLevel` |
| InfiniteFuel | 4194304 | — |
| NoInteractItems | 8388608 | — |
| NoInteractCraftingStation | 16777216 | — |
| NoInteractItemStands | 33554432 | — |
| NoInteractChests | 67108864 | — |
| NoInteractDoors | 134217728 | — |
| NoStructureSupport | 268435456 | — |
| NoInteractPortals | 536870912 | — |
| CustomPaint | 1073741824 | int → `PaintGround` (`PaintType`: Paved=0, Grass=1, Cultivated=2, Dirt=3) |
| LimitZoneHeight | -2147483648 (0x80000000) | float → `OverridenHeight` |

**`AdditionalTerritoryFlags` (`[Flags]` int) — ALL values:**
| Flag | Value | `=value` consumed |
|---|---|---|
| None | 0 | — |
| NoItemLoss | 1 | — |
| SnowMask | 2 | — |
| NoMist | 4 | — |
| InfiniteEitr | 8 | — |
| InfiniteStamina | 16 | — |
| DropMultiplier | 32 | float → `DropMultiplier` |
| ForceWind | 64 | float → `Wind` |
| GodMode | 128 | — |
| OnlyForGuild | 256 | string → `OnlyForGuild` (guild name) |
| CustomJereSpawner | 512 | string → added to `CustomSpawnerNames` (HashSet) |
| JoinOtherServer | 1024 | string → `JoinOtherServerString` |
| RevealOnMap | 2048 | — |
| OnlyForFaction | 4096 | string → `OnlyForFaction` (**lowercased** — matches the faction dict key) |

Notes: flags whose value-parse uses `Convert.ToSingle(str4, new CultureInfo("en-US"))` so decimals
use `.`. `CustomEnvironment`'s value is itself comma-split — but the outer line is comma-split first,
so multiple environments must be supplied as repeated `CustomEnvironment=...` only via the quoted form
(`ReplaceSpacesOutsideQuotes` hints values can be quoted). If the override sidecar has a
`TerritoryFlags` string for this Name, it replaces this whole line.

### Line 4 — Permitted
Raw string, spaces stripped. `Permitted = (empty ? "None" : line)`. Special values: `ALL` (everyone
permitted), a substring-matched user-ID list, or `None`. Permission check (`IsPermitted`, client) is
true if: `Owner == localUserID` OR `Permitted.Contains(localUserID)` OR `Permitted.Contains("ALL")`
OR own guild name == `OnlyForGuild` OR `Factions_Client.IsMyFaction(OnlyForFaction)`.

## Domain object — `TerritorySystem_DataTypes.Territory` (implements `ISerializableParameter`)
Key fields (full ZPackage serialize order is the authoritative list):
`Shape, Flags, AdditionalFlags, X[], Y[], Name, Colors(List<Color32>), Radius, ShowExternalWater,
Priority, PeriodicHealValue, PeriodicDamageValue, IncreasedPlayerDamageValue,
IncreasedMonsterDamageValue, CustomEnvironment[], MoveSpeedMultiplier, Permitted, OverridenHeight,
OverridenBiome, AddMonsterLevel, PaintGround, GradientType, ExponentialValue, HeightBounds(int,int),
Wind, DropMultiplier, OnlyForGuild, OnlyForFaction, CustomSpawnerNames(HashSet<string>),
Type(TerritoryType: AdminCreated=0, ForSale=1), Owner, SellPrice, UID(random int), JoinOtherServerString`.
Container: `TerritoryContainer { List<Territory> Territories; bool ShouldRedraw; }`.

## Persistence
- **Admin-authored territories:** config-only (`Territories/*.cfg`). No DB. They live entirely in the
  `CustomSyncedValue<TerritoryContainer> SyncedTerritoriesData` (ServerSync key `"territoryData"`),
  rebuilt from CFG every load.
- **ForSale territories + flag overrides:** persisted to the two **YAML sidecars** inside `Territories/`
  (`_InternalMarketplace_TerritoriesForSale.yml`, `_InternalMarketplace_TerritoriesFlagOverrides.yml`),
  rewritten on RPC (`AddTerritory`, `RemoveTerritory`, `BuyTerritory`/GetOwnership,
  `SellTerritory`/CancelOwnership, `AddOverrideFlagAndSave`). On load they are appended to the synced
  list. **Owner** of a bought territory = the buyer's host-name (userID); stored in the sidecar YAML.
- **No LiteDB involvement.**

## NPC linkage
None directly. Territories are world-position regions, not NPC profiles — an NPC does not reference a
territory by `Profile`. Indirect links: `OnlyForFaction` ties into the Factions module
(`MPASN_Faction` player customData), `OnlyForGuild` into Guilds, and `CustomJereSpawner` names hook the
NPC/custom-spawner system.

## Gotchas / risks for the parser
- `AdditionalTerritories/` is a **dead folder** in this version — do not parse it; AdditionalTerritoryFlags
  come from the same Line-3 flags list. Create the dir for drop-in parity only.
- Fixed **5-line block** by absolute offset, not delimited — a missing/extra line shifts everything.
  Replicate the offset reads, not a token stream.
- Section header is **`@`-delimited (`[name@priority]`)**, NOT the shared `[name=bool]`. Name case is
  preserved (override-key + display).
- Line 2 token consumption is **order-sensitive removal** (bool → gradient → exp: → heightbounds: →
  RGB remainder). Parse the special tokens out first, then read the rest as RGB triples.
- Flag `=value` floats use `en-US` culture (decimal `.`); `exp:` uses InvariantCulture. Don't apply the
  current locale.
- Same flag value can be shared by two flags (e.g. PeriodicHeal & PeriodicHealALL both write
  `PeriodicHealValue`); LimitZoneHeight is the negative `int.MinValue` bit.
- "Y" in geometry is world **Z**. Rectangle uses X/Y arrays of length 2 (origin + size), Circle/Square
  length 1 + Radius.
- Override sidecars can silently replace the color/flags lines for a named territory; a faithful reader
  must apply them before parsing those two lines.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
