# Verdant's Ascent — Help Panel Update Plan

Audit of `UI/VerdantAscentHelpPanel.cs` against the live codebase. The panel
was last meaningfully updated before the Guild, Party, Chat-channel,
Companion-radial, and archetype-ability systems landed, and several
existing sections describe behavior that no longer matches the code.

## 1. Current state — sections in the panel today

The 18 sections registered in `SectionNames` (lines 57–77) and built by
`BuildXxx()` (lines 638–1288):

1. Getting Started, 2. NPC Types & Setup, 3. Quest System,
4. Quest Config Format, 5. Quest Journal, 6. Dialogue System,
7. Dialogue Config Format, 8. Companions Overview, 9. Companion Commands,
10. Companion Idle Work, 11. Territories, 12. Territory Config,
13. Globe HUD Overlay, 14. UI Customization, 15. Config Reference,
16. File & Folder Layout, 17. Keyboard Shortcuts, 18. Troubleshooting.

Every section uses helpers (`AddHeader`/`SubHeader`/`Paragraph`/`Bullet`/
`CodeBlock`/`Divider`) plus admin-only blocks gated on
`IsCurrentPlayerAdmin()`.

## 2. Outdated content

### Getting Started (`BuildGettingStarted`, l. 638)
- "Place a companion NPC in the world using the companion system" is too
  vague and doesn't mention the actual flow (tame → Shift+P place / Shift+U
  unstation / Shift+I idle toggle from
  `Modules/Companions/Commands/CompanionCommandSystem.cs:46–56`).
- Key Hotkeys block lists only J, E, Esc — missing G (guild screen), R
  (companion radial), H (idle radial), Tab (chat-channel cycle).

### Companion Commands (`BuildCompanionCommands`, l. 975)
- Describes only Follow/Stay + idle wander. The actual command surface is
  much larger:
  - Hold **R** on a companion → main radial menu (`UI/CompanionRadialMenu.cs`,
    trigger in `CompanionRadialMenuPatches.cs:26,49`).
  - Hold **H** on a companion → idle-behavior radial
    (`UI/CompanionIdleBehaviorRadial.cs`, trigger
    `CompanionRadialMenuPatches.cs:27,73`).
  - **Shift+Middle-Mouse** → ping a target (attack/move/sit/train/interact)
    (`Modules/Companions/Commands/CompanionCommandSystem.cs:14–32, 220+`).
  - **Shift+P / Shift+U / Shift+I** → NPC placement / unstation / toggle idle
    (same file, lines 51–56, 226–242).

### Companion Idle Work (`BuildCompanionIdleWork`, l. 1002)
- Behavior list is incomplete. Real list under
  `Modules/Companions/IdleBehaviors/`: ChestDeposit (V2), LootPickup (V2),
  FireTending (V2), Farming (multi-phase), WoodGathering, ResourceGathering,
  WorkstationInteraction (V2), SmelterOperator/, CraftingUpgrade (V2),
  SmartStorage, **Cooking**, **Fishing**, **BuildingRepair**,
  **BowTraining**. Missing the last four entirely.

### Keyboard Shortcuts (`BuildKeyboardShortcuts`, l. 1219)
- Same gaps as Getting Started. Add: G (guild screen, configurable —
  `Managers/ConfigManager.cs:154`), R / H (companion radials),
  Tab (chat-channel cycle — `Modules/Chat/ChatChannelUI.cs:264`),
  Shift+MMB / Shift+P / Shift+U / Shift+I.

### Companions Overview (`BuildCompanionsOverview`, l. 945)
- Names "Tank, Healer, Mage, Ranger, Rogue, etc." with no detail. Real
  enum (`Modules/Companions/Archetypes/ArchetypeDefinitions.cs:21–88`):
  Tank/Guardian, Paladin, Berserker, Rogue, Monk, Ranger, Mage, Healer
  (8 base classes), plus a full hybrid grid in
  `HybridArchetypeDefinitions.cs` (Crusader, Juggernaut, Shadow Guardian,
  …). No mention of group-role / synergy / status-effect / ability-unlock
  systems also living in `Modules/Companions/Archetypes/`.

### Config Reference (`BuildConfigReference`, l. 1153)
- Missing: Hotkeys section now has **GuildScreenKey** (G); whole **Guilds**
  category (GuildsEnabled, AllowGuildCreation, MaxGuildMembers,
  MaxGuildsPerWorld — `ConfigManager.cs:158–169`); **UI.GroupHud**
  (MaxRowsPerColumn, Columns, DefaultPosX/Y); **Companions.Behavior**
  StayModeWorkRadius (ChestSearchRadius default is now 50 m, not 15);
  **Territories.EnableDeepNorthMapOverlay**; whole **Progression** block
  (private keys, blocked/allowed/enforced lists, lock-equipment/crafting/
  building/cooking/eating/taming/guardian-power/boss-summons,
  AdminBypassProgression). Underwater Bubble defaults still match.

### File & Folder Layout (`BuildFileLayout`, l. 1195)
- No longer lists `Guilds/` storage directory or the per-world guild
  persistence file (see `Modules/Guilds/Storage/GuildStorage.cs`). No
  mention of `Compendium/` runtime data.

### Troubleshooting (`BuildTroubleshooting`, l. 1244)
- No entries for guild-sync failures, party-invite "player not found",
  chat-channel "coming soon" stub on Group, or companion archetype not
  applying when weapon requirements fail.

## 3. Missing features (no panel coverage at all)

Walked `Modules/`. Missing major submodules:

- **Guilds** (`Modules/Guilds/`, 20 files). Commands `/guild /gcreate
  /ginvite /gleave /gdisband` (`GuildModule.cs:93–113`), in-game G hotkey
  for screens (Main / Create / Search via `GuildScreenManager`), ranks
  Member/Officer/Coleader/Leader (`Core/GuildRank.cs`), invitations &
  applications, guild chat `/g <text>` (`Chat/GuildChatChannel.cs:18–60`),
  guild map pings `/gping` (`Map/GuildMapFeatures.cs:11`), shared guild
  storage, achievements (`Achievements/`), companion access &
  ownership transfer (`Companions/CompanionGuildAccess.cs`,
  `CompanionOwnershipTransfer.cs`), full sync RPC pipeline
  (`Network/GuildSyncManager.cs`).
- **Party** (`Modules/Party/PartyManager.cs`). Console commands `/invite
  <name> /disband /leave` (lines 787–807); YesNo invite popup; party
  state in `Player.m_customData` via `PartyData.cs`; leadership
  transfer; routed RPCs `FiresRPGmaker_PartyRoute`/`PartyMsg`.
- **Chat channels** (`Modules/Chat/`). Channels Local / Global / Group
  (stub) / Guild — column selector + Tab-to-cycle while typing
  (`ChatChannelManager.cs:22–82`, `ChatChannelUI.cs:264`); persisted in
  config under `[Chat] ActiveChannel`. Also: **media embeds** & GIF
  rendering (`ChatMediaEmbedManager.cs`, `GifDecoder.cs`).
- **Companion archetypes & abilities** (`Modules/Companions/Archetypes/`).
  Base archetypes, hybrid archetypes, ability unlock progression
  (`AbilityUnlockSystem.cs`), ability/skill decision systems, group
  role & synergy managers, archetype chat lines, taunt & status effects
  (`StatusEffects/StatusEffectManager.cs` — ~60+ named effects). None of
  this is mentioned today.
- **Companion idle behaviors** beyond the four listed (see §2).
- **Companion management UI** (`Modules/Companions/UI/`): Roster screen,
  Stats screen, Attribute screen, Inventory screen — none referenced.
- **Compendium** (`Modules/Compendium/`). Discovery tracker, entries,
  manager — player-facing feature with no help entry.
- **Progression** (`Modules/Progression/`). Private keys vs global keys,
  per-action lock keys (equipment/crafting/building/cooking/eating/
  taming/guardian-power/boss-summons). Mostly admin-facing but worth a
  short player-facing note explaining "why can't I craft this yet?".
- **VAngarde anti-cheat** (`Modules/VAngarde/`) — admin-only.
- **Discord integration** (`Modules/Discord/`) — admin-only.
- **NPC mode** (`Modules/Companions/NpcMode/`) — static NPC placement /
  finalize piece. Tied to Shift+P flow above.

## 4. Proposed structure

Reorganize from 18 sections into ~22, grouped by audience. Target
ordering puts player-facing first, then content authoring, then admin.

```
Getting Started
Companions / Overview & Archetypes
Companions / Commands & Radials
Companions / Idle Work
Companions / Management Screens          (new)
Combat & Abilities                       (new — archetype abilities)
Guilds                                   (new)
Party                                    (new)
Chat Channels                            (new)
Quests & Journal                         (merged: Quest System + Journal)
Dialogues                                (merged: Dialogue System overview)
Territories
Compendium                               (new)
Globe HUD Overlay
UI Customization
Keyboard Shortcuts
Troubleshooting
─── Authoring (admin) ───
Quest Config Format
Dialogue Config Format
Territory Config Format
─── Server (admin) ───
Config Reference
Progression / Key Locking                (new admin)
File & Folder Layout
```

### Section content checklist (concrete bullets to write)

**Getting Started** — refresh hotkey list (J quest, G guild, R radial,
H idle radial, Tab chat cycle, E interact, Esc close); add "Tame a
creature, then Shift+P to place / Shift+U to unstation" to quick-start.

**Companions / Overview & Archetypes** — split off from current
`BuildCompanionsOverview`. Bullets: 8 base archetypes with one-line
description each (source: `Archetypes/ArchetypeDefinitions.cs`). Note
hybrid archetypes are auto-assigned from main+sub combinations
(source: `HybridArchetypeDefinitions.cs`). Mention archetype scaling
per level and weapon requirements that gate ability unlocks.

**Companions / Commands & Radials** — replace current
`BuildCompanionCommands`. Bullets: Follow/Stay toggle; Hold-R radial
(actions list — read from `UI/CompanionRadialMenu.cs`); Hold-H idle
radial (toggle behaviors — read from `UI/CompanionIdleBehaviorRadial.cs`);
Shift+MMB ping (attack/move/sit/train/interact —
`CompanionCommandSystem.cs:14–32`); Shift+P place static NPC, Shift+U
unstation, Shift+I toggle idle (`CompanionCommandSystem.cs:51–56`).

**Companions / Idle Work** — extend current bullets with the missing
behaviors: Cooking (`CompanionCookingBehavior.cs`), Fishing
(`FishingBehavior.cs`), Building Repair (`BuildingRepairBehavior.cs`),
Bow Training (`BowTrainingBehavior.cs`), Resource Gathering
(`ResourceGathering/`), Smelter Operator (`SmelterOperator/`). Note that
each behavior can be toggled per companion via the H-radial
(source: `Modules/Companions/CompanionBehaviorToggles.cs`).

**Companions / Management Screens** (new) — Roster, Stats, Attribute,
Inventory screens (source: `Modules/Companions/UI/`). Briefly: how to
open each, what each shows.

**Combat & Abilities** (new) — Ability unlock by level
(`Archetypes/AbilityUnlockSystem.cs`); group synergy combos
(`GroupSynergyManager.cs`); role assignment (`GroupRoleManager.cs`);
status-effect categories: defensive, offensive, support, ultimate
(`StatusEffects/StatusEffectManager.cs:64–132`). Don't enumerate every
effect — group them and link to status icons.

**Guilds** (new) — Source of truth: `Modules/Guilds/GuildModule.cs`,
`GuildAPI.cs`, `Core/GuildRank.cs`, `Chat/GuildChatChannel.cs`,
`Map/GuildMapFeatures.cs`. Bullets:
- Press **G** to open the guild screen (Main / Create / Search /
  Members / Chat tabs).
- Console commands: `/guild`, `/gcreate <name> [desc]`, `/ginvite
  <player>`, `/gleave`, `/gdisband`.
- Chat: `/g <message>` for guild-only chat (or pick the Guild channel
  in the chat-column UI and just type).
- Map pings: `/gping` or Shift+map-ping for guild-only pings.
- Ranks: Member, Officer, Coleader, Leader (with permission
  matrix — keep brief).
- Limits: 20 members, 100 guilds per world (configurable).

**Party** (new) — Source: `Modules/Party/PartyManager.cs`. Bullets:
`/invite <name>` (sends YesNo popup), `/leave`, `/disband` (leader
only). Party state survives logout. Independent of guilds (party is
short-form, guild is persistent).

**Chat Channels** (new) — Source: `Modules/Chat/ChatChannelManager.cs`,
`ChatChannelUI.cs`. Bullets: channels Local / Global / Group / Guild;
Tab cycles channels while typing; click the channel column to pick
directly; selection persists across sessions; Group is a stub.
Mention chat media embeds (images / GIFs auto-render —
`ChatMediaEmbedManager.cs`).

**Quests & Journal** — merge current Quest System + Quest Journal
sections. Keep lifecycle, objectives, rewards, requirements, status
icons.

**Dialogues** — keep current Dialogue System overview (move detailed
config to admin sections).

**Territories** — keep, but add note that Deep North map overlay can
be toggled separately (`EnableDeepNorthMapOverlay`).

**Compendium** (new) — Source: `Modules/Compendium/`. Bullets: how
discoveries are tracked, where to view the compendium, that entries
unlock as the player encounters creatures/items.

**Globe HUD Overlay**, **UI Customization** — keep mostly as-is. Add
"`UseGlobeHud=false` reverts to vanilla HUD" note.

**Keyboard Shortcuts** — full refresh:
- General: J journal, G guild, E interact, Esc close.
- Companions: Hold R radial, Hold H idle radial, Shift+MMB ping,
  Shift+P place NPC, Shift+U unstation, Shift+I toggle idle.
- Chat: Enter open, Tab cycle channel, Esc close.
- Dialogue: 1–9/0 select, Backspace previous.
- Globe HUD (ESC menu open): drag, double-click resize.

**Troubleshooting** — add: "I can't see guild messages" (check
`GuildsEnabled`, that you're in a guild, that the channel column has
Guild selected); "Party invite says player not found" (must be online,
exact name); "Companion abilities not unlocking" (check archetype +
weapon requirement); "Group channel says coming soon" (stub).

**Quest Config Format / Dialogue Config Format / Territory Config**
— move under "Authoring" group, keep content as-is.

**Config Reference** — add Hotkeys.GuildScreenKey, full **Guilds**
group, **UI.GroupHud**, **Companions.Behavior.StayModeWorkRadius**
(and fix ChestSearchRadius default to 50), **Territories.
EnableDeepNorthMapOverlay**.

**Progression / Key Locking** (new admin) — Source:
`Modules/Progression/ProgressionConfig.cs`. Bullets: private vs global
keys, the eight per-action lock keys, AdminBypassProgression, blocked
message and effect toggles.

**File & Folder Layout** — add `Guilds/` (per-world JSON in
`GuildStorage`), `Compendium/` runtime cache.

---

### Implementation notes for the follow-up task

- Adding a section means: (1) extend `SectionNames` in the registry,
  (2) add the `case` to `PopulateContent` (l. 612), (3) write a
  `BuildXxx()` method using the existing helpers.
- Keep the admin-gated `IsCurrentPlayerAdmin()` blocks for any
  authoring / config / progression detail; players don't need to see
  config-key names.
- The csproj is old-style — `VerdantAscentHelpPanel.cs` is already
  listed, so no project-file edits are needed unless content is split
  into helper files. (Memory: csproj reference.)
- Source-of-truth for every new bullet is named above. When writing
  the section, re-read that file rather than copying these bullets
  verbatim — code may have moved since this audit.
