# Quests — Marketplace config schema

## Config location
- Folder: `Configs/Quests/*.cfg` (recursive merge — `Directory.GetFiles(QuestsDatabaseFolder, "*.cfg", AllDirectories)`, each via `File.ReadAllLines`)
- `QuestsDatabaseFolder` = `Configs/Quests` (`Market_Paths.cs`)
- Autoload watch key / on-change handler: `[Market_Autoload(Server, Normal, "OnInit", {"QP","QD","QE"}, {"OnQuestsProfilesFileChange","OnQuestDatabaseFileChange","OnQuestsEventFileChange"})]`. This folder = watch key **`QD`** → handler `OnQuestDatabaseFileChange`, which **debounces 3 seconds** (`DelayMore` coroutine) before re-reading, then `SyncedQuestData.Update()`. (The other two keys QP/QE belong to QuestProfiles/QuestEvents.)
- On reload `ReadQuestDatabase` also randomizes `SyncedQuestRevision` (`questRevision`) to an `int` to force clients to refresh.

## Section header
- Parsed in `ProcessQuestDatabaseProfiles`. A line starting with `[` is the **quest UID/name** header. Stripped of `[`, `]`, all spaces, then **lowercased**: `key = line.Replace("[","").Replace("]","").Replace(" ","").ToLower()`.
- `[name=bool]` form: after lowercasing, split on `=`. If `parts.Length==2` and `parts[1]` parses as the `SpecialQuestTag` flags enum (case-insensitive), the flag is **OR'd** into the running `SpecialTag` and `key` becomes `parts[0]`. The only defined tag is `Autocomplete` (`= 1`). Any header **without** `=` resets `SpecialTag` back to `None`. So `[mykill=autocomplete]` → quest auto-completes the moment its score is met (no NPC turn-in needed).
- The quest UID stored as dictionary key = `key.GetStableHashCode()` (int). NPCs/QuestProfiles reference quests by this same lowercased-name stable hash.

## Data-line grammar
A quest is **NOT one line** — it is a fixed block of **7 consecutive non-comment lines** following the `[header]`, read by index offset (`profiles[index1+0 .. +6]`), with an **optional 8th line**. Comments (`#`) and blank lines are skipped by the outer loop but the inner block reads raw `index1+N` offsets, so **do not interleave comments/blanks inside a quest block** — offsets are literal. Order:

| Offset | Field | Meaning |
|---|---|---|
| +0 | `QuestType` | enum, case-insensitive; must be defined or quest skipped |
| +1 | Name (+ optional `<image=...>`) | display name; embedded `<image=PATH>` is extracted into `PreviewImage` and removed from the name |
| +2 | Description | free text |
| +3 | Targets | `|`-separated target list (grammar depends on Type, below) |
| +4 | Rewards | `|`-separated reward list (`RewardType:args`) |
| +5 | `Cooldown[,TimeLimit]` | CSV; `Cooldown`=int days, `TimeLimit`=int (default `0`) |
| +6 | Conditions | `|`-separated AND groups, each group `||`-separated OR alternatives |
| +7 *(optional)* | `TransferQuestName[,TransferText]` | "transfer completion" chain — only read if present and not a `[` header |

### QuestType enum (`Kill, Collect, Harvest, Craft, Talk, Build, Move`)
Targets line (+3) is split on `|` into N targets (`TargetAmount = max(1, N)`). For all types except `Talk`/`Move`, **all spaces are stripped first** (`str4.Replace(" ","")`). Each target is then `,`-split into `strArray8`. `TargetPrefab[i]=strArray8[0]`, `TargetCount[i]` (the "needed count") and `TargetLevel[i]` default to `1`. Per type:

- **Kill** — `prefab,level[,count]`. `TargetLevel = max(1, level)`; if a 3rd token: `TargetCount = max(1, count+1)` (**NOTE the +1** — config `count` is "extra kills beyond 1"; effective kills = count+1). Prefab must have a `Character` component. Increments via the QuestKill RPC (see Persistence). Kill level test is `actualLevel >= TargetLevel`.
- **Collect** — `prefab,quality[,count]`. `TargetLevel = max(1, quality)` (the item quality/star to match exactly), `TargetCount = max(1, count)`. Progress is **inventory count**, recomputed on every inventory change (not incremental). Item quality must equal `TargetLevel` exactly.
- **Harvest** — `prefab,?[anything]`. Reads `TargetLevel` from `strArray8[1]` (`max(1,int)`), `TargetCount` stays `1` unless… actually `TargetCount` is left `1` (count not parsed for Harvest in +3 loop — Harvest/Build only set `numArray4`=level). Prefab must have `Pickable`. Increments on pickup RPC.
- **Craft** — `prefab,level[,count]`. `TargetLevel = max(1, level)` (crafted quality must equal it exactly), `TargetCount = max(1, count)`. Prefab must have `ItemDrop`.
- **Build** — `prefab,level`. `TargetLevel = max(1, level)`, `TargetCount` stays `1`. Prefab must have `Piece`.
- **Talk** — target = the **NPC name** (raw, `.Trim()`, spaces preserved). `TargetCount`/`Level` = 1. Completes when the player talks to an NPC whose name matches.
- **Move** — target = `"x,y,radius,LabelText[,autocompleteBool]"`, spaces preserved (`ReplaceSpacesOutsideQuotes`). `TargetPrefab[i]` holds the **whole CSV string**. At least 4 fields: `x`(float), `z`(float — stored as Vector2(x,y) but compared against player x/z), `radius`(float), `label`(string, shown yellow). Optional 5th = bool → `TargetLevel[i]` set to `1` if true else `0` (used as a quirky flag). Completes when player is within `radius` of `(x, z)`.

> **Important coercion:** for Kill/Collect/Craft the **third** CSV token is the count; for Kill it is incremented by +1, for the others it is taken as-is. Level is the **second** token. `int.Parse` on `strArray8[1]` is unconditional for Kill/Collect/Harvest/Build/Craft — a target with only a prefab and no level token will throw and the whole quest is skipped (caught, logged red).

### Rewards line (+4) — `RewardType:args` joined by `|`
Split on `|` → `RewardsAmount = max(1, N)`. Each entry split on **first `:`** into `[RewardType, argsCSV]`; `argsCSV` is then `,`-split. `RewardType` is the `QuestRewardType` enum (case-insensitive); unparseable type logs and the entry is left default (quest still added). Arg shapes by reward type:

- **Item** / **Pet** / **Skill** / **Skill_EXP** / **SetCustomValue** / **AddCustomValue** (the "default" branch): `prefab,count[,level]`. `RewardPrefab=args[0]`, `RewardCount=int(args[1])`, `RewardLevel=1` (or `args[2]` if present). For **Pet** the level is additionally `+1`. For Item/Pet the prefab must resolve in ZNetScene (Item needs `ItemDrop`, Pet needs `Character`); Skill/Skill_EXP use the prefab string as a skill name; Set/AddCustomValue use the prefab string as a customData key (underscores shown as spaces in UI).
- **RandomItem**: `RewardPrefab = the full argsCSV string` (everything after the colon), `RewardCount=0`, `RewardLevel=0`. The CSV must be a multiple of **3**: repeating `prefab,amount,level` triples. On completion one triple is chosen at random and granted.
- **EXP/level hooks** — `EpicMMO_EXP`, `Cozyheim_EXP`, `GuildAddLevel`, `Battlepass_EXP`, `RustyClasses_EXP`: args = just `amount`. `RewardPrefab="NONE"`, `RewardCount=int(args[0])`, `RewardLevel=1`. These call third-party mod APIs on completion: EpicMMO `EpicMMOSystem_API.AddExp`, Cozyheim `Cozyheim_LevelingSystem.AddExp`, RustyClasses `RustyClassesAPI.AddEXP`, Battlepass via reflection `UL_B.Battlepass_Main_Client.AddExp`, GuildAddLevel raises `Guild.General.level` (handled in the Dialogue command path, not in `RemoveQuestComplete`'s explicit branches — GuildAddLevel falls through with no-op there; level is added via the matching dialogue OptionCommand).

### Conditions line (+6)
`profile4.ReplaceSpacesOutsideQuotes()` (spaces stripped except inside `"..."`, quotes removed), then split on `|` into AND-groups. Each group is split on the global **OrConditionSeparator (default `||`)** with `RemoveEmptyEntries` into OR-alternatives. Stored as `List<string[]>` (`Conditions`). `Init()` turns them into a `Dialogue_Condition` via `Dialogue.CombineOrConditions(Conditions, isQuest:true)` — **a quest is takeable only if every AND-group passes, where a group passes if any of its `||` alternatives passes.** Condition token grammar is identical to the Dialogue condition mini-language — see **Gotchas** and `QuestEvents.md`.

### TransferCompletion line (+7, optional)
`line.ReplaceSpacesOutsideQuotes().Split(',')`: `TransferComplition = args[0].GetStableHashCode()` (the hash of another quest's name), optional `TransferComplitionText = args[1]`. Used to chain/credit completion to another quest.

### Example
```cfg
# all spaces inside target/reward lines are stripped (except Talk/Move)
[wolf_cull=Autocomplete]
Kill
Cull the Wolves <image=Marketplace_CachedImages/wolf.png>
Bring me 5 wolf pelts' worth of dead wolves.
Wolf,2,4 | Fenring,3,2
Item:Coins,250 | EpicMMO_EXP:500 | RandomItem:SwordIron,1,2,MaceIron,1,2
0,0
EpicMMOLevelMore:10 | !HasItem:Wolf_pelt,1 || HasGlobalKey:defeated_eikthyr
otherquestname,Thanks for finishing the prelude!
```
(`Kill Wolf` count = `4+1 = 5` at level ≥ 2; second target `Fenring` count `2+1=3` at level ≥ 3. Cooldown 0 days, no time limit. Condition: EpicMMO level ≥ 10 AND (not-has Wolf_pelt OR global key defeated_eikthyr).)

A `Move` quest target:
```cfg
[reach_summit]
Move
Climb the Mountain
Get to the marked peak.
"1500, -2300, 30, The Frozen Summit"
Skill:Run,5
0,0
None
```

## Domain object
`Quests_DataTypes.Quest : ISerializableParameter` (fields, in ZPackage serialize order):
- `QuestType Type`, `SpecialQuestTag SpecialTag` (`[Flags] None=0, Autocomplete=1`)
- `string Name`, `string Description`
- `int TargetAmount`; parallel arrays `string[] TargetPrefab`, `int[] TargetCount`, `int[] TargetLevel`
- `int RewardsAmount`; parallel `QuestRewardType[] RewardType`, `string[] RewardPrefab`, `int[] RewardCount`, `int[] RewardLevel`
- `List<string[]> Conditions` (AND-of-OR token groups)
- `string PreviewImage`, `int Cooldown` (days), `int TimeLimit`
- `int TransferComplition` (hash), `string TransferComplitionText`
- runtime-only (not serialized): `int[] ScoreArray`, `long AcceptedTime`, `Sprite GetPreviewSprite`, `Dialogue_Condition Condition`, `LocalizedReward[]`, `LocalizedTarget[]`

`QuestRewardType`: `Item, RandomItem, Skill, Skill_EXP, Pet, EpicMMO_EXP, Cozyheim_EXP, GuildAddLevel, Battlepass_EXP, SetCustomValue, AddCustomValue, RustyClasses_EXP`.

Synced container: `CustomSyncedValue<Dictionary<int, Quest>> SyncedQuestData` (key `"questData"`), keyed by `lowercasedName.GetStableHashCode()`. Plus `SyncedQuestRevision` (int, `"questRevision"`). Client mirrors live in `AllQuests` (all defs) and `AcceptedQuests` (the local player's in-progress).

## Persistence
**Quest progress is NOT in LiteDB and NOT in any ServerSync value. It lives entirely in the local Player's `m_customData` dictionary (saved with the character `.fch`).** Quest definitions sync server→client; *progress is purely client-side per character.* Keys (see `Quest.SaveScore` / `AcceptQuest` / `RemoveQuest*` / `IsOnCooldown`):

- **Active progress:** key `"[MPASN]quest=" + UID` → value `"s0,s1,...,sN;acceptedTime"` — comma-joined `ScoreArray` (per-target counts), then `;`, then `AcceptedTime` (a `long`, `EnvMan.m_totalSeconds` at accept). On load `AcceptQuest(UID, score, acceptedTime)` re-parses this. ScoreArray length = `TargetAmount`.
- **Cooldown:** key `"[MPASN]questCD=" + UID` → value = `EnvMan.GetCurrentDay()` (int, the in-game day the quest was completed). `IsOnCooldown` returns true while `completedDay + Cooldown - currentDay > 0`. Set on completion; the active `[MPASN]quest=` key is removed at the same time.
- A quest is "finished/on cooldown" iff the `questCD` key exists (and within the cooldown window). There is no separate "completed" list — completion = presence of the CD key.

Progress increments are driven by `Quest_ProgressionHook` (client-side Harmony patches), each calling a `TryAddReward*` on `Quests_DataTypes.Quest`:
- **Kill:** `Character.RPC_Damage`/`ApplyDamage` prefix records the last player attacker in `CharacterLastDamageList`; on death the postfix sends RPC `"KGmarket QuestKill"` (prefab, level, pos, isDirectKill) to that player. Handler `QuestKillEvent` → `TryAddRewardKill(prefab, level)` (also re-broadcasts to party members if `_allowKillQuestsInParty`, and ignores kills >100m away unless direct). Increments `ScoreArray[i]` by 1 per matching kill.
- **Harvest:** `Pickable.RPC_Pick` prefix → RPC `"KGmarket QuestPickup"` → `TryAddRewardPickup(prefab)` → +1.
- **Craft:** transpiler in `InventoryGui.DoCrafting` → `TryAddRewardCraft(name, level)` (level = upgrade quality+1 or 1) → +1 if level matches exactly.
- **Build:** transpiler in `Player.PlacePiece` → `TryAddRewardBuild(prefab)` → +1; also sets ZDO `MPASNquestBuild=true` on the piece (and `Piece.DropResources` is suppressed for quest-built pieces so they can't be farmed).
- **Collect:** `Player.OnInventoryChanged` postfix → `InventoryChanged()` **recomputes** `ScoreArray[i] = CustomCountItems(prefab, level)` (absolute count, not incremental). On manual turn-in (`RemoveQuestComplete`) the collected items are consumed.
- **Move:** `TryAddRewardMove(pos)` (called elsewhere on player move) → sets score when within radius.
- **Talk:** `TryAddRewardTalk(name)` sets `ScoreArray[i]=1`.

Each successful increment calls `SaveScore(UID)` → rewrites the `[MPASN]quest=` customData string. `Autocomplete`-tagged quests auto-call `RemoveQuestComplete` when `IsComplete()`; otherwise the player turns them in at the NPC (`ClickQuestButton`). Completion also fires a webhook RPC and the `OnCompleteQuest` event.

`_maxAcceptedQuests` (global option) caps how many can be active; `_allowMultipleQuestScore` (global) controls whether one kill/pickup can credit multiple quests at once.

## NPC linkage
A Quests NPC stores its profile name in its ZDO under key **`"KGnpcProfile"`** (`ZDO.GET_NPC_Profile()` → `GetString("KGnpcProfile","default")`). `HasQuests(npc)` looks the NPC type up as `Market_NPC.NPCType.Quests`, then `SyncedQuestProfiles.Value[profile]` → `List<int>` of quest UID hashes (see `QuestProfiles.md`). Each quest hash resolves against `AllQuests` (mirror of `SyncedQuestData`). So: **NPC ZDO `KGnpcProfile` string → QuestProfile section name (lowercased) → list of quest-name hashes → Quest defs.** The profile/quest names are joined by the **stable hash of the lowercased name** at every hop.

## Gotchas / risks for the parser
- **Fixed 7-line block, index-addressed.** The parser must consume exactly 7 lines after each `[header]` (8 with the optional transfer line) and must NOT treat interleaved comments/blanks as part of the block — but the *outer* loop only advances `index1` by 1 and skips comments at the **top** of the loop, while the block reads `profiles[index1+1..+7]` raw. In practice a `#comment` or blank line *inside* a block shifts the offsets and corrupts the quest. Treat each quest as 7 contiguous data lines.
- **Space stripping is type-dependent:** target & condition & transfer lines use `ReplaceSpacesOutsideQuotes` (Talk/Move keep quoted spaces) while Kill/Collect/Harvest/Craft/Build strip ALL spaces. Reward line strips ALL spaces in token args only after the `:` split is irrelevant (it splits the raw line). Match exactly.
- **Kill count is +1; Collect/Craft count is as-written.** Easy to get wrong.
- **Reward `:` vs `,`:** type and args separated by the FIRST `:`; args internally `,`-separated. `RandomItem` keeps the entire arg blob (commas included) in `RewardPrefab`.
- **Condition mini-language** (shared with Dialogues, `Dialogue.ParseCondition`): each token is `Condition:arg1,arg2,...` (the parser does `command.Replace(":",",").Split(',')`, so `:` and `,` are interchangeable delimiters). A leading **`!`** on the condition name negates it (XOR `0x80` into the `OptionCondition` byte, flipping e.g. `HasItem`→`NotHasItem`). Full condition vocabulary = the `OptionCondition` enum: `None, HasItem(prefab,count[,quality]), HasBuff(name), SkillMore/Skill(skill,level), GlobalKey(key), HasQuest(name), QuestProgressDone(name), QuestFinished/OtherQuest(name), EpicMMOLevelMore(n), CozyheimLevelMore(n), HasAchievement(id), HasAchievementScore(n), CustomValueMore(key,n), ModInstalled(guid), IronGateStatMore(stat,n), HasGuild, HasGuildWithName(name), GuildLevelMore(n), GuildHasAchievement(id), IsVIP, RustyClassesLevelMore(n), HasPlayerKey(key), NPCModelEquals(prefab), HealthMore/MaxHealthMore/StaminaMore/MaxStaminaMore/EitrMore/MaxEitrMore(n), PlayerHasAllCustomDataKeys(key,reason), PlayerHasOneOfCustomDataKeys(key,reason), NPCNameEquals(name), HasFaction(name), HasAnyFaction`, plus each `Not*`/`*Less` reverse (the `!` form). `NotFinished`, `QuestNotFinished`, etc. exist as explicit reverse cases. Quote args containing spaces.
- **`SpecialTag` is sticky-until-reset:** it OR-accumulates across `=tag` headers and only resets to `None` on a header with no `=`. A parser that resets per-quest will diverge if multiple `=` headers appear.
- **`TimeLimit` default `0`** when the +5 line has no second CSV token (`s2 = parts.Length>1 ? parts[1] : "0"`). `Cooldown` is `int.Parse(parts[0])` — mandatory, will throw if line empty.
- **`<image=...>` extraction** happens only on the Name line (+1): substring between `<image=` and the next `>` becomes `PreviewImage`, and that tag is spliced out of the displayed Name.
- **Dedup:** quests are added to `SyncedQuestData` only `if (!ContainsKey(hash))` — duplicate lowercased names across files silently keep the first.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
