# Chest Interaction Integration Plan

## Overview

This document tracks the integration of the unified `ChestInteractionService` into all behaviors that need to physically interact with chests.

---

## Architecture Summary

### Two-Layer System

| Layer | Class | Purpose |
|-------|-------|---------|
| **Data Layer** | `ResourceAccessService` | Find chests, check items, transfer items between inventories |
| **Physical Layer** | `ChestInteractionService` | Pathfind to chest, open, interact, return to origin |

### Movement Architecture (Post-Bug #8 Fix)

All movement now uses **vanilla pathfinding** with **authority coordination**:

```
???????????????????????????????????????????????????????????????????
?                    CORRECT APPROACH                              ?
?                                                                  ?
?   ChestInteractionService.Update()                              ?
?           ?                                                      ?
?   CompanionAI.RequestPathfindingMovement()                      ?
?           ?                                                      ?
?   UnifiedMovementAuthority.TryAcquireAuthority() ? Coordination ?
?           ?                                                      ?
?   BaseAI.MoveTo() ? VANILLA PATHFINDING with obstacle avoidance ?
???????????????????????????????????????????????????????????????????
```

---

## Integration Status

### ? COMPLETE - Core Services Created

| File | Status | Description |
|------|--------|-------------|
| `ChestInteractionService.cs` | ? Created | Physical chest interaction with pathfinding |
| `ResourceAccessService.cs` | ? Exists | Data layer for item operations |
| `CompanionAI.RequestPathfindingMovement()` | ? Created | Public API for behaviors to use vanilla pathfinding |
| `CompanionEvents` | ? Has events | `OnItemPulled`, `OnItemDeposited` events exist |

### ?? TODO - Behaviors Need Integration

The following behaviors currently use **direct movement** and **direct item transfers**. They need to be updated to use `ChestInteractionService` for proper pathfinding to chests.

#### Priority 1: Work Behaviors (Most Complex)

| Behavior | Current State | Needed Changes |
|----------|---------------|----------------|
| `SmelterOperatorBehavior` | Uses `_combatMovement.SetMoveDestination()` + direct `ChestHelper` calls | Add `ChestInteractionService` for chest trips |
| `ResourceGatheringBehavior` | Uses `TryMoveToPosition()` + direct `ChestHelper` calls | Add `ChestInteractionService` for deposit trips |
| `FireTendingBehavior` | Uses direct movement | Add `ChestInteractionService` for fuel retrieval |

#### Priority 2: Simple Behaviors

| Behavior | Current State | Needed Changes |
|----------|---------------|----------------|
| `ChestDepositBehavior` | Moves to chest, deposits all | Should use `ChestInteractionService` |
| `LootPickupBehavior` | Picks up items, sometimes deposits | Should use `ChestInteractionService` for deposit |

---

## How to Integrate ChestInteractionService

### Step 1: Add Field and Initialize

```csharp
// In your behavior class:
private ChestInteractionService _chestService;

public override void Initialize(CompanionController companion, CompanionIdleBehavior idleBehavior)
{
    base.Initialize(companion, idleBehavior);
    
    // Create ResourceAccessService first (if not already)
    _resources = new ResourceAccessService(_inventory, () => Transform.position);
    _resources.SearchRadius = CHEST_SEARCH_RADIUS;
    
    // Create ChestInteractionService
    _chestService = new ChestInteractionService(Companion, _resources, BehaviorName);
    _chestService.VerboseLogging = CompanionIdleBehavior.VerboseLogging;
}
```

### Step 2: Add Phase for Chest Operations

```csharp
private enum MyPhase
{
    // ... existing phases ...
    FetchingFromChest,    // Going to chest to pull items
    DepositingToChest,    // Going to chest to deposit items
}
```

### Step 3: Start Chest Operation When Needed

```csharp
// When you need items from a chest:
if (NeedsItemsFromChest())
{
    if (_chestService.StartPullOperation("Coal", 10, returnToOrigin: true))
    {
        _currentPhase = MyPhase.FetchingFromChest;
    }
    else
    {
        // No chest with item found - handle gracefully
    }
}

// When you need to deposit items:
if (NeedsToDeposit())
{
    if (_chestService.StartDepositOperation("CopperBar", returnToOrigin: true))
    {
        _currentPhase = MyPhase.DepositingToChest;
    }
}
```

### Step 4: Update Phase in Your Update Loop

```csharp
case MyPhase.FetchingFromChest:
case MyPhase.DepositingToChest:
    // Let ChestInteractionService handle pathfinding and interaction
    if (_chestService.Update())
    {
        // Operation complete
        if (_chestService.LastResult.Success)
        {
            // Success! Continue with next phase
            _currentPhase = NextPhase();
        }
        else
        {
            // Failed - handle error
            LogWarning($"Chest operation failed: {_chestService.LastResult.Message}");
            // Maybe skip this step or fail the behavior
        }
    }
    break;
```

### Step 5: Cancel on Behavior Cancel

```csharp
public override void Cancel()
{
    _chestService?.Cancel();
    base.Cancel();
}
```

---

## SmelterOperatorBehavior Integration Plan

The `SmelterOperatorBehavior` is the most complex case. Here's how to integrate:

### Current Flow (Direct Movement)
```
PullingFromChests phase:
1. Call ChestHelper.PullItemsByPrefabName() - instant, no pathfinding
2. Continue to next phase

DepositingToChests phase:
1. Call ChestHelper.DepositItemType() - instant, no pathfinding
2. Continue to next phase
```

### New Flow (With ChestInteractionService)
```
PullingFromChests phase:
1. Check if we need items AND they're in chests (not in inventory)
2. If yes: _chestService.StartPullOperation("Coal", needed)
3. New phase: WalkingToChestForPull
4. In WalkingToChestForPull: call _chestService.Update() until complete
5. Service pathfinds to chest, opens, pulls items, returns
6. Continue to FillingStation

DepositingToChests phase:
1. If inventory has items to deposit
2. _chestService.StartDepositOperation("CopperBar")
3. New phase: WalkingToChestForDeposit
4. In WalkingToChestForDeposit: call _chestService.Update() until complete
5. Continue to Complete or next cycle
```

### New Phases to Add

```csharp
private enum OperatePhase
{
    // ... existing phases ...
    
    // NEW: Chest interaction phases
    WalkingToChestForMaterials,   // Pathfinding to chest to get ore/fuel
    InteractingWithChestPull,     // Opening chest, pulling items
    ReturningFromChestPull,       // Walking back to station
    
    WalkingToChestForDeposit,     // Pathfinding to chest to deposit
    InteractingWithChestDeposit,  // Opening chest, depositing items
    ReturningFromChestDeposit,    // Walking back (optional)
}
```

---

## ResourceGatheringBehavior Integration Plan

When inventory is full during gathering, companion should:

1. Stop gathering
2. Pathfind to nearest chest
3. Deposit items
4. Return to gathering area
5. Continue gathering

```csharp
// In UpdateGathering():
if (_resources.IsInventoryFullOrNearlyFull())
{
    if (_chestService.StartDepositAllOperation(returnToOrigin: true))
    {
        _savedGatheringPosition = Transform.position;
        SetPhase(GatheringPhase.DepositingToChest);
    }
}
```

---

## Testing Checklist

After integration, verify:

### SmelterOperator
- [ ] Companion pathfinds to chest to get ore (doesn't teleport)
- [ ] Companion opens chest (animation plays)
- [ ] Companion pulls correct amount
- [ ] Companion pathfinds back to smelter
- [ ] Companion deposits output to chest with pathfinding
- [ ] Works with obstacles between smelter and chest

### ResourceGathering
- [ ] When inventory full, pathfinds to chest
- [ ] Deposits items correctly
- [ ] Returns to gathering area
- [ ] Continues gathering after deposit

### General
- [ ] Vanilla pathfinding avoids obstacles
- [ ] Authority system prevents movement conflicts
- [ ] Combat can interrupt chest operations (ChestInteractionService.Cancel())
- [ ] Events fire correctly (OnItemPulled, OnItemDeposited)

---

## Files to Modify

| File | Changes Needed |
|------|----------------|
| `SmelterOperatorBehavior.cs` | Add `_chestService` field and initialize |
| `SmelterOperatorBehavior.Lifecycle.cs` | Add chest interaction phases to Update() |
| `SmelterOperatorBehavior.StationPhases.cs` | Modify `UpdatePullingFromChests()` to use service |
| `SmelterOperatorBehavior.OutputPhases.cs` | Modify `UpdateDepositingToChests()` to use service |
| `ResourceGatheringBehavior.cs` | Add deposit integration when full |
| `ChestDepositBehavior.cs` | Refactor to use `ChestInteractionService` |

---

## Version History

| Date | Changes |
|------|---------|
| 2024 | Created `ChestInteractionService.cs` with pathfinding integration |
| 2024 | Documented integration plan |
| TODO | Integrate into `SmelterOperatorBehavior` |
| TODO | Integrate into `ResourceGatheringBehavior` |
| TODO | Integrate into other behaviors |
