# QuestEvents — Marketplace config schema

## Config location
- Folder: `Configs/QuestEvents/*.cfg` (recursive merge — `Directory.GetFiles(QuestsEventsFolder, "*.cfg", AllDirectories)`, each via `File.ReadAllLines`)
- `QuestsEventsFolder` = `Configs/QuestEvents` (`Market_Paths.cs`)
- Autoload watch key / on-change handler: same `[Market_Autoload(...)]` on `Quests_Main_Server` — watch key **`QE`** → handler `OnQuestsEventFileChange` → `ReadEventDatabase()` → `SyncedQuestsEvents.Update()`, then `ParseAll()` on every event.

## Section header
- Parsed in `ProcessEventDatabase`. A line starting with `[` sets the current **event key** = `line.Replace("[","").Replace("]","").ToLower()`.
  - **NOTE: spaces are NOT stripped here** (unlike Quests/QuestProfiles which also `.Replace(" ","")`). Only `[`, `]` removed and `.ToLower()`. So a header `[My Event]` → key `"my event"` (with the space). Stored hash = `key.GetStableHashCode()`.
- No `=bool` form. Lines before any header default to event key **`"default"`**.
- **The event key is the SAME hash as a Quest's name** (`Quests.md`: quest UID = lowercased `[name]` header, but space-stripped there). Because QuestEvents does NOT strip spaces, an event only fires for a quest whose lowercased name (no spaces, per the quest parser) equals this key's hash. In practice: name your quest and its event identically with no spaces. Events are dispatched via `HandleQuestEvent(UID, condition)` keyed by the quest's UID hash → so the event `[key]` must hash-match the quest UID.

## Data-line grammar
Each non-comment, non-header line is **one condition-keyed command**:
```
<QuestEventCondition> : <command>
```
Processing (per line): `line.Split(':')`; requires `>= 2` parts. `parts[0]` = the trigger (`QuestEventCondition` enum, case-insensitive, must be defined). `parts[1]` = the command string, passed through `ReplaceSpacesOutsideQuotes()` (all spaces removed except inside `"..."`, and the quotes themselves removed). The command is **appended** to the matching list on the event object (`AddOnAcceptQuest` / `AddOnCancelQuest` / `AddOnCompleteQuest` / `AddOnQuestTimeout` / `AddOnDeath`). Multiple lines with the same trigger stack (executed in order).

> **Gotcha:** the split is on the FIRST-and-all `:` — `line.Split(':')` yields ALL colon-segments but only `parts[0]` and `parts[1]` are used. A command argument that itself contains `:` (e.g. `Teleport:100:0:200`) would be **truncated at the first colon** here — *but* the command parser (`ParseCommands`) treats `:` and `,` interchangeably (`command.Replace(":",",")`), so authors must use commas inside command args, e.g. `OnCompleteQuest : GiveItem,Coins,100,1`. The `parts[1]` you keep is everything after the first colon only IF there were exactly 2 segments; with extra colons `parts[1]` is just the second segment. **Faithful parser: take `parts[0]` as trigger and `parts[1]` (the second colon-segment) as the command; commands should not contain raw colons in args.**

### QuestEventCondition triggers (the 5 enum values)
`OnAcceptQuest, OnCancelQuest, OnCompleteQuest, OnQuestTimeout, OnDeath`. These fire from quest lifecycle:
- **OnAcceptQuest** — when the player accepts the quest (`AcceptQuest`, only if `handleEvent` true; `GiveQuest` dialogue command passes false to avoid recursion).
- **OnCompleteQuest** — on `RemoveQuestComplete`.
- **OnCancelQuest** — on `RemoveQuestFailed(UID, OnCancelQuest)` (player cancels an in-progress quest via the UI button).
- **OnQuestTimeout** — when a quest's `TimeLimit` elapses (`RemoveQuestFailed(UID, OnQuestTimeout)`).
- **OnDeath** — player death while holding the quest.

### Command grammar (the command after the `:`)
The command string is parsed by `Dialogues_DataTypes.Dialogue.ParseCommands` (shared with the Dialogues module). Grammar: `<OptionCommand>,arg1,arg2,...` — but note the parser does `command.Replace(":",",").Split(',')`, so `:` and `,` are interchangeable arg delimiters. `split[0]` is the command name (`OptionCommand` enum, case-insensitive); unknown/unparseable commands are skipped (logged). Full command vocabulary (`OptionCommand`):

| Command | Args | Effect |
|---|---|---|
| `OpenUI` | `[type][,profile]` | open an NPC UI (defaults to current NPC) |
| `PlaySound` | `clipName` | play a registered NPC audio clip |
| `GiveQuest` | `questName` | accept quest (by lowercased-name hash, `handleEvent:false`) |
| `RemoveQuest` | `questName[,fireCancelBool]` | drop a quest (optionally firing OnCancel) |
| `FinishQuest` | `questName` | force-complete a quest |
| `GiveItem` | `prefab,count,level` | add item |
| `GiveItemWithData` | `prefab,count,level,customDataKey` | add item w/ CustomSpawnZDO |
| `SetPlayerData` | `customDataKey` | write CustomSpawnZDO keys onto player ZDO |
| `RemoveItem` | `prefab,count` | remove items (level 1) |
| `Spawn` | `prefab,count,quality` | spawn near player (random ±15, floor-snapped) |
| `SpawnXYZ` | `prefab,count,quality,x,y,z,spread` | spawn at coords |
| `SpawnWithData` | `prefab,count,quality,customDataKey` | spawn near player + ZDO data |
| `SpawnXYZWithData` | `prefab,count,quality,x,y,z,spread,customDataKey` | spawn at coords + ZDO data |
| `Teleport` | `x,y,z[,ignoreTeleportableBool]` | teleport player |
| `Damage` | `amount` | damage player |
| `Heal` | `hp` | heal player |
| `GiveBuff` | `statusEffectName[,ttlSeconds]` | apply status effect |
| `AddPin` | `name,x,y,z` | add a map pin |
| `PingMap` | `text,x,y,z` | ping the map |
| `AddEpicMMOExp` | `amount` | EpicMMO API |
| `AddCozyheimExp` | `amount` | Cozyheim API |
| `PlayAnimation` | `trigger` | NPC animator trigger |
| `EnterPassword` | `title,password,onSuccessCmd,onFailCmd` | password gate |
| `GuildAddLevel` | `amount` | raise own guild level |
| `Battlepass_EXP` | `amount` | UL_B Battlepass via reflection |
| `ConsoleCommand` | `command string` | runs a Terminal command (`{playername}` substituted; debug mode toggled) |
| `AddCustomValue` | `key,amount` | player custom value += |
| `SetCustomValue` | `key,amount` | player custom value = (0 removes it) |
| `PlayVideo` | `name[,heightPercent]` | play a video clip |
| `AddRustyClassesEXP` | `amount` | RustyClasses API |
| `SendWebhook` | `urlWithoutScheme,text` | POST a Discord webhook (prepends `https://`, applies `{...}` replacements) |
| `AddPlayerKey` | `key` | player unique key |
| `RemovePlayerKey` | `key` | remove player unique key |
| `SetPlayerGuild` | `guildNameOrId` | move player into a guild |
| `SetPlayerGuildRank` | `rankName` | set player's guild rank |
| `SetNPCModelLocal` | `prefab` | change this NPC's model (client only) |
| `SetNPCModelGlobal` | `prefab` | change NPC model for everyone (RPC) |
| `SetNPCNameLocal` | `name` (`_`→space) | rename NPC locally |
| `SetNPCNameGlobal` | `name` (`_`→space) | rename NPC for everyone (RPC) |
| `SetNPCPatrol` | `patrolData` | set NPC patrol |
| `SetFaction` | `factionName` | set player faction (customData `MPASN_Faction`) |
| `RemoveFaction` | — | clear player faction |

Note: `npc`-targeting commands receive a **null NPCcomponent** when fired from a quest event (`HandleQuestEvent` invokes with `(Market_NPC.NPCcomponent)null`), so `SetNPC*`/`PlayAnimation`/`OpenUI`-with-npc behave as the null-NPC fallback path.

### Example
```cfg
# fires when the player accepts/finishes the quest named "wolf_cull"
[wolf_cull]
OnAcceptQuest : PlaySound,horn_blow
OnAcceptQuest : PingMap,"Hunt them here",1500,0,-2300
OnCompleteQuest : GiveItem,Coins,500,1
OnCompleteQuest : SendWebhook,discord.com/api/webhooks/123/abc,"A hunter finished the cull!"
OnDeath : Damage,9999
OnQuestTimeout : RemoveQuest,wolf_cull,true
```

## Domain object
`Quests_DataTypes.QuestEvent : ISerializableParameter`. Holds five `List<string>` of raw command strings (`args_OnAcceptQuest` … `args_OnDeath`) plus five compiled `Action<Market_NPC.NPCcomponent>` delegates (`OnAcceptQuest` … `OnDeath`). `ParseAll()` (server, only when `WorkingAs.Both`) and `Deserialize` (clients) compile the raw lists into delegates via `Dialogue.ParseCommands`. Serialize/Deserialize write each list as `[count]` then the strings.

Synced container: `CustomSyncedValue<Dictionary<int, QuestEvent>> SyncedQuestsEvents` (ServerSync key `"questEvents"`), keyed by `eventKey.GetStableHashCode()` (= the quest UID hash it attaches to). Cleared+rebuilt on every `ReadEventDatabase()`. Multiple lines append into the same event object (`if (!ContainsKey) create`, else reuse).

## Persistence
None of its own. QuestEvents are server config synced to clients; the actual side-effects (items, buffs, custom values, faction, guild) land in the usual places (player inventory / `customData` / guild DB) when a command runs — but the event definition itself is not persisted per-player. Triggers fire from the quest lifecycle which itself is tracked in player `customData` (`Quests.md`).

## NPC linkage
Indirect: an event is keyed by a quest UID hash (the `[header]` lowercased), and that quest is offered by NPCs via QuestProfiles (`QuestProfiles.md`). The event is not bound to an NPC directly — it fires whenever *any* player triggers the lifecycle condition on the matching quest. Commands that target an NPC get `null` (the fallback path).

## Gotchas / risks for the parser
- **Header is lowercased but NOT space-stripped** (differs from Quests/QuestProfiles). To match a quest, keep quest+event names space-free.
- **`Split(':')` truncation:** the trigger/command split is on `:`; only segments [0] and [1] are used. Command args MUST use commas (the command parser treats `:`≡`,` anyway). Don't put a raw `:` mid-command in the cfg or it gets cut at the second segment.
- **`ReplaceSpacesOutsideQuotes` on the command:** spaces survive only inside `"..."` (and the quotes are removed). So `PingMap,"Hunt them here",...` keeps the spaces in the label; an unquoted space is deleted.
- **Command/condition parser is the shared Dialogues mini-language** — see the command table above; the *condition* mini-language (used by Quest `Conditions` and Dialogue option gating, not by QuestEvents triggers) is the `OptionCondition` enum with a leading `!` negation. Document both if implementing the full engine.
- **`{playername}` / `{...}` substitutions:** `ConsoleCommand` substitutes `{playername}`; `SendWebhook` runs all `Dialogues_UI._replacements`. Faithful execution needs these tables.
- **Unknown commands/conditions are silently skipped** (try/catch logs to console). Match that lenient behavior — don't abort the file.
- **Five trigger lists are append-order-sensitive** — execution order = file/line order across the merged folder.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
