# Transmogrification — Marketplace config schema

## Config location
- Folder: `Configs/Transmogrifications/*.cfg` (recursive merge — `SearchOption.AllDirectories`) → `Market_Paths.TransmogrificationFolder`.
- Autoload watch key / on-change handler (from `[Market_Autoload(... new[]{"TM"}, new[]{"OnTransmogrificationProfileChanged"})]`):
  - `"TM"` → `OnTransmogrificationProfileChanged` → `ReadTransmogrificationProfiles()` (clears + full re-read of the whole folder, then `SyncedTransmogData.Update()`).
  - `OnInit` → same `ReadTransmogrificationProfiles()`.

## Section header
- `[name]` → current section key = `name` stripped of `[`, `]`, all spaces, then **`.ToLower()`**. Default key before any header = `"default"`.
- On first sight of a key, an empty `List<TransmogItem_Data>` is created for it (`if (!...ContainsKey(key)) Add(key, new List<>())`). Re-encountering the same key (same or another file) **appends** to the existing list — sections are additive across files, unlike Buffer profiles.
- **No `[name=bool]` flag.** A `=` inside the brackets would just be part of the key (and lowercased).

## Data-line grammar
Each non-comment, non-header line is one **transmog offering** (an appearance the NPC can sell). Line is `line.Replace(" ", "").Split(',')`; the record is accepted only if it has **≥ 4 tokens** (extra tokens are ignored). Tokens are positional CSV:

| Index | Field | C# type | Coercion |
|---|---|---|---|
| 0 | `Prefab` | `string` | the **appearance** prefab name — the visual the player's item is re-skinned to (must resolve to an `ItemDrop` in `ZNetScene`) |
| 1 | `Price_Prefab` | `string` | currency item prefab |
| 2 | `Price_Amount` | `int` | `int.Parse` (throws → line skipped, logged `Error while parsing line {n} in TransmogConfig`) |
| 3 | `IgnoreCategory` | `bool` | `bool.Parse` — accepts `true`/`false` case-insensitively; anything else throws → line skipped |

`IgnoreCategory`: when **false**, the appearance is only offered for items of the **same `ItemType`** as the appearance prefab (client `InitTransmogData` buckets it by `itemData.m_shared.m_itemType`). When **true**, it is filed under the sentinel `(ItemDrop.ItemData.ItemType)999` "any category" bucket and can be applied to any item type.

There is **no source→appearance pair encoded in the config.** A transmog rule does not name a *source* item. The config only declares *available appearances* (+ price + category gate). The "source" is whatever item the player has selected in the UI at purchase time; the chosen appearance's `Prefab` becomes that item's `ReplacedPrefab`. So encoding is: `appearancePrefab, currencyPrefab, currencyAmount, ignoreCategory`.

```cfg
# Transmog profile (lowercased key "cosmetics")
[cosmetics]
# Re-skin to a bronze sword, costs 100 coins, only for sword-type items
SwordBronze,Coins,100,false
# Cape skin usable on ANY item type
CapeLox,Coins,250,true
```

## Domain object

### `Transmogrification_DataTypes.TransmogItem_Data : ISerializableParameter`
The config-declared offering. Fields: `string Prefab; string Price_Prefab; int Price_Amount; bool IgnoreCategory;` plus client-only runtime cache (`LocalizedName`, `LocalizedPrice`, `Icon`, `PriceIcon` via getters/setters). ZPackage wire order: `Prefab, Price_Prefab, Price_Amount, IgnoreCategory`.

### Synced container
`SyncedTransmogData : CustomSyncedValue<Dictionary<string, List<TransmogItem_Data>>>` key `"transmogData"` — profile-key → list of offerings. Client `Transmogrification_Main_Client.FilteredTransmogData` re-buckets each profile's list into `Dictionary<ItemType, List<TransmogItem_Data>>` (with `999` = any).

### Applied-appearance component (NOT config)
`TransmogItem_ComponentV2 : ItemDataManager.ItemData` with `[SerializeField] string ReplacedPrefab; string ItemColor;` — this is the **per-item** record of an applied transmog (the appearance prefab name + an optional `#RRGGBB` color string). It is attached to the actual `ItemDrop.ItemData` the player owns.

## Persistence
- **Config offerings:** ServerSync / `CustomSyncedValue` only (`transmogData`). No LiteDB, no player customData for the offerings themselves.
- **Per-player applied transmogs ("presets"):** stored **per item**, NOT in `DB.db`. On purchase the UI sets `component.ReplacedPrefab = data.Prefab`, `component.ItemColor = "#RRGGBB"` (validated via `ColorUtility.TryParseHtmlString`, else `""`), then calls `component.Save()`. `TransmogItem_ComponentV2` extends **`ItemDataManager.ItemData`** (the ItemDataManager / "ItemInfo" custom-data framework), so it serializes into the item's own custom data and rides along in the player's saved inventory / character file — wherever that item goes. There is no central transmog table to migrate; the appearance travels with the item.
- At render time, `Humanoid.SetupVisEquipment` is patched: if an equipped item has a `TransmogItem_ComponentV2`, the vis-equipment call is redirected to `ReplacedPrefab` (and variant), and the player ZDO `colorID` slot is set to `ItemColor` (or `""`). So the color is mirrored into a vanilla ZDO field for networked visibility, but the authoritative store is the item's custom data.
- A drop-in importer that only reads `.cfg` + `DB.db` will recover the **offerings** but will **not** find applied per-player transmogs there — those live in saved character/inventory data via ItemDataManager. Match ItemDataManager's serialization if you need to read existing applied skins.

## NPC linkage
- `NPCType.Transmog`. On interact, `OpenUIForType` → `Transmogrification_UI.Show(profile, npcName)`.
- The NPC ZDO `Profile` string is `.ToLower()`-ed before the call, matching the lowercased section keys. `Transmogrification_UI.Show` bails if `FilteredTransmogData` lacks the profile key.

## Gotchas / risks for the parser
- **No source item in config.** Don't look for a `source→appearance` pair; each line is just an available appearance. The source is chosen at runtime.
- **Sections are additive across files** (append to existing list) — unlike Buffer profiles (first-write-wins). Recursive merge genuinely concatenates.
- **≥ 4 tokens required**; a 3-token line is silently dropped (no exception). Extra (5th+) tokens are ignored — do not assume a 5-field variant exists.
- `Price_Amount` is `int.Parse` and `IgnoreCategory` is `bool.Parse`, both **non-invariant** but format-insensitive (`int`/`bool` don't care about culture here). Bad values throw → that one line is skipped + logged.
- All spaces are stripped before split, so the appearance/currency prefab names cannot contain spaces (vanilla prefab names don't).
- `(ItemType)999` is a sentinel for "any category" — replicate as a wildcard bucket, not a real ItemType.
- Applied transmogs are **not** in LiteDB — easy to wrongly assume they are. They persist via ItemDataManager item custom-data + a `colorID` ZDO mirror.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
