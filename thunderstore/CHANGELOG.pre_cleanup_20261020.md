## 0.0.686
- The rig-proven build of 2026-10-20.

## 0.0.674
- The rig-proven build of 2026-10-20.

## 0.0.662
- Chat, quest tracker and territory box can be dragged at any time (no layout editor needed); dragging the tracker or territory box out of the minimap column keeps its spot, layout Reset puts it back.

## 0.0.660
- Setup NPC: every field is labelled and each dropdown is one tidy row.

## 0.0.659
- New Dressing room: three windows - Setup NPC (grouped fields), the Dressing room with the live NPC and 10 slots (helmet, chest, legs, cape, utility, both hands, both backs, pet), and the Wardrobe (Fires All The Items, equippables only; without it, your inventory).
- Appearance changes show on the NPC at once; nothing is saved until Apply Changes.
- The NPC's anchor orb shows only from the admin menu or the new "Show anchor" toggle.
- The admin NPC book (Shift+E) opens in the Fires look every time.
- NPC patrol routes are walked and kept after a relog.
- Live map zooms in to a single building, and stays sharp.
- Chat, quest journal, trader, NPC info and HUD boxes move and resize in the Fires layout mode; chat resizes in both directions and Reset restores it.
- Requires FiresUnifiedCore 0.2.580.

## 0.0.629
- 10-15 release: the rig-proven build (R-NIGHT). Requires FiresUnifiedCore 0.2.550.

## 0.0.628
- 10-15 release: the rig-proven build (R-NIGHT). Requires FiresUnifiedCore 0.2.550.

## 0.0.619
- 10-15 release: the rig-proven build (R-NIGHT). Requires FiresUnifiedCore 0.2.547.

* v0.0.567 - Static NPCs no longer trigger an AI-guard warning after load.

* v0.0.566 - Non-combat pets (follow, emotes, item stands) and fixes.

* v0.0.463 - liquid globe HUD, NPC animations
  - the health, stamina, eitr and adrenaline globes are liquid: they slosh as you move and show value popups and tooltips
  - the globe HUD remembers where you put it and how big you made it, even tucked against the screen edge
  - no more errors from the globe HUD when logging out
  - town NPCs wear outfits from the real 1.0 gear list
  - worker NPCs placed from a template count once on the leaderboard instead of twice
  - no more "could not find peer" warning for your own character when playing single player or hosting
  - NPCs given another model (a creature, or the female player body) walk, emote and greet on that model instead of standing still
  - the dressing room's idle, interact, greet and bye animation lists only offer animations that work for that NPC
  - NPCs placed from a template belong to whoever placed them, so as workers they can use that player's private chests and work inside their wards
  - your deaths and what killed you now count on the leaderboard and on memorial cards (a player's own death was never recorded before)
  - a helmet hides a companion's or NPC's hair the way it hides yours, and taking it off puts the original hair back
  - drawing territories on the map no longer churns 64 MB of memory every time it refreshes, so the map stutters less on long sessions
  - the territory map overlay also holds about 50 MB instead of about 270 MB, with the map drawn exactly as before; [Territories] CompactMapOverlayBuffers turns that off to compare

* v0.0.436 - updated for Valheim 1.0

* v0.0.420 - maintenance and fixes

