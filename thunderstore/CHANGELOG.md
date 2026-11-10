## 0.0.846

### New
- Large map: zoom in far enough in a loaded area and it fades into the live map; zoom out and it fades back. Header switch Auto / Live map / Default, and a setting to turn the auto switch off.
- Biome painting mode on the territory map (needs FiresAdminTerrain).
### Changed
- Live map opens showing all floors; turning roofs off also hides the snow on them.
- The NPC book can be resized from its corner grip, which now sits on the book itself.
- NPC info panel opens fully on screen and sizes to its text; the small/medium/large button is gone.
- Placed NPCs turn their heads to look at you again.
- Companion billboard, behaviour and save settings moved to FiresCompanions (either load order works); leaderboard batch setting moved here from Core.
- Hover nameplates and hotkeys do less work when not in use.
- Test and admin commands need devcommands; recovering a companion asks the server first, so no duplicate copy.
- Quieter log: repeating and test messages only show with debug logging on.
### Fixed
- Placed NPCs no longer freeze or T-pose after loading.
- Dressing preview no longer shows the wrong hair.
- No warning at start-up when FiresAdminTerrain is not installed.

### Also in this release (0.0.846)
- The normal live map no longer marks doors; door marks stay on the RPG maker route map.

## 0.0.816
- Companion settings moved to FiresCompanions, and the leaderboard's batch setting moved here from Core; your saved values are copied over.

## 0.0.815
- Fixed: an error from footsteps on the NPC placement preview while placing an NPC. (NPCs)
- Fixed: the quest journal's resize grip now draws on the journal itself instead of being hidden. (UI)

## 0.0.814
- Fixed: the NPC book's quest page opened with an example wolf quest already typed in, and saving it created a real quest with example commands. A new quest page now starts empty; the examples only show as greyed hints and are never saved. (quests)
- New: a ready-to-use example quest, "example_wolf_hunt" (kill 10 wolves for 50 coins), in Quests\ExampleQuests.cfg. It is only created when missing and never overwrites your files. (quests)
- Fixed: the Auto-Complete checkbox on the NPC book's quest page could not be clicked. (quests)
- Fixed: a quest whose Requirements field held only a placeholder like "-" could never be accepted. Placeholders now count as no requirement, including in quests already saved that way. (quests)

## 0.0.811
- Fixed: the Studio NPC template preset rows called a Newtonsoft method the game doesn't ship (could error on use). Flagged by Hexium's compatibility check. (fix)

## 0.0.810
- Fixed map, quest tracker and food row text that could fail to draw and spam errors on the newer Unity version. (UI)
- Shared world pins: the server keeps map pins that every player sees. (map)
- The territory map overlay now redraws after territories finish loading, so it no longer shows empty on the host ("RedrawMapOverlayAfterLoad", on by default). (map)
- Less stutter on kills: stat saves are batched every few seconds instead of on each kill ("DebounceStatSaves", on by default), and the save file is written in the background. (performance)
- Uses much less memory: the mod's asset bundles load from files next to the DLL; keep those files in the same folder (it still works without them). (performance)
- NPC templates can be used as presets in Fires God Complex. (admin)
- Group HUD settings (show, rows, columns, position) now live in this mod's config; the unused "ShowGroupHud" key is removed. (config)
- Without Fires Advanced Pieces, NPC pieces show on the hammer's Misc tab, and the Static NPC can be placed even without Fires Companions. (building)
- Accepting a quest that is already active says "Quest already active!", and the quest journal and NPC/quest editor can be resized with the corner grip (saved per player). (quests)
- New optional background work for territory map paint and vault data, off by default. Fewer warnings in the log when optional mods are not installed. (performance)

## 0.0.791
- Fixed the skill-drain icon flashing in territories with no death penalty.
- Fixed the territory box sitting on top of the day/time box.
- Faster terrain blending in territories.
- NPC Studio snapshot / restore works again.

## 0.0.773
- Bug fixes and improvements.

## 0.0.720
- Territories: owners can let their party or guild in and turn on raid and offline protection. Fires started by players can't burn down protected bases.
- Added a Leaderboard page to the F1 hub (Player > Leaderboard). The leaderboard key is now unbound by default.
- Live map: step through a building floor by floor with roofs hidden, watch an NPC walk its route, and an optional live minimap (off by default).
- The globe HUD can be dragged and resized wide or tall.
- Chat now scrolls in a fixed-size window and grows upward while you type, drawn on top of the HUD.
- NPC book: cleaner and easier to read, opens on the Dressing room, and NPCs can now defend themselves when attacked. More body models to pick from.
- Fixed NPCs taking no damage with Invincible off, Max Health not applying on servers, and killed NPCs coming back with a random look.
- Fixed Dressing room tabs not switching, and party invites and territory access not syncing in multiplayer.

## 0.0.662
- Added a Dressing room for NPCs: set them up with clear fields and dress them in 10 gear slots from a Wardrobe.
- Look changes show on the NPC right away and only save when you press Apply Changes.
- NPC patrol routes now keep working after a relog.
- Fixed the pause menu sometimes not opening after closing the NPC book.
- The live map can zoom in to a single building.
- Chat, quest journal, trader and other boxes can be moved and resized.
- Territories now take your character's size into account.

## 0.0.567
- Fixed a warning about stationed NPCs after loading.

## 0.0.566
- Updated for Valheim 1.0.
- Added non-combat pets that follow you, emote and sit on item stands.
- Added the liquid globe HUD for health, stamina, eitr and adrenaline.
- NPCs given another body (a creature or the female body) now walk, emote and greet properly, and town NPCs wear 1.0 outfits.
- NPCs belong to whoever placed them, so workers can use that player's chests and work inside their wards.
- Your deaths and what killed you now count on the leaderboard.
- Helmets hide hair on companions and NPCs, and the territory map uses far less memory.
