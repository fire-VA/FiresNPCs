# Marketplace Dialogue System — Full Source Reference

> **Purpose**: Complete reference of the Marketplace NPC Dialogue system as documented by KG.
> Use this when building/improving our own dialogue parser, UI, conditions, and commands.
> This file should NOT be abbreviated — it is the canonical source of truth for Marketplace compatibility.
>
> **Companion file**: `MARKETPLACE_DIALOGUE_SOURCE.cs` contains the full decompiled source code.

---

## Overview

The Dialogues system allows you to create interactive NPC conversations with branching dialogue trees, conditions, and commands. Dialogues are defined in .cfg files and loaded from the server. They support:

- Multi-branch dialogue trees with transition chaining
- Conditional options based on items, quests, skills, player stats, guild membership, and more
- Commands that trigger actions when a player selects an option
- Random transitions and probabilistic commands
- Custom ZDO data for spawning entities or modifying players

---

## File Structure

```
Dialogues/
??? *.cfg              ? Dialogue definition files
CustomSpawnData/
??? *.yml              ? Custom ZDO data used by some commands
```

All .cfg files in the Dialogues folder (including subdirectories) are loaded automatically. All .yml files in the CustomSpawnData folder are loaded as named data sets (keyed by filename without extension, lowercased).

---

## Dialogue File Format

### Sections

Each dialogue entry begins with a section header in square brackets:

```
[dialogue_uid]
```

Or with an optional background image link:

```
[dialogue_uid=https://example.com/image.png]
```

- **UID** — Unique identifier for this dialogue node, used for transitions. Must be lowercase.
- **BG Image** — Optional URL for a background image displayed in the dialogue UI.

The first non-empty, non-comment line after the header is the NPC's text (what the NPC says). Subsequent lines define player interaction options.

```
[my_dialogue]
Hello traveler, what do you seek?
Text: I need supplies. | Transition: shop_dialogue
Text: Nothing, farewell. | Transition: goodbye
```

Lines beginning with `#` are treated as comments and ignored.

### Interaction Line Format

Each option line is a series of `|`-separated `Field: value` pairs:

```
Field: value | Field: value | ...
```

Example:

```
Text: Buy something. | Transition: shop_menu | Condition: HasItem, Gold, 1
```

### Interaction Types

By default, options are shown as clickable dialogue choices. You can prefix the line with a special type tag to change the behavior:

| Prefix | Type | Description |
|--------|------|-------------|
| (none) | Option | A standard clickable dialogue option shown to the player |
| `@interaction` | Interaction | Triggered when the player interacts with the NPC (replaces the default UI open) |
| `@inrange` | InRange | Triggered automatically when the player enters range of the NPC |
| `@outrange` | OutRange | Triggered automatically when the player leaves range of the NPC |

```
# Opens the dialogue UI directly on NPC click
@interaction Command: OpenUI, Dialogue, my_dialogue_profile

# Plays a sound when player enters range
@inrange Command: PlaySound, npc_hello_sfx

# Plays a sound when player leaves range
@outrange Command: PlaySound, npc_bye_sfx

Text: Hello! | Transition: hello_response
```

### Input Fields

| Field | Description | Example |
|-------|-------------|---------|
| Text | The text shown to the player for this option | `Text: I want to trade.` |
| Transition | UID of the next dialogue to load when this option is chosen. If omitted, the dialogue closes. | `Transition: shop_main` |
| RandomTransition | Comma-separated list of UIDs; one is picked at random | `RandomTransition: path_a, path_b, path_c` |
| Command | A command to execute when this option is chosen | `Command: GiveItem, SwordBasic, 1, 1` |
| RandomCommand | A probabilistic command: weight, command_string (weight 0–100) | `RandomCommand: 50, GiveItem, Coins, 5, 1` |
| Condition | A condition that must pass for this option to be available | `Condition: HasItem, Wood, 10` |
| Icon | Icon to display next to this option (prefab name or URL) | `Icon: Hammer` |
| AlwaysVisible | Whether to show the option even if its condition fails (true/false, default true) | `AlwaysVisible: false` |
| Color | RGB color of the option text as R, G, B (0–255) | `Color: 255, 200, 100` |
| OverrideError | Custom message shown when a condition fails, overriding the default | `OverrideError: You are not ready.` |

Fields are parsed case-insensitively, but the canonical casing above is recommended. Multiple Command, RandomCommand, and Condition fields are all applied. Commands execute in order; conditions all must pass (AND logic within a row, OR logic between values separated by `||`).

---

## Conditions

### Condition Syntax

```
Condition: ConditionName, arg1, arg2, ...
```

To negate a condition, prefix the name with `!`:

```
Condition: !HasItem, Wood, 10
```

### AND / OR Logic

- Multiple `Condition:` fields on the same line are evaluated with **AND** — all must pass.
- Within a single `Condition:` field, conditions separated by `||` are evaluated with **OR** — at least one must pass.

```
Condition: HasQuest, main_quest || HasQuest, side_quest | Condition: SkillMore, Swords, 20
```

The above requires: (main_quest OR side_quest) AND (Sword skill ? 20).

### Available Conditions

#### Items and Inventory

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| HasItem | prefab, amount[, level] | Player has at least amount of prefab at the specified level (default: 1) |
| NotHasItem | prefab, amount[, level] | Player does NOT have that many of the item |

#### Buffs and Status Effects

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| HasBuff | effectName | Player currently has the named status effect |
| NotHasBuff | effectName | Player does NOT have the named status effect |

#### Skills

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| SkillMore | skillName, level | Player's skill is ? level |
| SkillLess | skillName, level | Player's skill is < level |

#### Quests

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| HasQuest | questName | Player has accepted the quest |
| NotHasQuest | questName | Player has NOT accepted the quest |
| QuestProgressDone | questName | Player has accepted the quest AND all objectives are complete |
| QuestProgressNotDone | questName | Player has the quest but objectives are NOT complete |
| QuestFinished | questName | Quest has been completed (on cooldown) |
| QuestNotFinished | questName | Quest has NOT been completed yet |

#### World / Player Keys

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| GlobalKey | keyName | The global world key is set |
| NotGlobalKey | keyName | The global world key is NOT set |
| HasPlayerKey | keyName | The player has the unique key |
| NotHasPlayerKey | keyName | The player does NOT have the unique key |

#### Player Stats

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| HealthMore | value | Current HP ? value |
| HealthLess | value | Current HP < value |
| MaxHealthMore | value | Max HP ? value |
| MaxHealthLess | value | Max HP < value |
| StaminaMore | value | Current stamina ? value |
| StaminaLess | value | Current stamina < value |
| MaxStaminaMore | value | Max stamina ? value |
| MaxStaminaLess | value | Max stamina < value |
| EitrMore | value | Current Eitr ? value |
| EitrLess | value | Current Eitr < value |
| MaxEitrMore | value | Max Eitr ? value |
| MaxEitrLess | value | Max Eitr < value |
| IronGateStatMore | statType, value | Player profile stat ? value |
| IronGateStatLess | statType, value | Player profile stat < value |

#### Custom Values

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| CustomValueMore | key, value | Player's custom value for key ? value |
| CustomValueLess | key, value | Player's custom value for key < value |
| PlayerHasAllCustomDataKeys | dataKey, reason | Player ZDO matches ALL entries in the named custom data set |
| PlayerHasOneOfCustomDataKeys | dataKey, reason | Player ZDO matches at least ONE entry in the named custom data set |

#### Guild

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| HasGuild | — | Player is in any guild |
| NotHasGuild | — | Player is NOT in a guild |
| HasGuildWithName | guildName | Player's guild has exactly this name |
| NotHasGuildWithName | guildName | Player's guild does NOT have this name |
| GuildLevelMore | level | Player's guild level ? level |
| GuildLevelLess | level | Player's guild level < level |
| GuildHasAchievement | achievementKey | Player's guild has the named achievement |
| GuildNotHasAchievement | achievementKey | Player's guild does NOT have the achievement |

#### VIP

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| IsVIP | — | Player is in the server VIP list |
| NotIsVIP | — | Player is NOT in the VIP list |

#### Achievements and Leaderboard

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| HasAchievement | achievementKey | Player has the named achievement |
| NotHasAchievement | achievementKey | Player does NOT have the achievement |
| HasAchievementScore | score | Player's total achievement score ? score |
| NotHasAchievementScore | score | Player's total achievement score < score |

#### Third-Party Mod Levels

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| EpicMMOLevelMore | level | EpicMMO player level ? level |
| EpicMMOLevelLess | level | EpicMMO player level < level |
| CozyheimLevelMore | level | Cozyheim leveling system level ? level |
| CozyheimLevelLess | level | Cozyheim leveling system level < level |
| RustyClassesLevelMore | level | RustyClasses player level ? level |
| RustyClassesLevelLess | level | RustyClasses player level < level |

#### Mods

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| ModInstalled | modGUID | The given BepInEx plugin GUID is loaded |
| NotModInstalled | modGUID | The given BepInEx plugin GUID is NOT loaded |

#### NPC State

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| NPCModelEquals | prefabName | NPC's current model override matches prefabName |
| NotNPCModelEquals | prefabName | NPC's current model override does NOT match |
| NPCNameEquals | name | NPC's current name matches |
| NotNPCNameEquals | name | NPC's current name does NOT match |

#### Special

| Condition | Arguments | Description |
|-----------|-----------|-------------|
| None | — | Always passes (no condition) |

---

## Commands

### Command Syntax

```
Command: CommandName, arg1, arg2, ...
```

Arguments are comma-separated. Colons (`:`) are also treated as separators, so `CommandName: arg1: arg2` is equivalent to `CommandName, arg1, arg2`.

### Quoted Strings

Some arguments contain spaces — for example a console command like `spawn Boar 1`, or a webhook message. Wrap such arguments in double quotes so the parser treats them as a single value:

```
Command: ConsoleCommand, "spawn Boar 1"
Command: SendWebhook, discord.com/api/webhooks/xxx/yyy, "Player {playername} has arrived!"
Command: EnterPassword, "Enter the secret word", valheim, npc_secret_granted, npc_secret_denied
```

Any argument that contains a comma or space should be quoted.

### Available Commands

#### UI

| Command | Arguments | Description |
|---------|-----------|-------------|
| OpenUI | uiType, profile | Opens the specified NPC UI. If both args are omitted, falls back to the NPC's own type and profile — but explicit args are recommended. |
| PlayVideo | fileName[, sizeMultiplier] | Plays a video file from the local Marketplace_VideoClips folder. Size multiplier is 0–100 (default 50 = 50%) |

#### Audio

| Command | Arguments | Description |
|---------|-----------|-------------|
| PlaySound | soundName | Plays a registered NPC audio clip |

#### Animations

| Command | Arguments | Description |
|---------|-----------|-------------|
| PlayAnimation | triggerName | Triggers an animation on the NPC |

#### Items

| Command | Arguments | Description |
|---------|-----------|-------------|
| GiveItem | prefab, amount, level | Gives the player amount of prefab at level |
| GiveItemWithData | prefab, amount, level, dataKey | Gives item with custom ZDO data from the named data set |
| RemoveItem | prefab, amount | Removes amount of prefab from the player's inventory |

#### Player Modification

| Command | Arguments | Description |
|---------|-----------|-------------|
| Heal | amount | Heals the player for amount HP |
| Damage | amount | Deals amount damage to the player |
| GiveBuff | effectName[, duration] | Applies a status effect to the player |
| SetPlayerData | dataKey | Applies custom ZDO data values directly to the player's ZDO |
| AddCustomValue | key, amount | Adds amount to the player's custom value for key |
| SetCustomValue | key, value | Sets the player's custom value for key to value (0 removes it) |
| AddPlayerKey | key | Adds a unique key to the player |
| RemovePlayerKey | key | Removes a unique key from the player |

#### Quests

| Command | Arguments | Description |
|---------|-----------|-------------|
| GiveQuest | questName | Accepts the named quest for the player |
| RemoveQuest | questName[, triggerEvent] | Removes (fails) the quest. Pass `true` as 2nd arg to trigger cancel event |
| FinishQuest | questName | Marks the quest as completed for the player |

#### Spawning

| Command | Arguments | Description |
|---------|-----------|-------------|
| Spawn | prefab, amount, level | Spawns amount of prefab near the player at level |
| SpawnWithData | prefab, amount, level, dataKey | Spawns with custom ZDO data |
| SpawnRaw | prefab, amount, x, y, z, maxDistance | Spawns any prefab at specific coordinates |
| SpawnXYZ | prefab, amount, level, x, y, z, maxDistance | Spawns near a specific world position |
| SpawnXYZWithData | prefab, amount, level, x, y, z, maxDistance, dataKey | Spawns at position with custom ZDO data |

#### Navigation

| Command | Arguments | Description |
|---------|-----------|-------------|
| Teleport | x, y, z[, allowWithOre] | Teleports the player to the given coordinates. Pass `true` as 4th arg to bypass teleport restriction |
| AddPin | name, x, y, z | Adds a map pin at the given coordinates |
| PingMap | label, x, y, z | Pings the map at the given coordinates and shows the location |

#### Guilds

| Command | Arguments | Description |
|---------|-----------|-------------|
| GuildAddLevel | amount | Adds amount levels to the player's guild |
| SetPlayerGuild | guildNameOrId | Moves the player into the named/ID'd guild |
| SetPlayerGuildRank | rankName | Sets the player's rank within their guild |

#### NPC Modification

| Command | Arguments | Description |
|---------|-----------|-------------|
| SetNPCModelLocal | prefabName | Changes NPC model locally (client only) |
| SetNPCModelGlobal | prefabName | Changes NPC model globally (all clients) |
| SetNPCNameLocal | name | Changes NPC name locally. Use `_` for spaces |
| SetNPCNameGlobal | name | Changes NPC name globally. Use `_` for spaces |
| SetNPCPatrol | patrolName | Sets the NPC's patrol route |

#### Third-Party Mods

| Command | Arguments | Description |
|---------|-----------|-------------|
| AddEpicMMOExp | amount | Adds EpicMMO experience |
| AddCozyheimExp | amount | Adds Cozyheim leveling experience |
| AddRustyClassesEXP | amount | Adds RustyClasses experience |
| Battlepass_EXP | amount | Adds Battlepass experience |

#### Misc

| Command | Arguments | Description |
|---------|-----------|-------------|
| ConsoleCommand | "command string" | Runs a console command. `{playername}` is replaced with the player's name. Use quotes for commands with spaces. |
| EnterPassword | title, password, onSuccess, onFail | Opens a text input. Transitions to `onSuccess` or `onFail` UID based on input |
| SendWebhook | webhookUrl, "message" | Sends a Discord-style webhook POST. URL is provided without `https://`. Use quotes for messages with spaces. |

---

## Custom Spawn ZDO Data

Custom spawn data files are YAML files placed in the `CustomSpawnData/` folder. Each file becomes a named data set (keyed by filename without extension, in lowercase).

These data sets are used by commands like `GiveItemWithData`, `SpawnWithData`, `SetPlayerData`, and conditions like `PlayerHasAllCustomDataKeys`.

### Format

```yaml
Ints:
  SomeIntKey: 42
  AnotherInt: 100

Floats:
  SomeFloatKey: 3.14

Strings:
  SomeStringKey: hello

Bools:
  SomeBoolKey: true

Longs:
  SomeLongKey: 123456789
```

All sections are optional. Only include the data types you need.

---

## Examples

### Basic Shop and Quest Dialogue

```cfg
[npc_greeter]
Greetings, adventurer! What brings you here today?
Text: I'd like to trade. | Transition: npc_shop
Text: Tell me about the quest. | Transition: npc_quest_intro | Condition: NotHasQuest, the_great_hunt
Text: I've finished the quest! | Transition: npc_quest_done | Condition: QuestProgressDone, the_great_hunt | AlwaysVisible: false
Text: Nothing, farewell. | Transition: npc_goodbye

[npc_shop]
Of course! Here's what I have.
Text: Browse wares. | Command: OpenUI, Trader, my_trader_profile
Text: Actually, never mind. | Transition: npc_greeter

[npc_quest_intro]
A terrible beast has been spotted to the north. Will you deal with it?
Text: I'll do it! | Transition: npc_quest_accepted | Command: GiveQuest, the_great_hunt
Text: Not today. | Transition: npc_greeter

[npc_quest_accepted]
Good luck out there. I'll be counting on you.
Text: I'll be back. | Transition: npc_greeter

[npc_quest_done]
You've done it! Here is your reward.
Text: Thank you. | Transition: npc_greeter | Command: FinishQuest, the_great_hunt | Command: GiveItem, Coins, 50, 1

[npc_goodbye]
Safe travels, friend.
Text: Goodbye.
```

### Interaction and Range Triggers

```cfg
[npc_guard]
@interaction Command: OpenUI, Dialogue, npc_guard_greet
@inrange Command: PlaySound, guard_alert
@outrange Command: PlaySound, guard_relax

[npc_guard_greet]
Halt! State your business.
Text: I'm just passing through. | Transition: npc_guard_ok
Text: I need to speak with the captain. | Transition: npc_guard_captain | Condition: HasPlayerKey, captain_permit
Text: ...

[npc_guard_ok]
Very well. Move along.
Text: Thank you.

[npc_guard_captain]
The captain is inside. Don't cause trouble.
Text: Understood.
```

### Random Transitions and Commands

```cfg
[npc_wanderer]
I have tales to tell... which would you like to hear?
Text: Tell me something. | RandomTransition: npc_tale_a, npc_tale_b, npc_tale_c
Text: Any chance of a reward? | Transition: npc_gamble

[npc_tale_a]
They say a dragon once slept beneath this very village.
Text: Fascinating. | Transition: npc_wanderer

[npc_tale_b]
A sailor once navigated the Mistlands blindfolded. He didn't survive, but still.
Text: Grim. | Transition: npc_wanderer

[npc_tale_c]
The old king buried his treasure somewhere to the east. No one has found it yet.
Text: Interesting... | Transition: npc_wanderer

[npc_gamble]
I'm feeling generous. Let's see what fate has in store...
Text: Try my luck. | Transition: npc_wanderer | RandomCommand: 40, GiveItem, Coins, 1, 1 | RandomCommand: 15, GiveItem, Ruby, 1, 1
Text: Never mind. | Transition: npc_wanderer
```

### Password Gate

```cfg
[npc_keeper]
This door is not for everyone. Do you know the word?
Text: I know the password. | Command: EnterPassword, "Enter the secret word", valheim, npc_keeper_granted, npc_keeper_denied
Text: Never mind.

[npc_keeper_granted]
Welcome, friend. You may pass.
Text: Thank you. | Command: AddPlayerKey, secret_door_open | Command: Teleport, 150, 32, 200
Text: Actually, not right now.

[npc_keeper_denied]
That is not the word. Be gone.
Text: My apologies.
```

### Console Commands with Spaces

```cfg
[npc_admin]
What would you like me to do?
Text: Spawn a troll near me. | Transition: npc_admin | Command: ConsoleCommand, "spawn Troll 1 2"
Text: Toggle god mode. | Transition: npc_admin | Command: ConsoleCommand, "god"
Text: Announce my arrival. | Transition: npc_admin | Command: ConsoleCommand, "say {playername} has arrived!"
Text: Log this visit. | Transition: npc_admin | Command: SendWebhook, discord.com/api/webhooks/xxx/yyy, "Player {playername} just spoke to the admin NPC."
Text: That's all. | Transition: npc_admin
```

---

## Tips

- Dialogue UIDs are always lowercased internally. It is recommended to write them in lowercase in the config files as well for consistency.
- A dialogue node with no options can only be dismissed with the Escape key. Always include at least one option — even a simple `Text: Goodbye.` with no Transition — so the player can close it naturally.

---

## Marketplace UI Architecture (from decompiled source)

The Marketplace dialogue UI uses a **prefab-based** approach:

1. **`NPCDialogueUI`** — Main panel prefab loaded from asset bundle
2. **`Dialogue_Option`** — Per-option row prefab, instantiated into Content

### UI Hierarchy (from `Dialogues_UI.Init`)

```
NPCDialogueUI (root, has CanvasGroup for fade)
??? GO (main container, acts as Content parent)
    ??? NPC_Name (Text component — NPC display name)
    ??? Dialogue_Text (Text component — quoted NPC speech)
    ??? BACKGROUND (Image component — optional BG image)
    ??? [Instantiated Dialogue_Option elements...]
```

### Dialogue_Option Hierarchy

```
Dialogue_Option (root, has Button component)
??? Indexer
?   ??? Text (number label: "1", "2", etc.)
?   ??? bg (Image — colored background circle)
??? Text (option label text)
?   ??? Icon (Image — item/prefab icon)
??? line (Image — colored accent bar)
```

### Key Visual Behaviors

- **NPC name**: Plain text, centered
- **Dialogue text**: Wrapped in quotes: `"Hello There! And welcome..."`
- **Option numbering**: Sequential (1, 2, 3...) displayed in a circle badge
- **Option icon**: Small image next to the text (circle shape in the screenshot)
- **Option color**: Applied to both the `line` bar and `Indexer/bg` circle
- **Failed conditions**: Button destroyed, line + indexer bg set to `Color.red`, reason appended
- **Fade**: CanvasGroup alpha animated via coroutine (0.5s ease-out)
- **Close button**: NOT present in Marketplace — ESC key only
- **Back button**: NOT present in Marketplace — navigation is purely through transitions

### Input Blocking

- Patches `TextInput.IsVisible` to return `true` when dialogue is open
- This blocks all Valheim game input (movement, crafting, etc.)

---

## Our Implementation Status vs Marketplace

| Feature | Marketplace | Ours | Notes |
|---------|------------|------|-------|
| `.cfg` file parsing | ? | ? | Full compatibility |
| `[uid]` / `[uid=bg]` headers | ? | ? | |
| `@interaction` / `@inrange` / `@outrange` | ? | ? | |
| Pipe-separated attributes | ? | ? | |
| `!` prefix negation | ? | ? | |
| OR conditions (`\|\|`) | ? | ? | |
| `%playername%` replacement | ? | ? | |
| HasItem / NotHasItem | ? | ? | |
| HasBuff / NotHasBuff | ? | ? | |
| SkillMore / SkillLess | ? | ? | |
| GlobalKey / NotGlobalKey | ? | ? | |
| HasPlayerKey / NotHasPlayerKey | ? | ? | |
| Quest conditions (all 6) | ? | ? | |
| CustomValueMore / Less | ? | ? | |
| Health/Stamina/Eitr checks | ? | ? | |
| GiveItem / RemoveItem | ? | ? | |
| GiveQuest / RemoveQuest / FinishQuest | ? | ? | |
| Teleport | ? | ? | |
| Spawn / SpawnXYZ | ? | ? | |
| Damage / Heal | ? | ? | |
| GiveBuff | ? | ? | |
| AddPlayerKey / RemovePlayerKey | ? | ? | |
| AddCustomValue / SetCustomValue | ? | ? | |
| PlayAnimation / PlaySound | ? | ? | |
| ConsoleCommand | ? | ? | |
| AddPin / PingMap | ? | ? | |
| OpenUI | ? | ? | |
| EnterPassword | ? | ? | |
| SpawnRaw | ? | ? | Maps to Spawn |
| **UI: Marketplace-style visual** | ? | ?? | **Rework in progress** |
| **UI: Back navigation** | ? | ?? | **Planned — Marketplace doesn't have this** |
| **UI: Close button** | ? | ?? | **Planned — Marketplace only uses ESC** |
| IsVIP / NotIsVIP | ? | ? | Skip — VIP is Marketplace-specific |
| Achievement conditions | ? | ? | Skip — Marketplace leaderboard |
| Guild conditions & commands | ? | ? | Skip — Marketplace guilds |
| EpicMMO / Cozyheim / RustyClasses | ? | ? | Skip — third-party mods |
| Battlepass_EXP | ? | ? | Skip — Marketplace-specific |
| PlayVideo | ? | ? | Skip — Marketplace-specific |
| SendWebhook | ? | ? | Skip — Marketplace-specific |
| ModInstalled / NotModInstalled | ? | ? | Future |
| IronGateStatMore / Less | ? | ? | Future |
| NPCModelEquals / Not | ? | ? | Future |
| SetNPCModelLocal / Global | ? | ? | Future |
| SpawnWithData / SpawnXYZWithData | ? | ? | Future (needs CustomSpawnZDO) |
| GiveItemWithData / SetPlayerData | ? | ? | Future (needs CustomSpawnZDO) |
