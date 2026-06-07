# Trader — Marketplace config schema

## Config location
- Folder: `Configs/Traders/*.cfg` (recursive merge — `Directory.GetFiles(..., SearchOption.AllDirectories)`, all files merged, filename irrelevant)
- Autoload watch key / on-change handler: `[Market_Autoload(Server, Normal, "OnInit", new[]{"TR"}, new[]{"OnTraderProfilesFileChange"})]` — watch key `TR`, re-reads whole folder + re-syncs on change.

## Section header
- `[name]` means: trader **profile name** (the section key). Stored stripped of `[`, `]`, all spaces, and **lowercased**. Default key before any header is `"default"`.
- `[name=bool]` flag means: `NeedToKnow`. Parsed via `Convert.ToBoolean(strArray[1])` (accepts `true`/`false`/`1`/`0`, case-insensitive). Set onto every `TraderData` created under this section as `NeedToKnow`. Semantically a gating/visibility flag (whether the trade is shown/known to the player). A header with no `=` resets the flag to `false`.

## Data-line grammar
Each data line is one trade recipe. Two top-level forms, decided by whether the **raw line contains `=`**.

### Form A — single-item trade, NO `=` (after `.Replace(" ","")` then `.Split(',')`)
Two field-count branches:

- **5 tokens**: `NeededPrefab , NeededCount , ResultPrefab , ResultCount , ResultLevel`
  - `NeededItems = [ { ItemPrefab=tok0, Count=int.Parse(tok1), Level=1(default) } ]`
  - `ResultItems = [ { ItemPrefab=tok2, Count=int.Parse(tok3), Level=int.Parse(tok4) } ]`
  - NOTE: only the **result** item gets a parsed level; the needed item keeps default `Level=1`.
- **4 tokens**: `NeededPrefab , NeededCount , ResultPrefab , ResultCount`
  - Same as above but `ResultLevel` defaults (`Level=1`); no level token parsed for either side.
- Any other token count in Form A (e.g. 3, 6+) is **silently ignored** — no branch matches, nothing added, no error thrown.
- `int.Parse` (not TryParse) on counts/level → a non-integer token throws, caught per-line, logged `Can't add items in {file}, line: {n}`, and the rest of that file is **`break`-skipped** (see Gotchas).

### Form B — multi-item trade, line CONTAINS `=` (after `.Replace(" ","")` then `.Split('=')`)
Only valid when split yields exactly **2 segments** (`need=result`); other counts ignored.
- Left segment `need` and right segment `result` are each `.Split(',')` and fed to `FillTrader`.
- `FillTrader` walks the token array building `TraderItem`s greedily:
  - Reads `prefab` = tokens[i], `Count` = `int.Parse(tokens[i+1])`.
  - If `tokens[i+2]` exists AND `int.TryParse` succeeds → `Level = that int`, advance `i += 3`.
  - Else `Level` stays default `1`, advance `i += 2`.
  - So each item is a `prefab,count` pair OR a `prefab,count,level` triple, **distinguished at runtime** by whether the 3rd token parses as an int. A non-numeric 3rd token is treated as the next item's prefab.
  - **Cap: max 5 items per side** (`if (list.Count < 5) list.Add(...)`). Extra parsed items past 5 are discarded silently, but parsing still continues (and can still throw on a bad count).

### Example lines
```cfg
[bandit_trader=false]
Coins,50,SwordBronze,1,2
Wood,10,Stone,5
LeatherScraps,5,Coins,25 = ArmorBronzeChest,1,3
Coins,100,Wood,50,2,Stone,25 = SwordIron,1,3,ShieldIronBuckler,1,2
```
(Line 4: left = two needed items `Coins×100 lvl2` + `Wood×50`; right = `SwordIron×1 lvl3` + `ShieldIronBuckler×1 lvl2`.)

## Domain object
- Synced container: `CustomSyncedValue<Dictionary<string, List<Trader_DataTypes.TraderData>>> SyncedTraderItemList` (ServerSync key `"traderItemList"`). Dictionary key = profile name; value = list of trades.
- `TraderData` (implements `ISerializableParameter`):
  - `bool NeedToKnow`
  - `List<TraderItem> NeededItems`
  - `List<TraderItem> ResultItems`
- `TraderItem`:
  - `int Count`
  - `string ItemName`, `string OriginalItemName` (client display, not from cfg)
  - `string ItemPrefab` (the cfg prefab token)
  - `int Level = 1` (default)
  - `bool DisplayStars`, `TraderItemType Type { Item, Skill, Monster }`, `Sprite Icon` (client-only)
  - Wire serialization writes per side: `Count` then each item's `ItemPrefab, Count, Level`.

## Persistence
- Trades themselves: **config-file + ServerSync only** (`SyncedTraderItemList`). Not in LiteDB.
- No per-player Trader state persists in LiteDB — trades execute against the player's live inventory at purchase time. (Trader is purely a recipe table; the only DB-backed economy collection is the auction `Marketplace` collection, unrelated to Trader configs.)

## NPC linkage
- Opened by `NPCType.Trader`. `Market_NPC.NPCcomponent.OpenUIForType` reads the NPC ZDO via `GET_NPC_Profile()`, **lowercases** it, and `Trader_UI.Show(profile, npcName)` looks up `SyncedTraderItemList.Value[profile]`.
- Because both the section key and the lookup are lowercased, the NPC's `Profile` ZDO string matches the `[name]` header case-insensitively.

## Gotchas / risks for the parser
- `.Replace(" ","")` strips **all** spaces from the whole line → prefab names and values cannot contain spaces.
- Section keys and the NPC profile lookup are **lowercased** — match case-insensitively.
- The `=` vs no-`=` decision is on the **raw line** (`profiles[index].Contains('=')`), made before the space-strip. A `bool`-style header is handled separately (starts with `[`), so `=` only matters on data lines.
- `int.Parse` (counts/level in Form A; `FillTrader` count) is **not** TryParse → throws on bad input. The catch does `break`, abandoning the rest of that file's lines (not just the bad line). Our importer should log-and-skip the line and continue.
- Form A token counts other than 4 or 5 are silently dropped (no error). Don't assume malformed lines error.
- `FillTrader` level detection is ambiguous-by-design: a 3rd token that happens to parse as int is consumed as a level. A prefab that is purely numeric would be misread.
- Max 5 items per side in Form B; excess silently dropped.
- "Currency" here is just whatever prefab you list (commonly `Coins`); there is no special currency field — it's an item like any other.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
