# Companion Radial Menu Implementation Plan

## Overview

This document outlines the implementation of a radial menu system for companion commands, inspired by the Offline Companions mod but adapted for our VerdantsAscent companion system.

## Design Goals

1. **Quick Access** - Allow players to issue common commands without memorizing hotkeys
2. **Visual Feedback** - Clear indication of current companion state (following, staying, etc.)
3. **Context Awareness** - Show different options based on companion capabilities
4. **Smooth Animations** - Match Valheim's UI polish with animated open/close
5. **Controller Support** - Work with both mouse and gamepad input

## Menu Structure

### Outer Ring (Main Actions)
| Segment | Action | Toggle/Mode | Description |
|---------|--------|-------------|-------------|
| 1 | Follow | Toggle | Follow/Stay mode |
| 2 | Combat Stance | Mode | Cycle: Balanced/Aggressive/Defensive/Passive |
| 3 | Gather Resources | Mode | Enable resource gathering when stayed |
| 4 | Tend Fires | Mode | Enable fire/cooking tending when stayed |
| 5 | Operate Smelters | Mode | Enable smelter operation when stayed |
| 6 | Deposit Items | Action | Deposit companion inventory to nearby chests |
| 7 | Inventory | Action | Open companion inventory screen |
| 8 | Stats/Attributes | Action | Open companion stats/attribute screen |
| 9 | Wander | Toggle | Allow idle wandering vs staying put |
| 10 | Dismiss | Action | Dismiss companion (can resummon from roster) |

### Inner Ring (Combat Stances) - Only shown for combat-capable companions
| Segment | Stance | Description |
|---------|--------|-------------|
| 1 | Balanced | Default - engages threats normally |
| 2 | Aggressive | Proactively attacks nearby enemies |
| 3 | Defensive | Only attacks when owner is attacked |
| 4 | Passive | Won't attack unless commanded |
| 5 | Melee | Prefer melee weapons |
| 6 | Ranged | Prefer ranged weapons |

## Action IDs

```csharp
// Main actions (outer ring)
public const int ActionFollow = 0;
public const int ActionGatherResources = 1;
public const int ActionTendFires = 2;
public const int ActionOperateSmelters = 3;
public const int ActionDepositItems = 4;
public const int ActionInventory = 5;
public const int ActionStats = 6;
public const int ActionWander = 7;
public const int ActionDismiss = 8;
public const int ActionSetHome = 9;

// Combat stances (inner ring)
public const int ActionStanceBalanced = 20;
public const int ActionStanceAggressive = 21;
public const int ActionStanceDefensive = 22;
public const int ActionStancePassive = 23;
public const int ActionStanceMelee = 24;
public const int ActionStanceRanged = 25;
```

## Implementation Files

### New Files to Create
1. `FiresNPCs\UI\CompanionRadialMenu.cs` - Main radial menu UI
2. `FiresNPCs\UI\CompanionRadialMenuPatches.cs` - Harmony patches for HUD integration

### Files to Modify
1. `FiresNPCs\Modules\Companions\CompanionPatches.cs` - Add interaction hook

## Key Features

### 1. Show/Hide Trigger
- **Keyboard**: Hold `R` (configurable) while hovering over companion
- **Gamepad**: Hold `Select/Back` button while near companion
- Releasing the key closes the menu and executes selected action

### 2. Visual Design
- Dark semi-transparent background circle
- Inner donut for stance selection
- Outer ring for main actions
- Icons with colored backgrounds indicating status
- Active state dots (green = on, gray = off)
- Center text showing companion name and hovered action

### 3. Animation
- Smooth open/close animation (0.45s duration)
- Staggered segment reveal
- EaseOutBack scaling for "pop" effect
- Segments rotate in during open

### 4. Selection Logic
- Mouse/Stick distance from center determines ring selection
- Dead zone in center (30px mouse, 0.3 stick magnitude)
- Angle-based segment selection

## Integration with Existing Systems

### CompanionAI Integration
- Follow/Stay: `CompanionAI.SetShouldFollow()` and `SetFollowTarget()`
- Stay position: `CompanionAI.SetStayPosition()`

### CompanionIdleBehavior Integration
- Resource gathering: Toggle via behavior enable/disable
- Fire tending: Toggle via behavior enable/disable
- Smelter operation: Toggle via behavior enable/disable
- Wander: `CompanionIdleBehavior.SetHomePosition()` + radius settings

### CompanionCombatMovement Integration
- Combat stances: New property to add

### CompanionInventory Integration
- Deposit items: Trigger chest deposit behavior
- Open inventory: `CompanionInventoryScreen.Show()`

### CompanionController Integration
- Dismiss: `CompanionController.ForceDismiss()`

## State Persistence

States that should be saved to ZDO/Vault:
- Follow/Stay mode ? (already saved)
- Combat stance (NEW - need to add)
- Work behavior toggles (NEW - need to add)
- Wander toggle ? (uses home position)

## Configuration Options

Add to ConfigManager:
```csharp
public ConfigEntry<KeyCode> configRadialMenuKey;
public ConfigEntry<bool> configRadialMenuEnabled;
```

## Implementation Order

1. ? Create plan document (this file)
2. ? Create CompanionRadialMenu.cs with basic structure
3. ? Implement UI building (circles, segments)
4. ? Implement animation system
5. ? Implement input handling
6. ? Connect to existing companion systems
7. ? Add combat stance system (via ZDO)
8. ? Add work behavior toggles (via ZDO)
9. ? Add Harmony patches for HUD integration
10. ?? Test and polish

## Changelog

### 2025-06-01
- Initial plan created
- Defined menu structure and action IDs
- Identified integration points with existing systems
- **COMPLETED**: Full radial menu implementation
  - Created `CompanionRadialMenu.cs` (1400+ lines)
  - Created `CompanionRadialMenuPatches.cs` for input handling
  - Outer ring with 10 actions: Follow, Inventory, Stats, Gather, Fires, Smelters, Deposit, Wander, Set Home, Dismiss
  - Inner ring with 6 combat stances: Balanced, Aggressive, Defensive, Passive, Melee, Ranged
  - Animated open/close with staggered segment reveal
  - Procedural icon generation for all actions
  - HUD integration patches to hide crosshair when menu is open
  - Hold R (configurable) to open, release to close
  - Mouse/gamepad input for selection

### Chest Deposit Bug Fix (2025-06-01)
- **BUG**: Companion says inventory is full, interacts with chest, but doesn't deposit items
- **ROOT CAUSE**: Missing `InventoryNeedsDeposit()` method in `ChestDepositBehaviorV2.cs`
  - The method was called in `InventoryPriority` getter but never defined
  - This caused the behavior to never report high priority when inventory was full
- **FIX**: Added the missing `InventoryNeedsDeposit()` method
- **DEBUGGING**: Added verbose logging to `ShouldDepositItem()` and `DepositItemsToCluster()`
  - Now logs why each item is skipped or deposited
  - Logs total counts: deposited, skipped (keep types), failed
  - Enable with: `CompanionIdleBehavior.VerboseLogging = true`
