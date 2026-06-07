# Companion Radial Menu

## Overview

The Companion Radial Menu provides quick access to companion commands without memorizing hotkeys. It features an animated outer ring for main actions and an inner ring for archetype/hybrid selection.

**Last Updated**: 2025-06-03

## Files

- `FiresNPCs\UI\CompanionRadialMenu.cs` - Main radial menu implementation
- `FiresNPCs\UI\CompanionRadialMenuPatches.cs` - Harmony patches for input handling
- `FiresNPCs\Debug\DebugRefs\VALHEIM_SPRITES_REFERENCE.txt` - Sprite mapping reference

## Usage

- **Open**: Hold `R` while hovering over a companion
- **Select**: Move mouse toward desired action
- **Execute**: Click or release `R`
- **Close**: Press `Escape` or `R` again

## Menu Structure

### Outer Ring (10 Actions)

| Position | Action | Type | Description |
|----------|--------|------|-------------|
| 1 | Follow/Stay | Toggle | Toggles between following player and staying in place |
| 2 | Inventory | Action | Opens companion inventory screen |
| 3 | Stats | Action | Opens companion stats (via inventory screen) |
| 4 | Gather | Toggle | Enables/disables resource gathering when stayed |
| 5 | Fires | Toggle | Enables/disables fire tending when stayed |
| 6 | Smelters | Toggle | Enables/disables smelter operation when stayed |
| 7 | Deposit | Action | Deposits items to nearby chests |
| 8 | Wander | Toggle | Allows/prevents idle wandering |
| 9 | Set Home | Action | Sets current position as home point |
| 10 | Dismiss | Action | Dismisses companion (can recall from roster) |

### Inner Ring (Dynamic Archetype/Hybrid Selection)

The inner ring content depends on companion level:

#### For Companions Below Level 30: Base Archetype Selection
Shows available archetypes based on the companion's inventory:

| Archetype | Required Equipment | Valheim Sprite |
|-----------|-------------------|----------------|
| Tank | Shield + Melee weapon | shield_serpentscale |
| Paladin | Shield + Melee weapon | SwordSilver |
| Berserker | Two-handed or melee weapon | AtgeirBlackmetal |
| Rogue | Knives | KnifeBlackMetal |
| Ranger | Bow | bow_draugrfang |
| Mage | Offensive staff | StaffFireball |
| Healer | Support staff | StaffShield |
| Monk | Always available (unarmed fallback) | FistsFenrirClaw |

#### For Companions Level 30+: Hybrid Archetype Selection
Shows available hybrid combinations based on main archetype + equipment:

| Main Archetype | Available Hybrids |
|----------------|-------------------|
| Tank | Crusader (Paladin), Juggernaut (Berserker), Shadow Guardian (Rogue), Warden (Ranger), Spellbreaker (Mage), Bastion (Healer), Iron Monk (Monk) |
| Paladin | Templar (Tank), Zealot (Berserker), Inquisitor (Rogue), Holy Archer (Ranger), Battle Priest (Mage), High Priest (Healer), Ascetic Knight (Monk) |
| Berserker | Ravager (Tank), Avenger (Paladin), Reaper (Rogue), Beast Hunter (Ranger), Blood Mage (Mage), Blood Knight (Healer), Battle Rager (Monk) |
| ... | (56 total hybrid combinations) |

The currently active archetype/hybrid shows a green dot indicator.

## Visual Design

- **Background**: Dark semi-transparent donut (outer) and circle (inner)
- **Icons**: Valheim sprites with fallback to procedurally generated white icons
- **Sprite Loading**: Caches all Valheim sprites at startup via `Resources.FindObjectsOfTypeAll<Sprite>()`
- **Status Dots**: Green = active/on, Gray = inactive/off
- **Highlight**: Gold glow on hover
- **Animation**: Staggered segment reveal with EaseOutBack scaling

### Icon Sprite Mapping

| Action | Primary Sprite | Fallbacks |
|--------|---------------|-----------|
| Follow/Stay | T_emote_wave | mapicon_player_32, skill_run |
| Inventory | item_background | Icon_Radial_ArmorUtilities, trophies |
| Stats | skills | texts_icon, trophies |
| Gather | axe_iron | axe_bronze, axe_stone, pickaxe_iron |
| Fires | Campfire | torch, brazierfloor01 |
| Smelters | smelter | blastfurnace, charcoal_kiln, forge |
| Deposit | chest_wood | chest_iron, chest_blackmetal, chest_private |
| Wander | T_emote_wave | T_emote_relax, skill_run |
| Set Home | mapicon_house | mapicon_bed, mapicon_fire, mapicon_pin |
| Dismiss | trash_icon | mapicon_death |

## State Persistence

Toggle states are saved to the companion's ZDO:
- `companion_gather_enabled` - Resource gathering toggle
- `companion_fires_enabled` - Fire tending toggle
- `companion_smelter_enabled` - Smelter operation toggle
- `companion_wander_enabled` - Wandering toggle
- `companion_archetype_override` - Manual archetype selection
- `companion_sub_archetype_override` - Manual hybrid sub-archetype selection

## Changelog

### 2025-06-01 - Initial Implementation
- Created full radial menu with 10 outer ring actions
- Added 6 combat stance options in inner ring
- Implemented animated open/close with staggered segments
- Added procedural icon generation for all actions
- Integrated HUD patches to hide crosshair when menu is open

### 2025-06-01 - Bug Fixes

#### Fix 1: Green Dots Overlapping Text
- **Problem**: Status indicator dots were positioned at `(0, 8)` which overlapped with label text
- **Solution**: Moved outer ring dots to `(0, -2)` and inner ring dots to `(0, -4)`
- **Result**: Dots now appear below the text labels

#### Fix 2: Stats Button Opens Wrong Screen
- **Problem**: `OpenStats()` called non-existent static `CompanionStatsScreen.Show()` method
- **Solution**: Changed to open companion inventory screen (which contains stats tab)
- **Result**: Stats button now properly opens inventory where stats are displayed

#### Fix 3: Deposit Shows "No Chests Nearby" for Following Companions
- **Problem**: Chest scan happened from behavior start position, not companion's current position
- **Solution**: 
  - Force chest scan from companion's current position using `ChestHelper.FindNearbyChests()`
  - Use `CompanionSettings.ChestSearchRadius` (50m) for search
  - Try V2 behavior first with `SetCommandedTarget()` to nearest chest
  - Show helpful message with chest count found
- **Result**: Following companions now properly find chests when deposit is triggered

#### Fix 4: Inner Ring Changed from Combat Stances to Archetype Selection
- **Problem**: Combat stances weren't wired to anything functional
- **Solution**:
  - Replaced hardcoded stance constants with dynamic archetype system
  - `GetAvailableArchetypes()` analyzes companion inventory for valid archetypes
  - `SetArchetypeOverride()` forces archetype change via `ArchetypeController.SetArchetype()`
  - Dynamic icons for each archetype class (Tank=shield, Berserker=crossed axes, etc.)
- **Result**: Inner ring now shows available archetypes based on equipment, allowing manual override of automatic detection

### 2025-06-03 - Valheim Sprite Integration & Hybrid Archetypes

#### Feature 1: Valheim Sprite Loading System
- **Added**: `InitializeValheimSpriteCache()` caches all Valheim sprites at startup
- **Added**: `GetValheimSprite(name)` retrieves sprites by name
- **Added**: `GetValheimSpriteWithFallbacks(names...)` tries multiple sprites
- **Implementation**: Uses `Resources.FindObjectsOfTypeAll<Sprite>()` to find all loaded sprites
- **Fallback**: Procedural icons are used when Valheim sprites aren't found
- **Reference**: See `VALHEIM_SPRITES_REFERENCE.txt` for full sprite list

#### Feature 2: Action Sprite Mappings
- **Mapped**: All 10 outer ring actions to appropriate Valheim sprites
- **Mapped**: All 8 archetype classes to weapon/ability sprites
- **Example Mappings**:
  - Follow/Stay ? T_emote_wave, mapicon_player_32
  - Gather ? axe_iron, pickaxe_iron
  - Fires ? Campfire, torch
  - Tank ? shield_serpentscale
  - Mage ? StaffFireball

#### Feature 3: Hybrid Archetype Selection (Level 30+)
- **Added**: `ActionHybridArchetypeBase = 100` for hybrid action IDs
- **Added**: `GetAvailableHybrids()` method to find valid hybrid combinations
- **Added**: `SetHybridArchetype()` method to apply hybrid selection
- **Logic**: 
  - Companions below level 30 see base archetype selection
  - Companions at/above level 30 see hybrid options for their main archetype
  - Hybrid options are filtered by equipment (must have gear for sub-archetype)
- **ZDO Storage**: `companion_sub_archetype_override` persists hybrid choice
- **Integration**: Calls `HybridAbilityManager.RefreshHybridDefinition()` after selection

## Technical Notes

### Icon Action IDs
```csharp
// Outer ring
private const int ActionFollow = 0;
private const int ActionGatherResources = 1;
private const int ActionTendFires = 2;
private const int ActionOperateSmelters = 3;
private const int ActionDepositItems = 4;
private const int ActionInventory = 5;
private const int ActionStats = 6;
private const int ActionWander = 7;
private const int ActionDismiss = 8;
private const int ActionSetHome = 9;

// Inner ring - Base archetypes (20-29)
private const int ActionArchetypeBase = 20;
// ActionArchetypeBase + (int)ArchetypeClass = action ID

// Inner ring - Hybrid archetypes (100+)
private const int ActionHybridArchetypeBase = 100;
// ActionHybridArchetypeBase + (int)SubArchetypeClass = action ID
```

### Archetype Detection Priority
1. Support Staff equipped ? Healer
2. Shield + Melee equipped ? Tank/Paladin
3. Offensive Staff equipped ? Mage
4. Bow equipped ? Ranger
5. Knives equipped ? Rogue
6. Two-handed/Melee ? Berserker
7. Fallback ? Monk

### HUD Integration
- `Hud.InRadial()` patch reports radial as active to prevent UI conflicts
- `Hud.UpdateCrosshair()` patch hides crosshair when menu is open
