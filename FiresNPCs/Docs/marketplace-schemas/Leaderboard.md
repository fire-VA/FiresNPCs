# Leaderboard — Marketplace config schema

Two halves: a CFG **achievement definition** folder (`LeaderboardAchievements/`) and the LiteDB
**`Leaderboard`** collection of per-player stats. Decompiled from
`Marketplace/Modules/Leaderboard/Leaderboard_Server_Main.cs`, `Leaderboard_DataTypes.cs`, and
`Marketplace/DB/DB.cs` (kg.Marketplace v9.8.1.0).

## Config location
- **Folder:** `BepInEx/config/Marketplace/Configs/LeaderboardAchievements/` — read recursively
  (`Directory.GetFiles(LeaderboardAchievementsFolder, "*.cfg", SearchOption.AllDirectories)`), all
  `.cfg` files **merged**. Format: custom CFG (hand-parsed).
- **Autoload / watch:** `[Market_Autoload(Server, Last, "OnInit", {"LA"}, {"OnAchievementsFileChange"})]`.
  Watch key `"LA"` → `FoldersToFiles[LeaderboardAchievementsFolder] = "LA"`. `OnInit` reads
  achievements, pushes to clients, and starts a 300s `UpdateLB` coroutine that re-evaluates everyone
  and re-syncs the leaderboard. `OnAchievementsFileChange()` re-reads the folder.
- Gated by the global option `Global_Configs.SyncedGlobalOptions._useLeaderboard` — when off, stat
  events are dropped and the periodic refresh is skipped.

## Section header / structure
Per-line scan (`ProcessAchievementsProfile`). Skips blank/whitespace and `#` comments.
- **Section header:** any line starting with `[`. Stripped of `[`/`]`, **lowercased**, and hashed:
  `ID = "[name]".Replace("[","").Replace("]","").ToLower().GetStableHashCode()`. The display name is
  NOT the header — it is data line 2 (see below). So the bracket text is purely the **stable ID seed**.
- **Data block:** an achievement is **exactly 6 consecutive data lines** after the header, read by
  absolute offset (`profiles[index] .. profiles[index+5]`). On success `index` advances past them;
  on a non-parsable trigger or exception `index += 5` skips the block and logs.

```
[bossbane]
MonstersKilled          # line 0: TriggerType
Boss Slayer             # line 1: Name (display)
Kill 10 Bonemass        # line 2: Description
Bonemass, 10            # line 3: target (prefab,amount) OR bare amount (see below)
255, 215, 0             # line 4: RGB color
500                     # line 5: Score
```

## Data-line grammar (the 6 lines)
- **Line 0 — `TriggerType`** (enum, case-insensitive `Enum.TryParse`). If it doesn't parse, the block
  is skipped (`index += 5`).
- **Line 1 — Name** (display string, raw).
- **Line 2 — Description** (raw string).
- **Line 3 — target:**
  - For `Died`, `Explored`, `PlayersKilled` → a **bare int** `MinAmount` (`int.Parse(line)`).
  - For all other types → `prefab, amount` (spaces stripped, split on `,`): `Prefab = tok[0]`,
    `MinAmount = int.Parse(tok[1])`. `Prefab` is the **string key** matched against the player's
    per-prefab stat dictionaries (NOT hashed).
- **Line 4 — Color** (`r,g,b`, spaces stripped) → `Color32(r,g,b,255)` (`byte.Parse`).
- **Line 5 — Score** (`int.Parse`).

### `TriggerType` enum (values matter — wire type ints)
| Name | Value | Checked against `Player_Leaderboard` |
|---|---|---|
| MonstersKilled | 0 | `KilledCreatures[Prefab] >= MinAmount` |
| ItemsCrafted | 1 | `ItemsCrafted[Prefab] >= MinAmount` |
| StructuresBuilt | 2 | `BuiltStructures[Prefab] >= MinAmount` |
| Died | 3 | `DeathAmount >= MinAmount` (bare amount) |
| KilledBy | 4 | `KilledBy[Prefab] >= MinAmount` |
| Explored | 5 | `MapExplored >= MinAmount` (bare amount; float vs int) |
| Harvested | 6 | `Harvested[Prefab] >= MinAmount` |
| PlayersKilled | 100 (0x64) | `KilledPlayers >= MinAmount` (bare amount) |

Note the **gap**: `PlayersKilled = 100`, the rest 0–6. The RPC stat-ingest path
(`RPC_ReceiveLeaderboardEvent`) handles 0–6; `PlayersKilled` is derived, not ingested directly.

After load, `AllAchievements` is sorted **descending by Score** and a client copy
(`Client_Achievement`: ID, Name, Score, Description, Color) is pushed via
`CustomSyncedValue<List<Client_Achievement>> SyncedClientAchievements` (key `"clientAchievements"`).

## Domain object — `Leaderboard_DataTypes.Achievement`
`ID(int stable-hash), Name, Description, Prefab, MinAmount, Color(Color32), Score, Type(TriggerType)`.
`Check(Player_Leaderboard)` → bool, dispatched on `Type` per the table above.

## Persistence — config defines achievements, LiteDB holds per-player stats
- **Achievements:** config-only (`LeaderboardAchievements/*.cfg`), never persisted to DB; rebuilt from
  CFG each load and ServerSync'd.
- **Per-player stats:** LiteDB collection **`Leaderboard`**, entity **`Player_Leaderboard`**
  (`DB/DB.cs`), index on **`Owner`**:
  ```
  Player_Leaderboard {
    int _id;                         // int id (BsonAutoId.ObjectId passed but int used)
    string Owner;                    // = "{hostName}_{playerName}".Trim()  (lookup/index key)
    string PlayerName;
    float MapExplored;
    int DeathAmount;
    Dictionary<string,int> KilledCreatures;   // prefab -> count
    Dictionary<string,int> BuiltStructures;
    Dictionary<string,int> ItemsCrafted;
    Dictionary<string,int> KilledBy;
    Dictionary<string,int> Harvested;
    [BsonIgnore] int KilledPlayers => KilledCreatures.TryGet("Player");  // derived, NOT stored
  }
  ```
- **Link:** the CFG `Achievement.Prefab` string is the **dictionary key** into the player's
  `KilledCreatures` / `ItemsCrafted` / `BuiltStructures` / `KilledBy` / `Harvested` maps; `MinAmount`
  is the threshold; `Achievement.Check(player_lb)` decides if the player earned it. The server's
  `SendToPlayers()` builds a `Client_Leaderboard` per player (summing each dict's values into single
  ints for the scoreboard view) plus the list of earned achievement IDs, and pushes the whole map via
  `CustomSyncedValue<Dictionary<string,Client_Leaderboard>> SyncedClientLeaderboard`
  (key `"clientLeaderboard"`).
- **Stat ingest:** client RPC `Marketplace_Leaderboard_Receive(type, pkg)` →
  `GetOrCreateLeaderboard("{host}_{playerName}".Trim(), playerName)`, increments the matching map/field
  (reads a prefab string from the pkg for the per-prefab types; `Explored` reads a float), then
  `UpdateLeaderboard`. `KilledPlayers` is just `KilledCreatures["Player"]`, so player kills are logged
  as a "MonstersKilled" event with prefab `"Player"`.

## NPC linkage
None. Achievements key off prefab names / map dictionaries, not NPC `Profile` strings.

## Gotchas / risks for the parser
- **Fixed 6-line block** by absolute offset (not delimited). The bracket header contributes only the
  **lowercased-hashed ID**; the visible Name is line 1. A missing line shifts/skips the block
  (`index += 5` on failure).
- Line 3 shape depends on `TriggerType`: bare int for `Died`/`Explored`/`PlayersKilled`, else
  `prefab,amount`. Branch on the parsed type.
- `Prefab` is a **plain string key** (matched against the player's stat dicts), **not** a stable hash —
  unlike Factions/Bank which hash prefab names. Do not hash it.
- LiteDB `Owner` key format is `"{hostName}_{playerName}"` trimmed — our reader must compose the same
  key to correlate a player. Honor the global `BsonMapper` settings from the README
  (`IncludeFields=true`, dictionaries are field-backed).
- `KilledPlayers` is `[BsonIgnore]`/derived from `KilledCreatures["Player"]`; don't expect a stored
  column. `TriggerType.PlayersKilled = 100` is a non-contiguous value.
- Color line is `byte.Parse` (0–255) RGB; alpha is forced 255.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
