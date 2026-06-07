# Marketplace drop-in compatibility contract

Reverse-engineered from `kg.MarketplaceREF` (kg.Marketplace v9.8.1.0). This is the **contract**
Phase 2's compat engine must satisfy: drop a Marketplace `BepInEx/config/Marketplace/` tree (configs
**and** `SavedData/DB.db`) in, and FiresNPCs reads it unchanged.

Per-module data-line grammars live in the sibling `<Module>.md` files. This README documents the parts
**common to all modules**: the on-disk layout, the shared `.cfg` skeleton, and the LiteDB schema.

> ⚠️ There is **no single shared `.cfg` serializer.** Every module's `_Main_Server.cs` hand-parses its
> own data-line syntax. The only thing they share is the skeleton below (comments + `[section]` headers).
> The drop-in parser must be **per-module over a common skeleton**, not one generic deserializer.

---

## On-disk layout (exact — from `Marketplace/Paths/Market_Paths.cs`)

Root = `BepInEx/config/Marketplace/` (`MainPath`). Server creates these on init:

```
BepInEx/config/Marketplace/
├── MarketPlace.cfg                         Global options (BepInEx CFG, bound via Config.Bind)
├── Factions.yml                            Factions (YAML — the only top-level YAML)
├── SavedData/
│   └── DB.db                               LiteDB. Path overridable via "Database/Database File Path".
├── Logger.log
├── DiscordWebhooks/DiscordSettings.cfg
├── DistancedUI/DistancedUI.cfg
├── PlayerTags/PlayerTags.cfg
└── Configs/
    ├── Traders/*.cfg
    ├── Bankers/*.cfg
    ├── Teleporters/*.cfg
    ├── Gamblers/*.cfg
    ├── ServerInfos/*.cfg
    ├── Transmogrifications/*.cfg
    ├── QuestProfiles/*.cfg
    ├── Quests/*.cfg
    ├── QuestEvents/*.cfg
    ├── Dialogues/*.cfg
    ├── CustomSpawnData/*.yml                (YAML — NPC spawn ZDO data)
    ├── Territories/*.cfg
    ├── AdditionalTerritories/*.cfg
    ├── LeaderboardAchievements/*.cfg
    ├── BufferProfiles/*.cfg
    ├── Buffers/*.cfg                        (buff database)
    └── Lootboxes/*.cfg                      (MainMarketplace lootboxes)
```

Client-side folders (cosmetic asset caches, not config): `Marketplace_Sounds/`, `Marketplace_CachedImages/`,
`Marketplace_SavedNPCs/`, `Marketplace_VideoClips/`.

**Per-folder reading:** modules call `Directory.GetFiles(<Folder>, "*.cfg", SearchOption.AllDirectories)`
then `File.ReadAllLines(file)`. So **all `.cfg` files in a module's folder (recursively) are merged** —
filename is irrelevant; the `[section]` headers inside disambiguate profiles. Our parser must replicate
recursive-merge-by-folder, not one-file-per-profile.

**Hot-reload:** each module's `[Market_Autoload(... new[]{"<watchKey>"}, new[]{"<OnChange>"})]` registers a
file watcher; on change the module re-reads its whole folder and re-syncs. We replicate via our
`ServerConfigFileWatcher`.

---

## The shared `.cfg` skeleton (from `Trader_Main_Server.ProcessTraderProfiles`, identical pattern across modules)

Every module's line loop applies these rules before its own grammar:

1. **Skip** lines that are null/empty/whitespace, or start with `#` (comments).
2. **Section header**: a line starting with `[`.
   - `[name]` → sets current section key = `name` (stripped of `[`, `]`, spaces, **lowercased**).
   - `[name=bool]` → section key = `name`, plus a per-section boolean flag (Trader calls it `NeedToKnow`;
     semantics are module-specific — usually a visibility/gating flag). Parsed via `Convert.ToBoolean`.
   - A header with no `=` resets the flag to `false`.
3. **Data line**: anything else → parsed by the **module-specific grammar** (see `<Module>.md`), appended
   to the current section's list. Most modules `.Replace(" ", "")` (strip ALL spaces) before splitting,
   so whitespace inside values is not preserved — values are comma/`=`-delimited tokens.
4. **Errors** are caught per-line and logged as `Can't add ... in {file}, line: {n}` then `break` (the rest
   of that file is skipped). Our parser should log-and-skip per line, not abort the whole load.

The section key is the **profile name** an NPC references via its ZDO `Profile` string (see `NPC.md`).

### Worked example — Trader (`Configs/Traders/*.cfg`)

Section = trader profile name. Data-line variants (after `.Replace(" ","")`):
- **5 CSV tokens**: `NeededPrefab,NeededCount,ResultPrefab,ResultCount,ResultLevel`
- **4 CSV tokens**: `NeededPrefab,NeededCount,ResultPrefab,ResultCount` (level defaults)
- **`=` form** (multi-item trade, ≤5 items per side): `need1,cnt1,need2,cnt2,...=result1,cnt1,lvl1,...`
  — each side is CSV of `prefab,count[,level]` triples/pairs; `level` is taken when the 3rd token parses as int.

```cfg
# Bandit trader
[bandit_trader=false]
Coins,50,SwordBronze,1,2
Wood,10,Stone,5
LeatherScraps,5,Coins,25 = ArmorBronzeChest,1,3
```

---

## LiteDB schema (from `Marketplace/DB/DB.cs`)

- **File:** `SavedData/DB.db`. **Connection:** `LiteDB.ConnectionType.Shared`.
- **BsonMapper.Global:** `IncludeFields = true`, `EmptyStringToNull = false`, `TrimWhitespace = false`.
  (Our reader MUST set these identically or field-backed types deserialize wrong.)
- Auto-id: `BsonAutoId.ObjectId` is passed to `GetCollection`, but every entity uses an **`int _id`**.

| Collection | Entity | Key fields / indexes | Notes |
|---|---|---|---|
| `Marketplace` | `MarketSlot` | index `ItemPrefab` | Auction listings. Fields: `_id, ItemPrefab, Count, Price, SellerName, SellerUserID, ItemCategory(enum int), Quality, Variant, CrafterName, CrafterID(long), CUSTOMdata(json="{}"), DurabilityPercent(byte), TimeStamp(uint), Currency(="Coins")`. |
| `Bank` | `BankSlot` | index `Owner` | Banker storage. `{ _id, Owner(userID string), Prefab(int = stable hash), Amount(int) }`. One row per (owner, prefab); amounts merged on deposit. |
| `Users` | `CMS_User` | index `UserID` | Mail accounts. `{ _id, Name, UserID, LastOnline(long), IsOnline(bool) }`. |
| `Mails` | `CMS_Entry` | index `Owner` | Mail. `{ _id, Owner, Created(long), Sender, Topic, Message, Attachments(List<ClientMarketSendData>), Links(...), ExpireAfterMinutes(=10800), WasRead, AttachmentsTaken }`. Attachments/Links persisted as fastJSON inside ZPackage on sync. |
| `Leaderboard` | `Player_Leaderboard` | index `Owner` | `{ _id, Owner, PlayerName, MapExplored(float), DeathAmount(int), + 5 Dictionary<string,int>: KilledCreatures, BuiltStructures, ItemsCrafted, KilledBy, Harvested }`. `KilledPlayers` is `[BsonIgnore]` (derived from KilledCreatures["Player"]). |

**Bank prefab key:** stored as the **`int` stable hash** of the prefab name (`prefab.GetStableHashCode()`),
not the string. Our compat reader must hash the same way.

**Not in LiteDB:** quest progress, per-player stats/cooldowns, transmog presets, faction membership — these
sync via `CustomSyncedValue<T>`/ServerSync and persist elsewhere (per-module — see each `<Module>.md`;
several ride on player `customData` / saved-character data). Confirm per module.

---

## `ISerializableParameter` (the ServerSync wire convention)

Many config/data types implement `ISerializableParameter { void Serialize(ref ZPackage); void Deserialize(ref ZPackage); }`
and are distributed to clients via `CustomSyncedValue<T>` (ServerSync). For **drop-in we honor the config
files + DB, NOT this wire protocol** — our own clients run our mod and use Core `ConfigSync`. The serialize
methods are still useful as the authoritative field list/order when reverse-engineering a type's shape.

---

## Per-module schema docs (filled by Phase 0 fan-out)

Each documents: config folder, section-header semantics (incl. the `=bool` flag meaning), exact data-line
grammar with field types, the resulting domain object, any LiteDB/customData persistence, and the NPC
`Profile` linkage. Status checkbox: `[ ] format documented → [ ] importer written → [ ] verified-in-game`.

- Trader, Banker, Gambler, Teleporter
- Quests, QuestEvents, QuestProfiles
- Dialogues, NPC, CustomSpawnData
- Buffer (+BufferProfiles), Transmogrification, ServerInfo
- TerritorySystem (+AdditionalTerritories), Factions, Leaderboard (LeaderboardAchievements)
- MainMarketplace (+Lootboxes), CMS/Mail, DistancedUI, PlayersTag, KG_Chat, Global_Options (MarketPlace.cfg)

---

## ★ Critical cross-cutting findings (Phase 0 reverse-engineering, 2026-06-03)

These came out of documenting all 22 areas. Several **correct earlier assumptions** — read before writing the parser or the storage layer.

### Persistence is HETEROGENEOUS — not LiteDB-only (corrects the master plan)

The Phase-1 storage layer cannot be "LiteDB and done." Per-player state lives in **four** places:

| Data | Where it persists | Key |
|---|---|---|
| Bank items | LiteDB `Bank` | `Owner`(userID str) + `Prefab`(**int stable-hash**) |
| Auction listings | LiteDB `Marketplace` | `_id`; index `ItemPrefab` |
| Mail + mail users | LiteDB `Mails` / `Users` | `Owner` / `UserID` |
| Leaderboard stats | LiteDB `Leaderboard` | `Owner` = `"{hostName}_{playerName}".Trim()` |
| **Quest progress + cooldowns** | **Player `m_customData`** (saved with character) | `"[MPASN]quest=<UID>"` → `"score0,score1,…;acceptedTime"`; `"[MPASN]questCD=<UID>"` → completion day. *No completed-list; cooldown = key presence.* |
| **Faction membership** | **Player `m_customData`** + a ZDO int index | `"MPASN_Faction"` → lowercased faction id |
| **Transmog (per item)** | **ItemDataManager item custom-data** (saved inventory) | `TransmogItem_ComponentV2` → `ReplacedPrefab` + `ItemColor`; color mirrored to vanilla `colorID` ZDO at render |
| **NPC identity + appearance** | **World ZDO** on the `MarketPlaceNPC` instance | `"KG…"` keys (see `NPC.md`; some keys contain literal spaces) |

→ Drop-in quest/faction compat means **reading/writing the exact `[MPASN]…` customData keys**, not a DB table.
Transmog means hooking ItemDataManager. NPC drop-in means reading the `KG…` ZDO keys. The Core storage
layer wraps LiteDB **plus** a player-customData accessor; it is not a single backend.

### The `.cfg` format is a family of per-module grammars, not one format

- **Section-header variants:** `[name=bool]` (Trader `NeedToKnow`), `[name=int]` (Gambler `MAXROLLS`),
  `[name@priority]` (Territory), **no flag** (Banker, Teleporter — `=` is NOT split, so `[x=true]` is a
  literal key), colon `userID : tag` (PlayersTag), and plain BepInEx INI (DistancedUI, Global_Options).
- **Fixed-line-block records** (offset-addressed, NOT delimited) in Quests (7), Buffer DB (7), Territory
  (5), Leaderboard (6). A comment or blank line *inside* a block misaligns every field after it. Our
  parser must treat these as positional blocks (and ideally warn on misalignment rather than silently corrupt).
- **Merge semantics differ per module:** append (Trader, Transmog), first-write-wins (BufferProfiles),
  last-line-assigns/replace (Gambler), last-write-wins overwrite (ServerInfo, Territory sidecars). Replicate each.
- **Whitespace:** most do `.Replace(" ","")` (values can't contain spaces); **exceptions** preserve spaces —
  Teleporter pin names, ServerInfo text, and Talk/Move quest targets (quoted-space-aware).
- **Stable-hash keying is inconsistent:** Bank prefab = int hash; Faction item/building/monster lists =
  hashed at runtime; **Leaderboard `Achievement.Prefab` = plain string** (not hashed). Match per module.
- **Error handling:** a bad parse usually `break`s the *rest of the file* (Trader/Teleporter/Quests), and
  ServerInfo's `<image=` parse has **no try/catch** (a malformed tag aborts the whole file).

### The dialogue/quest-event command language is the real surface area

Dialogue options and quest-event hooks share one mini-language: **~41 `OptionCommand` verbs** (give/finish
quests, `OpenUI` into any module, spawn-with-ZDO-data, teleport, give items/buffs, run console commands,
post webhooks, mutate guild/faction/NPC state, third-party EXP via reflection: EpicMMO/Cozyheim/RustyClasses/
Battlepass) gated by **~35 `OptionCondition` verbs** (leading-`!` negation via `0x80` bit; AND-of-OR-groups,
OR-separator is server-configurable). Inside commands/conditions `:` and `,` are interchangeable delimiters.
**This vocabulary — not the per-module cfg grammars — is the largest single implementation task in Phase 2.**
Full lists in `Dialogues.md` / `QuestEvents.md`.

### Things NOT implemented in v9.8.1 (don't build compat for them)

- **Lootboxes** (`Configs/Lootboxes/`): folder + file-watch + webhook category exist, but **nothing parses
  the cfg**. The live analogue is the Gambler roll-table. Create the empty folder for parity; read nothing.
- **`AdditionalTerritories/`**: created on init but never registered/read. `AdditionalTerritoryFlags` are
  parsed inline from the same flag line in `Territories/*.cfg`. Make the empty dir; read nothing.
- **Groups** (separate from Guilds): stub API returning empty in this version.

### Validated against the real corpus (2026-06-03) — parser-robustness requirements

Cross-checked the specs against a live production corpus (`../marketplace-corpus/`, kg.Marketplace v9.8.1,
incl. this server's VerdantsAscent content). The specs held up; real configs additionally proved the parser
must be **tolerant and fail-soft**, because production files are full of human error:

- **Missing closing `]` on headers is pervasive** — 38 instances across Quests/Dialogues/QuestEvents/
  QuestProfiles (`[MeetHenryTank`, `[Roles`, …). They work because every module keys via
  `.Replace("[","").Replace("]","")` (strip-if-present), **not** a `[...]` regex. Our parser MUST strip
  brackets, never regex-match — a strict matcher would silently mis-key ~1.5% of real sections.
- **Bare condition/command verbs without the `Condition:`/`Command:` label** appear in real dialogue
  options (`… | NotHasPlayerKey, DPS | NotHasPlayerKey, HEALER`). The option-token parser recognises a
  token by its leading keyword; an unlabeled token whose first word is a known verb is still honored.
- **`##` lines** are used as disabled/commented-out option lines (still caught by the `#`-prefix skip).
- **Verbs are case-insensitive** (`spawnRaw` vs `SpawnRaw` both work).
- **Multiple `Command:` / `Condition:` / `Icon:` tokens per option** are normal (commands run in order;
  conditions AND together; last Icon wins).
- **Empty tokens between `||` / trailing `|`** occur (`…anymore...| |`) — skip empties, don't error.
- **Real typos exist** (`Transition edvarRogue` missing its colon) — that one option silently fails in MP.
  Match MP: fail **per-option / per-line**, never abort the file for one bad token (except where MP itself
  `break`s — Trader/Teleporter/Quests do abort the rest of a file on a numeric parse throw; replicate that
  specific behavior, but prefer log-and-skip everywhere MP doesn't hard-break).
- **Filenames with spaces/apostrophes** (`Majestic_Trader Buy.cfg`, `Traders_Raven'sRest.cfg`) and
  **nested subfolders** (`Dialogues/Asgavin/…`) are real — confirms recursive `GetFiles(..., AllDirectories)`
  merge; never key off filename.
- **Empty folders** `AdditionalTerritories/`, `Teleporters/`, `Lootboxes/` in production corroborate those
  features being unused/unimplemented in v9.8.1.

### Other traps for the importer
- Bank/Marketplace currencies + VIP/blocked lists funnel through `MarketPlace.cfg` / `Global_Configs`
  (`_possibleCurrencies`, `_marketTaxes`, `_vipPlayerList`, `_blockedPrefabsServer`, banker-interest keys).
  VIP/blocked use raw-string **`Contains` substring** tests (short IDs false-match). `_possibleCurrencies`
  is re-validated against live prefabs at `ZNetScene.Awake` (missing prefabs silently dropped).
- KG_Chat cosmetic keys live in a **separate** `kg.Marketplace.cfg`, not `MarketPlace.cfg`.
- `API_NPCType` (public) is missing `Mail` (11 values) vs the internal `NPCType` (12).
- LiteDB `BsonMapper.Global` MUST be set `IncludeFields=true, EmptyStringToNull=false, TrimWhitespace=false`
  or field-backed entities (e.g. `Player_Leaderboard`'s 5 dictionaries) deserialize wrong.
