# FiresRPGmaker

**Updated for Valheim 1.0.**

An RPG toolkit for Valheim servers. Admins place NPCs that give quests, talk, trade, bank, gamble and more, and
players get parties, chat channels, territories, a quest journal, a leaderboard and a globe-style HUD. Part of the
Fires mod family (in game it is also called Verdant's Ascent).

## What's new in 0.0.846

**The large map and the live map work together.** Zoom in far enough on the large map over an area that is loaded
around you, and it fades smoothly into the live map, which shows buildings floor by floor. Zoom back out and it fades
back to the normal map. A switch in the map's header picks Auto (the default), Live map or Default. If you prefer the
old map, turn off **Auto live map when zoomed in** under `[Live map]`.

Other map changes:
- The live map opens showing all floors, and turning roofs off also hides the snow lying on them.
- The normal live map no longer marks doors; door marks stay on the RPGmaker route map.
- Admins get a biome painting mode on the territory map. It needs FiresAdminTerrain.

**NPCs behave properly after loading.** Placed NPCs no longer freeze or stand in a T-pose after the area loads, and
they turn their heads to look at you again.

**Easier windows.**
- The NPC book can be resized by dragging the grip in its corner, which now sits on the book itself.
- The NPC info panel opens fully on screen and sizes itself to its text, so the small / medium / large button is
  gone.
- The dressing preview no longer shows the wrong hair.

**Settings that moved.** Each Fires mod now keeps its own settings. Your saved values are copied over once, so
nothing resets:
- The companion settings this mod used to hold (the name and health bar over companions, how they behave and how they
  are saved) are now in FiresCompanions' config. It does not matter which of the two mods loads first.
- The leaderboard's **Batch stat writes** setting (`[Leaderboard]`) moved here from FiresUnifiedCore.

**Smaller fixes.**
- Test and admin console commands now need `devcommands` turned on.
- Recovering a companion checks with the server first, so you never end up with a second copy of it.
- Hover nameplates and hotkeys do less work while you are not using them.
- No warning at start-up when FiresAdminTerrain is not installed.
- A quieter log: repeating and test messages only show with debug logging turned on.

## What you get

- **NPCs with jobs.** Quest giver, Info, Dialogue, Trader, Banker, Marketplace (players sell to each other),
  Transmog, Mail, Buff station, Gambler, Leaderboard, Teleporter, or just decoration. Walk up and press E.
- **Dressing room.** Admins set up each NPC, pick its body and dress it in 10 gear slots.
- **Quests and journal.** Objectives, timers, rewards, quest chains and an on-screen quest tracker.
- **Branching dialogue** with choices that depend on what you've done.
- **Social.** Parties, chat channels (Local, Global, Group, Guild), player titles and factions.
- **Territories.** Map zones with their own rules. Owners can let their party or guild in and turn on raid and
  offline protection.
- **Live map.** See buildings floor by floor with roofs hidden, plus an optional live minimap. Zooming in on the large
  map switches to it on its own.
- **Compendium** of discoveries, and a **Leaderboard & Achievements** page, including deaths and what killed you.
- **Games.** Dice, rune and wheel bets at Gambler NPCs, plus chess, fanorona and poker.
- **Globe HUD.** Liquid health, stamina, eitr and adrenaline globes. Drag and resize it; it remembers its spot.
- **Non-combat pets** that follow you, emote and sit on item stands.
- **Progression locks (optional).** Per-player boss progress, and locks on gear, crafting, building and more until a
  player has beaten the right boss.

## How to use

- **E** on an NPC opens its window. Dialogue options: 1-9 and 0, Backspace goes back.
- **J** opens the quest journal. The leaderboard is in the F1 hub under Player > Leaderboard (no key by default).
- **Chat:** Tab cycles channels while typing.
- **Party commands:** `/invite <player>`, `/leave`, `/disband`. With FiresGuilds, `/g <message>` talks to your guild.
- `/feedback <message>` sends feedback to the server owner.
- **Stationed NPCs:** Shift+P stations a companion where it stands, Shift+U unstations it, Shift+I toggles
  wandering.
- **Admins:** Shift+E on an NPC opens the NPC book. The in-game "? Help" panel covers setup, content files and every
  admin command. Admin and test commands need `devcommands`.

## Install

- Install with a mod manager, or copy the `plugins` folder contents into `BepInEx/plugins`.
- Install on **every client and on the dedicated server**.

## Requires

- BepInExPack for Valheim
- FiresUnifiedCore
- Optional: FiresCompanions, FiresGuilds, FiresWayshrines and VAInventory are used when installed. FiresAdminTerrain
  adds biome painting to the territory map.

## Good to know

- Settings are in the F1 hub config page or `BepInEx/config/com.Fire.FiresRPGmaker.cfg`. Server settings override
  players' own. Companion settings are in FiresCompanions' config.
- Quests, dialogue, trader stock and other content live in folders under `BepInEx/config/FiresRPGmaker/` on the
  server. Changes reload without a restart.
- **Before starting a world:** boss progress is tracked per player by default, not server-wide. The progression
  locks are all off by default, and admins skip them.
- Bank, mail and marketplace data are kept on the server.
- Placed NPCs can't be killed by default; admins can change this per NPC in the NPC book.

## Issues / questions

https://discord.gg/zQRgHbqms4

## Licence

MIT License, Copyright (c) 2025 fire-VA. See the LICENSE file.
