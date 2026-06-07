# Dialogues — Marketplace schema

A dialogue is a tree of **nodes** (each `[uid]` section), each with one body text and a list of player
**options/interactions**. Options can have conditions (gate visibility), transitions (move to another node),
and commands (do things: give quests, open trader UI, teleport, set values, spawn, etc.). It is a small
line-oriented mini-language layered on the shared `.cfg` skeleton.

Source: `Marketplace/Modules/Dialogues/Dialogues_Main_Server.cs` (the parser),
`Dialogues/Dialogues_DataTypes.cs` (the runtime tree, condition + command vocabularies).

---

## Location / how loaded

- **Folder:** `BepInEx/config/Marketplace/Configs/Dialogues/*.cfg` (`Market_Paths.DialoguesFolder`),
  read recursively: `Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories)`, all merged.
- **Parser:** `Dialogues_Main_Server.ProcessDialogueProfiles` builds `List<RawDialogue>` into
  `SyncedDialoguesData` (a `CustomSyncedValue` distributed to clients). Clients convert each `RawDialogue`
  to a `Dialogue` (implicit operator) and store in `ClientReadyDialogues[UID.ToLower()]`.
- **Server-only autoload**; watch keys `"DI"` (dialogues) and `"DSCD"` (custom spawn data) trigger
  `OnDialoguesChange` / `OnCustomSpawnDataChange` (hot reload re-reads the whole folder).
- **Error handling:** per-file try/catch logs `Error reading {file} (line N)` and **`break`s** (rest of that
  file skipped). Per-command/per-condition parse errors are caught and logged but do not abort the node.

---

## Data model / grammar

### Node structure (`RawDialogue`)

| field         | type                  | source |
|---------------|-----------------------|--------|
| `UID`         | string (lowercased)   | `[uid]` header, before `=` |
| `BG_Image`    | string                | `[uid=image]` header, after `=` (background image link/name) |
| `Text`        | string                | the **first non-header line** after the header; `\n` → newline |
| `Interactions`| `RawPlayerOption[]`   | every subsequent data line |

`RawPlayerOption`: `Type` (InteractionType), `Text`, `Icon`, `NextUID` (TransitionTo), `RandomTransition[]`,
`Commands[]`, `RandomCommands` (`List<KeyValuePair<int,string>>` = percent→command), `Conditions`
(`List<string[]>` = AND-list of OR-groups), `AlwaysVisible` (default `true`), `Color` (Color32, default white),
`OverrideError` (string shown instead of the condition's failure reason).

### Skeleton rules (over the shared `.cfg` skeleton)

1. Blank lines and lines starting with `#` are skipped.
2. **Header** `[uid]` or `[uid=backgroundimage]` — spaces removed, lowercased, split on `=`. Starts a new node
   and flushes the previous one.
3. The **first body line** of a node (before any option) becomes the node `Text` (`\n` literal honored).
4. Every later body line is **one option**. Options are `key:value` tokens joined by `|`.

### Option line grammar

```
[@interaction|@inrange|@outrange] Text:Hello | Transition:next_node | Command:GiveItem,Coins,10,1 | Condition:HasItem,Coins,10
```

- **Optional leading `@`-prefix** sets `InteractionType` (default `Option` = a clickable button):
  - `@interaction` → `Interaction` (auto-fires on node open, not a button)
  - `@inrange`     → `InRange`  (fires once when a player comes within ~10 m of the NPC)
  - `@outrange`    → `OutRange` (fires once when the player leaves ~12 m)
- The rest is `Split('|')`; each piece is `Split(':', 2)` → `InputType:payload`. **InputType keys** (case-insensitive):

| key (InputType)   | effect | payload format |
|-------------------|--------|----------------|
| `Text`            | button / line text | free text, `\n` honored, trimmed |
| `Transition`      | go to node | target UID (spaces removed, lowercased) |
| `RandomTransition`| go to a random node | comma list of UIDs (spaces removed, lowercased); used only if `Transition` empty |
| `Command`         | run a command | one command string (see vocabulary). Spaces outside quotes stripped |
| `RandomCommand`   | chance command | `percent,command` — `int,rest`; rolls `Random(0,100) < percent` |
| `Icon`            | option icon | icon name (spaces removed) |
| `Condition`       | gate the option | condition string(s) joined by the OR separator (default `\|\|`); spaces-outside-quotes stripped |
| `AlwaysVisible`   | show even if condition fails (greyed) | `true`/`false` |
| `Color`           | text color | `r,g,b` (0–255 each) |
| `OverrideError`   | custom locked-reason text | free text, trimmed |

- Multiple `Condition:` tokens on one line → multiple entries in `Conditions` (**AND** across tokens).
  Within one `Condition:` token, the OR-separator (default `||`, `Global_Configs.OrConditionSeparator`,
  configurable in MarketPlace.cfg) splits into an **OR** group. So `Conditions` = AND-of-OR-groups.
- Commands and conditions both use `value.Replace(":", ",")` then `Split(',')` internally — so **inside a
  command/condition, `:` and `,` are interchangeable separators**. `split[0]` = the verb, the rest are args.

### Worked example

```cfg
# greeter dialogue
[greeter=https://example.com/bg.png]
Welcome, traveler.\nWhat brings you here?
Text:Show me your wares | Command:OpenUI,Trader,blacksmith
Text:I have a delivery | Transition:turnin | Condition:HasQuest,deliver_ore | OverrideError:You have no delivery for me.
Text:Heal me (VIP) | Command:Heal,100 | Condition:IsVIP || HasItem,Coins,50
@inrange Command:PlayAnimation,wave
@interaction Command:SetNPCNameGlobal,The_Merchant
Text:Goodbye | Transition:default | RandomCommand:25,GiveItem,Coins,5,1
```

---

## Command vocabulary (`OptionCommand`, parsed in `Dialogue.ParseCommands`)

Verb is `split[0]` (case-insensitive); args follow. Most run **client-side** on `Player.m_localPlayer`.

| Command            | Args (after verb) | Action |
|--------------------|-------------------|--------|
| `OpenUI`           | `[type] [profile]` | open a module UI; defaults to NPC's own type/profile; also accepts `Dialogue`/`Dialog` type |
| `PlaySound`        | `clipKey` | play NPC audio clip (or 2D fallback) |
| `GiveQuest`        | `questUID` | accept quest (by lowercased hash) |
| `RemoveQuest`      | `questUID [fireCancelEvent:bool]` | remove/fail quest |
| `FinishQuest`      | `questUID` | mark quest complete |
| `GiveItem`         | `prefab count level` | add item to player |
| `GiveItemWithData` | `prefab count level customDataKey` | add item with CustomSpawnData ZDO applied |
| `SetPlayerData`    | `customDataKey` | write CustomSpawnData ints/floats/strings/bools/longs onto the player's ZDO |
| `RemoveItem`       | `prefab count` | remove items |
| `Spawn`            | `prefab count quality` | spawn near player (±15 m), SetLevel/SetQuality |
| `SpawnXYZ`         | `prefab count quality x y z spread` | spawn at world XYZ, ± spread |
| `SpawnWithData`    | `prefab count quality customDataKey` | Spawn + apply CustomSpawnData ZDO |
| `SpawnXYZWithData` | `prefab count quality x y z spread customDataKey` | SpawnXYZ + CustomSpawnData |
| `Teleport`         | `x y z [ignoreTeleportable:bool]` | TeleportTo (respects `IsTeleportable` unless 4th arg true) |
| `Damage`           | `amount` | damage the player |
| `Heal`             | `hp` | heal the player |
| `GiveBuff`         | `statusEffectName [ttl]` | re-apply status effect (ttl clamped to ≤1!) |
| `AddPin`           | `name x y z` | add a permanent-ish map pin + show |
| `PingMap`          | `text x y z` | broadcast an in-world ping at point |
| `AddEpicMMOExp`    | `amount` | EpicMMO API |
| `AddCozyheimExp`   | `amount` | Cozyheim API |
| `PlayAnimation`    | `trigger` | NPC animator SetTrigger |
| `EnterPassword`    | `title password onSuccessUID onFailUID` | text-prompt; routes to a dialogue node by result |
| `GuildAddLevel`    | `amount` | Guilds API: own guild level += amount |
| `Battlepass_EXP`   | `amount` | reflection into `UL_B.Battlepass_Main_Client.AddExp` |
| `ConsoleCommand`   | `command…` | run a registered console command (debug mode on); `{playername}` substituted |
| `AddCustomValue`   | `key amount` | `Player.AddCustomValue` |
| `SetCustomValue`   | `key amount` | set (or remove if 0) custom value |
| `PlayVideo`        | `clip [sizePercent]` | VideoPlayerUI |
| `AddRustyClassesEXP`| `amount` | RustyClasses API |
| `SendWebhook`      | `host message` | POST to `https://host`; replaces `Dialogues_UI._replacements` tokens |
| `AddPlayerKey`     | `key` | `Player.AddUniqueKey` |
| `RemovePlayerKey`  | `key` | `Player.RemoveUniqueKey` |
| `SetPlayerGuild`   | `guildNameOrId` | move player into guild |
| `SetPlayerGuildRank`| `rank` | set own rank (enum `Ranks`) |
| `SetNPCModelLocal` | `prefab` | change this NPC's model client-locally |
| `SetNPCModelGlobal`| `prefab` | RPC `KGmarket overridemodel` to everybody (persists) |
| `SetNPCNameLocal`  | `name` | local name (`_`→space) |
| `SetNPCNameGlobal` | `name` | RPC `KGmarket overridename` (`_`→space, persists) |
| `SetNPCPatrol`     | `patrolString` | RPC `KGmarket GetPatrolData` (persists patrol) |
| `SetFaction`       | `factionName` | write `Player.m_customData["MPASN_Faction"]` (lowercased) |
| `RemoveFaction`    | — | remove `MPASN_Faction` |

`RandomCommand` reuses the same verbs but each is wrapped in a `percent` gate.

## Condition vocabulary (`OptionCondition`, parsed in `Dialogue.ParseCondition`)

A condition string is `verb,arg1,arg2…`. **Prefix `!`** negates (the enum has paired Not/Less values; `!`
toggles bit `0x80`). `verb` is case-insensitive. A `Condition:` token is an OR-group (any passes); multiple
tokens AND together. Returning `false` shows the `reason` (or `OverrideError`) as the locked tooltip.

| Condition (positive)        | Args | Passes when |
|-----------------------------|------|-------------|
| `None`                      | — | always |
| `HasItem`                   | `prefab count [quality]` | player has ≥count |
| `HasBuff`                   | `statusEffectName` | active status effect |
| `SkillMore` / `Skill`       | `skill level` | skill ≥ level |
| `GlobalKey`                 | `key` | global key set |
| `HasQuest`                  | `questUID` | quest accepted |
| `QuestProgressDone`         | `questUID` | accepted + objectives complete |
| `QuestFinished` / `OtherQuest` | `questUID` | quest on cooldown (i.e. completed) |
| `EpicMMOLevelMore`          | `level` | EpicMMO ≥ |
| `CozyheimLevelMore`         | `level` | Cozyheim ≥ |
| `HasAchievement`            | `name` | leaderboard achievement owned |
| `HasAchievementScore`       | `score` | achievement score ≥ |
| `CustomValueMore`           | `key value` | player custom value ≥ |
| `ModInstalled`              | `pluginGUID` | BepInEx plugin present |
| `IronGateStatMore`          | `PlayerStatType value` | vanilla profile stat ≥ |
| `HasGuild`                  | — | in a guild |
| `HasGuildWithName`          | `name` | guild name == |
| `GuildLevelMore`            | `level` | guild level ≥ |
| `GuildHasAchievement`       | `name` | guild has achievement |
| `IsVIP`                     | — | userID in VIP list |
| `RustyClassesLevelMore`     | `level` | RustyClasses ≥ |
| `HasPlayerKey`              | `key` | unique player key |
| `NPCModelEquals`            | `prefab` | NPC's `LocalModelOverride` == |
| `HealthMore`/`MaxHealthMore`/`StaminaMore`/`MaxStaminaMore`/`EitrMore`/`MaxEitrMore` | `value` | stat ≥ |
| `PlayerHasAllCustomDataKeys`| `customDataKey reason` | all KV in CustomSpawnData match player ZDO |
| `PlayerHasOneOfCustomDataKeys`| `customDataKey reason` | any KV differs (i.e. "has one of") |
| `NPCNameEquals`             | `name` | NPC name == |
| `HasFaction`                | `faction` | `MPASN_Faction` == (lower) |
| `HasAnyFaction`             | — | player has any known faction |

**Negated forms** (use `!verb` OR the explicit Not-name): `NotHasItem`, `NotHasBuff`, `SkillLess`,
`NotGlobalKey`, `NotHasQuest`, `QuestProgressNotDone`, `QuestNotFinished`/`NotFinished`, `EpicMMOLevelLess`,
`CozyheimLevelLess`, `NotHasAchievement`, `NotHasAchievementScore`, `CustomValueLess`, `NotModInstalled`,
`IronGateStatLess`, `NotHasGuild`, `NotHasGuildWithName`, `GuildLevelLess`, `GuildNotHasAchievement`,
`NotIsVIP`, `RustyClassesLevelLess`, `NotHasPlayerKey`, `NotNPCModelEquals`, `Health/Stamina/Eitr…Less`,
`NotNPCNameEquals`, `NotHasFaction`, `NotHasAnyFaction`. (`!HasItem` ≡ `NotHasItem`, etc.)

---

## Linkage

- A node UID is referenced by an NPC's `KGnpcDialogue` ZDO key (see `NPC.md`) and by `Transition` /
  `RandomTransition` / `EnterPassword` onSuccess/onFail — all lowercased.
- `OpenUI` command bridges back to the **module config profiles** (Trader/Banker/etc. `[section]` names).
- `*WithData` commands and the `PlayerHas…CustomDataKeys` conditions reference **CustomSpawnData** keys
  (filename without extension, lowercased) — see `CustomSpawnData.md`.
- Factions reference `Factions.yml`; quests reference `Configs/Quests/*.cfg`; achievements reference
  `Configs/LeaderboardAchievements/*.cfg`.

## Gotchas / risks

- **`:` and `,` are interchangeable inside commands & conditions** (`Replace(":", ",")`), but `:` is also the
  InputType separator at the option level (`Split(':',2)` — only the FIRST colon). So `Command:` is the token
  boundary, then everything after is comma/colon args.
- **OR separator is configurable** (default `||`) via MarketPlace.cfg `OrConditionSeparator`; a drop-in must
  read that global, not hardcode `||`.
- **`GiveBuff` ttl is clamped to ≤ 1 second** (`Mathf.Min(1f, ttl)`) — a bug-flavored quirk; reproduce it for
  fidelity.
- **`AlwaysVisible` default is `true`** — options show even when their condition fails (greyed/locked), unless
  set false.
- **`ReplaceSpacesOutsideQuotes()`** is applied to Command/RandomCommand/Condition payloads: spaces are
  stripped *except inside quotes*, so quoted strings preserve spaces (e.g. webhook text, ConsoleCommand args).
- **`Text`** uses `Trim()` + `\n`→newline; the node body `Text` and `OverrideError` are NOT space-stripped, so
  whitespace in display text is preserved.
- Conditions/commands run on the **client** against `Player.m_localPlayer`; they are not server-validated.

## Status
- [x] format documented
- [ ] importer written
- [ ] verified-in-game
