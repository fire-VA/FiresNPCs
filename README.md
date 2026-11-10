# FiresRPGmaker

**Updated for Valheim 1.0.**

An RPG toolkit for Valheim servers: placed NPCs that give quests, run dialogue, trade, bank, gamble and more,
plus parties, chat channels, territories, a quest journal, a leaderboard and a globe-style HUD. Part of the
Fires mod family (in game it is also called Verdant's Ascent).

## What it does

Admins place NPCs in the world and give each one a role. Players walk up, press E and get that role's window.
Content (quests, dialogue trees, trader stock and so on) lives in plain config files on the server.

## Key features

- **NPC roles.** Quest, Info, Dialogue, Trader, Banker, Marketplace (player-to-player listings), Transmog, Mail,
  Buff station, Gambler, Leaderboard / Achievement, Teleporter, or None (decoration). One role per NPC; several
  NPCs can share the same profile.
- **Quests and journal.** Quest lists, objectives, timers shown at the top left, rewards and requirements,
  chain quests, and an on-screen quest tracker. Press J for the journal.
- **Branching dialogue.** Dialogue trees with conditional options, commands and icons. Pick options with 1-9 and
  0, Backspace goes back.
- **Static NPCs.** Station a companion where it stands as a world NPC (innkeeper, vendor, quest giver): Shift + P
  to station, Shift + U to unstation, Shift + I to toggle idle wandering. Placed NPCs can't be killed by
  default.
- **Social.** Parties (`/invite <player>`, `/leave`, `/disband`), chat channels (Local, Global, Group, Guild;
  Tab cycles them while typing), player titles and factions. Guilds themselves come from the FiresGuilds mod;
  with it installed, `/g <message>` sends to your guild.
- **Territories.** Map zones with their own rules and a minimap overlay.
- **Compendium** of discoveries, and a **Leaderboard & Achievements** panel (Player > Leaderboard in the F1 hub), including deaths and what
  killed you.
- **Games.** Dice, rune and wheel wagers at Gambler NPCs, plus chess, fanorona and poker.
- **Globe HUD.** Liquid health, stamina, eitr and adrenaline globes with value popups and tooltips. With the
  Esc menu open, drag to move it and double-click to resize; it remembers its place and size.
- **Non-combat pets** that follow you, emote and sit on item stands, with size limits under `[Pets]`.
- **Progression locking (server).** Optional per-player boss keys and locks on equipment, crafting, building,
  cooking, eating, taming, guardian powers and boss summons until a player has earned a key.

## Status of this release

Checked in game by the automated `firesnpcs_test` run (step names in brackets):

**Proven**
- NPC book: the unified window with every tab, readable text, the HUD hidden while it is open
  (book_open, book_tabs).
- Patrol route popup, map view and Preview (patrol popup / map / preview / shot / close).
- Trader, Gambler and Mail windows (trader, gambler, mail).
- Live map on the large map (live_map) and its Level widget's buttons (live_map_widget).
- Live map Roofs toggle and Level stepping on a 3-floor player build (live_map_roofs, live_map_levels).
- Patrol route editor: drawing points, a point on an upper floor, Save, then reopening with the same points
  (route_draw, route_second_floor, route_persist, route_preview).

**In progress (not proven yet)**
- Door outlines on the live map: drawn in red (seen in screenshots), the automatic check is still pending.
- An NPC walking its saved patrol route: the route saves and is assigned, but a placed (static) NPC does not
  start moving yet. Most likely its static placement keeps it in place; still being investigated.
- Floors of vanilla multi-storey locations (towers) on the live map.

## Requires

- BepInExPack for Valheim
- FiresUnifiedCore 0.2.550 or newer
- Optional: FiresCompanions (the tamed companion system: follow/stay, archetypes, radial menus, kennel),
  FiresGuilds (guilds), FiresWayshrines and VAInventory are used when present.

## Installation

- Install with a mod manager, or copy the `plugins` folder contents into `BepInEx/plugins`.
- Install on **every client and on the dedicated server**. Content files are read on the server and sent to
  clients, so on a dedicated server edit them there.

## Configuration

In the Fires config window only the keybinds and main on/off switches show; turn on **Advanced** for the rest. Window sizes and positions are saved when you drag them, so they are not listed.

Main file: `BepInEx/config/com.Fire.FiresRPGmaker.cfg`, also editable in the Fires config window (F1, or F8 when
another configuration manager is installed). Content lives in folders under `BepInEx/config/FiresRPGmaker/`
(Quests, QuestProfiles, Dialogues, Traders, Bankers, Gamblers, Marketplace, Mail, Territories, Factions,
ServerInfos and more), and changes on the server reload without a restart.

Worth knowing:

- `[General] ServerAuthority` (false) - lock config changes to admins.
- `[Hotkeys] QuestJournalKey` (J), `LeaderboardKey` (unbound; the leaderboard is in the F1 hub) - key combinations work; set None to disable.
- `[UI] UseGlobeHud` (true), `UseBookAdminPanel` (true), `ExtendedInteractRange`, font choices, panel sizes.
- `[Chat] CustomChatWindow` (true) and its offset and scale.
- `[Territories] EnableMapOverlay` (true), `EnableDeepNorthMapOverlay` (true; turn off if another mod draws its
  own), `AdminBypassTerritoryRestrictions` (true).
- `[Companions.StaticNpcs] StaticNpcInvulnerable` (true), `NameplateStyle`; `[Companions.Persistence]
  PersistentCompanions` and `KeepZonesLoadedForOfflinePlayers` (both true, server-synced).
- `[General] QuestRewardOverflowDelivery` (Mail) - how rewards arrive when your inventory is full.
- `[Chess]`, `[Fanorona]`, `[Poker]` - difficulty, buy-in, blinds, scale and position.
- `[General] FeedbackWebhookUrl` - Discord webhook for `/feedback` (server side; keep it private).

**Progression (server-synced) - check these before you start a world.** `[Progression.Keys] UsePrivateKeys`
and `BlockAllGlobalKeys` are **on by default**: boss progress is tracked per player instead of server-wide.
Every lock under `[Progression.Locking]` (LockEquipment, LockCrafting, LockBuilding and the rest) is **off by
default**, and admins bypass them (`AdminBypass` = true).

## Commands

Player: `/invite <player>`, `/leave`, `/disband`, `/g <message>` (with FiresGuilds), `/feedback <message>`.

Admin (console, type a command with no arguments for usage):

- Shift + E on an NPC opens the admin book to set its role and profile.
- `firesnpcs_pull_configs` / `firesnpcs_push_configs` - copy the server's content to your machine and back.
- `firesnpcs_reload [gambler|marketplace|banker|mail|transmog|leaderboard|achievement|all]` - reload those
  content files from disk.
- Companion vault: `companioninfo`, `findcompanion <name>`, `listlostcompanions`, `recovercompanion <name>`,
  `restorecompanions`, `savevault`, `vaultclean`.
- Destructive: `deletecompanion <name>`, `purgeallcompanions CONFIRM`, `destroyallcompanionnpcs CONFIRM`.
- Quests and keys: `listquests`, `startquest`, `queststatus`, `va_setprivatekey`, `va_listprivatekeys`,
  `va_resetprivatekeys` and related `va_` key commands.
- Leaderboard: `firesnpcs_leaderboard_newseason`, `firesnpcs_leaderboard_wipe`.

## Multiplayer / server notes

- Settings marked synced come from the server and override client values.
- Player data (bank, mail, marketplace) is kept on the server in the shared Fires vault.
- The in-game "? Help" panel has a full FiresRPGmaker section; its setup, file format and command pages show
  for admins.

## Issues / questions

https://discord.gg/zQRgHbqms4

## Licence

MIT License, Copyright (c) 2025 fire-VA. See the LICENSE file.

## Screenshots

<!-- ![rpg_globes](HOSTED_URL/rpg_globes.png) -->
<!-- ![rpg_quest_tracker](HOSTED_URL/rpg_quest_tracker.png) -->
<!-- ![rpg_npc_book](HOSTED_URL/rpg_npc_book.png) -->
<!-- ![rpg_dressing_room](HOSTED_URL/rpg_dressing_room.png) -->
<!-- ![rpg_leaderboard](HOSTED_URL/rpg_leaderboard.png) -->
<!-- ![rpg_chat](HOSTED_URL/rpg_chat.png) -->
