# Marketplace corpus — regression fixture

**Source:** live dedicated-server install, copied 2026-06-03 from
`C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\BepInEx\config\Marketplace`
(kg.Marketplace v9.8.1). This is real production config + data — including this server's own
**VerdantsAscent** content already running on Marketplace. It is the fixture Phase 2's compat engine is
validated against; the per-module grammar specs live in `../marketplace-schemas/`.

## Inventory (35 config files + DB.db)

| Folder | Files | Notes |
|---|---|---|
| Traders | 6 | incl. filenames with a space and an apostrophe (`Majestic_Trader Buy.cfg`, `Traders_Raven'sRest.cfg`) |
| Quests | 8 | Majestic_* + Verdants_* quest databases |
| QuestProfiles | 4 | |
| QuestEvents | 2 | |
| Dialogues | 4 | incl. a nested `Asgavin/` subfolder (proves recursive merge) |
| Gamblers | 2 | |
| Bankers | 1 | `bank.cfg` |
| Buffers / BufferProfiles | 1 / 1 | Majestic buff DB + profile |
| Transmogrifications | 1 | |
| ServerInfos | 1 | |
| LeaderboardAchievements | 1 | |
| Territories | 2 | incl. a 4,516-line `TerritoryDatabase_meadows.cfg` |
| **AdditionalTerritories, Teleporters, Lootboxes** | **0** | empty — consistent with "not implemented / unused in v9.8.1" |
| root | MarketPlace.cfg, DistancedUI/, PlayerTags/, DiscordWebhooks/ | |

## DB.db (90 KB LiteDB) — populated collections

- **`Leaderboard`** — 20 `Player_Leaderboard` rows (all fields present: Owner, PlayerName, MapExplored,
  KilledCreatures, KilledBy, ItemsCrafted, Harvested, DeathAmount, BuiltStructures).
- **`Users`** — 8 `CMS_User` rows (UserID, Name, LastOnline, IsOnline).
- `Bank`, `Marketplace`, `Mails` — empty on this server (no banked items / listings / mail). Fixture for
  those collections' read path will need synthetic data, or a server that has used them.

## Validation status (2026-06-03)

Three hardest specs cross-checked against this real data and confirmed accurate:
- **Quests** 7-line block + `=autocomplete` flag + 38 missing-`]` headers → matches `Quests.md` and source.
- **LiteDB `Player_Leaderboard` / `CMS_User`** shapes → match `DB/DB.cs` and the README schema.
- **Dialogue** command/condition vocabulary → matches `Dialogues.md`.

See `../marketplace-schemas/README.md` → "Validated against the real corpus" for the parser-robustness
requirements this fixture surfaced.

> Do not edit these files — they are a frozen fixture. If the server's live config changes and you want a
> fresh snapshot, re-copy and note the new date here.
