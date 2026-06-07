# Banker — Marketplace config schema

## Config location
- Folder: `Configs/Bankers/*.cfg` (recursive merge — `SearchOption.AllDirectories`, all files merged)
- Autoload watch key / on-change handler: `[Market_Autoload(Server, Normal, "OnInit", new[]{"BA"}, new[]{"OnBankerProfilesFileChange"})]` — watch key `BA`.

## Section header
- `[name]` means: banker **profile name** (the section key). Stored stripped of `[`, `]`, all spaces, **lowercased**. Default key before any header is `"default"`.
- `[name=bool]` flag means: **NOT SUPPORTED by Banker.** The header parser does NOT split on `=` — it just strips brackets/spaces and lowercases the entire token. So a header written as `[merchant=true]` produces the literal key `merchant=true`, not a flag. There is no per-section boolean concept in Banker.

## Data-line grammar
A data line is a single **allowed item prefab name** — one token, no delimiters.
- The line is `.Replace(" ","")` then converted directly to its **stable hash**: `profile.Replace(" ","").GetStableHashCode()` (Valheim's `string.GetStableHashCode`).
- The resulting `int` hash is appended to the current section's `List<int>`.
- No `int.Parse`, no `Split`, no field-count branches — every non-comment, non-header line is exactly one prefab name. The list is simply the set of prefabs this banker profile permits a player to deposit/withdraw.

### Example lines
```cfg
[default]
Coins
Wood
Stone

[ironbank]
Coins
SilverNecklace
```

## Domain object
- Synced container: `CustomSyncedValue<Dictionary<string, List<int>>> SyncedBankerProfiles` (ServerSync key `"bankerData"`). Key = profile name; value = list of **stable-hash ints** of allowed prefab names.
- Client cache: `Dictionary<int, int> BankerClientData` (prefab hash → amount the local player currently has banked), populated from a server RPC, not from cfg.
- There is no per-line struct — the cfg only contributes the allowed-prefab hash list.

## Persistence
- **Allowed-prefab lists**: config-file + ServerSync only (`SyncedBankerProfiles`).
- **Player balances**: LiteDB collection **`Bank`**, entity `BankSlot { int _id; string Owner; int Prefab; int Amount }`, index on `Owner`.
  - `Owner` = the player's platform user id **string** (server uses `peer.m_socket.GetHostName()`; in single-player the local `PlatformUserID.m_userID`).
  - `Prefab` = `item.GetStableHashCode()` (**int stable hash**, same hashing as the cfg allowed-list).
  - One row per `(Owner, Prefab)`; deposits merge into the existing row (`AddBankItem` finds-one-or-inserts then adds `Amount`).
  - `GetUserBankItems(userID)` returns `Dictionary<int,int>` (prefab hash → amount); sent to the client JSON-compressed.
- **Interest/income** (optional, `Global_Configs.BankerIncomeTime > 0`): a coroutine periodically calls `DB._context.IncreaseInterest(vipSet, BankerTimeStamp)`; VIP set comes from `SyncedGlobalOptions._vipPlayerList` (comma-split string). `BankerTimeStamp` is an in-memory `Dictionary<hostname, Dictionary<prefabHash, DateTime>>` (last deposit/withdraw time), not persisted.

## NPC linkage
- Opened by `NPCType.Banker`. `OpenUIForType` reads `GET_NPC_Profile()` ZDO string, **lowercases** it, calls `Banker_UI.Show(profile, npcName)` → looks up `SyncedBankerProfiles.Value[profile]` for the allowed-prefab hash list.

## Gotchas / risks for the parser
- **No `=bool` flag** — unlike Trader/Gambler. Treat the entire bracket contents as the key. If a config author wrote `[x=true]` expecting a flag, the key literally becomes `x=true`.
- Each data line is hashed **immediately**; the cfg never stores the prefab string for bankers. To know which prefab a hash means, you must hash candidate prefab names yourself (`GetStableHashCode`) and match. Our importer must use the identical stable-hash algorithm.
- `.Replace(" ","")` strips all spaces; prefab names can't contain spaces (true for vanilla item prefabs anyway).
- Section keys lowercased; NPC lookup lowercased — match case-insensitively.
- No per-line error handling shown (no try/catch) — a malformed line just becomes a hash of garbage and is added; there is no validation that the prefab exists.
- LiteDB `Owner` is the **string** user id (not an int hash) — different keying from the `Prefab` field in the same row.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
