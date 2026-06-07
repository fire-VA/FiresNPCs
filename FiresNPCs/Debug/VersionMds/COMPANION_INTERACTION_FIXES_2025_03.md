# Companion Interaction Fixes - March 2025

## Overview
This document tracks fixes made to companion interaction systems, including chest interactions, smelter operations, tool retrieval, and interaction point calculations.

---

## Fix #1: Chest Lid Animation (SetInUse)

### Problem
Companions were interacting with chests without properly opening/closing the chest lid:
- No visual lid animation (the `m_open`/`m_closed` GameObjects weren't being toggled)
- No open/close sound effects
- Only the ZDO `s_inUse` flag was being set directly

### Root Cause
The original implementation only set `zdo.Set(ZDOVars.s_inUse, 1)` directly, which:
- Updates the ZDO state for multiplayer sync
- But does NOT trigger the visual/audio effects

### How Valheim's Container Works
From decompiled `Container.cs`:
```csharp
public GameObject m_open;       // Shows when chest is open (e.g., "woodchesttop_open")
public GameObject m_closed;     // Shows when chest is closed (e.g., "woodchesttop_closed")
public EffectList m_openEffects;   // Sound/particles when opening
public EffectList m_closeEffects;  // Sound/particles when closing

public void SetInUse(bool inUse)
{
    if (!m_nview.IsOwner() || m_inUse == inUse) return;
    
    m_inUse = inUse;
    UpdateUseVisual();  // Toggles m_open/m_closed GameObjects
    
    if (inUse)
        m_openEffects.Create(...);   // Play open sound
    else
        m_closeEffects.Create(...);  // Play close sound
}

private void UpdateUseVisual()
{
    bool flag = m_inUse;
    m_open.SetActive(flag);      // Show lid-open model
    m_closed.SetActive(!flag);   // Hide lid-closed model
}
```

### Fix Applied
**File:** `ChestInteractionService.cs`

Changed `TryOpenChest()` to use `Container.SetInUse(true)`:
```csharp
private void TryOpenChest()
{
    // Claim ownership so SetInUse works properly
    if (!_targetChestNView.IsOwner())
        _targetChestNView.ClaimOwnership();

    // Use proper SetInUse which handles:
    // - m_open/m_closed visual toggling
    // - m_openEffects sound/particles
    // - ZDO s_inUse flag
    _targetChest.SetInUse(true);
}
```

Changed `TryCloseChest()` to use `Container.SetInUse(false)`:
```csharp
private void TryCloseChest()
{
    if (!_targetChestNView.IsOwner())
        _targetChestNView.ClaimOwnership();

    // Use proper SetInUse for close
    _targetChest.SetInUse(false);
}
```

### Expected Behavior After Fix
1. Companion walks to front of chest
2. **Chest lid opens visually** with animation and sound
3. Companion transfers items (chest stays open)
4. **Chest lid closes visually** with animation and sound
5. Companion returns to origin

---

## Fix #2: Tightened Interaction Distances

### Problem
Companions could interact with chests and smelters from too far away (2.5m+), which allowed them to:
- Stand behind chests (where the lid doesn't open toward them)
- Stand on the wrong side of kilns/smelters
- Not walk around objects to the correct interaction point

### Root Cause
The `ARRIVAL_THRESHOLD` was too loose, so companions could "arrive" at an interaction point even if they were on the wrong side of the object.

### Fix Applied
**File:** `InteractionPointHelper.cs`

Tightened the constants:
```csharp
// BEFORE
public const float DEFAULT_INTERACTION_DISTANCE = 1.5f;
public const float ARRIVAL_THRESHOLD = 1.0f;
public const float MAX_INTERACTION_RANGE = 1.5f;

// AFTER
public const float DEFAULT_INTERACTION_DISTANCE = 1.3f;  // Stand closer
public const float ARRIVAL_THRESHOLD = 0.8f;             // Must be at exact spot
public const float MAX_INTERACTION_RANGE = 1.2f;         // Stricter range check
```

**File:** `SmelterOperatorBehavior.cs`

Now uses centralized `InteractionPointHelper` constants:
```csharp
private static float INTERACTION_DISTANCE => InteractionPointHelper.MAX_INTERACTION_RANGE;
private static float ARRIVAL_DISTANCE => InteractionPointHelper.ARRIVAL_THRESHOLD;
```

**File:** `ResourceGatheringBehavior.Tools.cs`

Reduced chest interaction distance:
```csharp
// BEFORE
private const float CHEST_INTERACTION_DISTANCE = 2.5f;

// AFTER  
private const float CHEST_INTERACTION_DISTANCE = 1.5f;
```

### How This Fixes the Problem

**Before:**
- Interaction point calculated as 1.5m in front of chest
- Arrival threshold was 1.0m
- If companion is 1.0m away from interaction point BUT on wrong side, they were "close enough"

**After:**
- Interaction point calculated as 1.3m in front of chest
- Arrival threshold is 0.8m (tight)
- Companion must be within 0.8m of the calculated front point
- If on wrong side, they're NOT within 0.8m ? must pathfind around

---

## Fix #3: Tool Retrieval from Nearby Chests

### Problem
When commanded to gather resources, if the companion didn't have the required tool (pickaxe/axe) in their inventory, they would fail - even if the tool was in a nearby chest.

### Fix Applied
**Files:** 
- `ResourceGatheringBehavior.cs` - Added new phases
- `ResourceGatheringBehavior.Tools.cs` - Added tool retrieval methods
- `ResourceGatheringBehavior.Lifecycle.cs` - Updated Start() and phase routing

### New Phases Added
```csharp
private enum GatherPhase
{
    FindingResource,
    MovingToChestForTool,      // NEW: Walk to chest to get required tool
    RetrievingToolFromChest,   // NEW: Open chest and pull tool
    MovingToResource,
    // ... existing phases
}
```

### New Methods Added
```csharp
// Check if companion has tool in inventory (not chests)
private bool HasToolEquippedOrInInventory(ResourceDataHelper.ResourceData resource)

// Find tool in nearby chests, set up _toolChest and _toolToPull
private bool TryFindToolInNearbyChests(ResourceDataHelper.ResourceData resource)

// Phase: Walking to chest containing tool
private bool UpdateMovingToChestForTool()

// Phase: Opening chest, pulling tool, equipping it
private bool UpdateRetrievingToolFromChest()

// Transfer tool from chest to inventory
private bool TryInstantPullToolFromChest()
```

### Flow
1. Companion commanded to gather ore
2. `Start()` checks: Do I have a pickaxe in inventory? **NO**
3. `TryFindToolInNearbyChests()`: Is there a pickaxe in any nearby chest? **YES**
4. Set `_toolChest` and `_toolToPull`, switch to `MovingToChestForTool` phase
5. Walk to the chest (uses proper interaction point)
6. `RetrievingToolFromChest`: Open chest, play animation, pull tool
7. `EquipBestToolForResource()`: Equip the tool
8. Switch to `MovingToResource` phase and proceed normally

### Fallback
If companion can't reach the chest in 30 seconds, falls back to instant pull (teleport item).

---

## Fix #4: SmelterOperator Circuit Breaker Fix

### Problem
The circuit breaker for consecutive failed pulls was checking `HasWoodForKiln()` after pulling, which returned false even when items were successfully added (due to inventory caching).

### Fix Applied
**File:** `SmelterOperatorBehavior.Helpers.cs`

Track pull count directly instead of re-checking inventory:
```csharp
private int _lastPullCount = 0;

private void PullMaterialsFromChests()
{
    // ... pull logic ...
    _lastPullCount = totalPulled;  // Track actual pull count
}
```

**File:** `SmelterOperatorBehavior.StationPhases.cs`

Check `_lastPullCount` instead of inventory state:
```csharp
private bool UpdatePullingFromChests()
{
    PullMaterialsFromChests();
    
    // Check actual pull count, not inventory state
    if (_lastPullCount > 0)
    {
        _consecutiveFailedPulls = 0;  // Reset on success
        // Continue to filling...
    }
    else
    {
        _consecutiveFailedPulls++;
        // Handle failure...
    }
}
```

---

## Fix #5: Resource Gathering Fallback for Smelter

### Problem
When kiln/smelter needed wood/ore but none was in chests, the companion would just say "no materials" and abort, even if there were trees/ore nearby to gather.

### Fix Applied
**File:** `SmelterOperatorBehavior.StationPhases.cs`

Added `TryStartResourceGatheringForMaterial()` method:
```csharp
private bool TryStartResourceGatheringForMaterial(string materialType)
{
    // Check if companion has the required tool
    bool needsAxe = materialType == "wood";
    bool needsPickaxe = materialType == "ore";
    
    // Search for nearby resources
    if (needsAxe && HasAxeEquipped())
    {
        var tree = FindNearbyTree();
        if (tree != null)
        {
            _needsResourceGathering = true;
            _resourceGatheringMaterial = "wood";
            return true;
        }
    }
    
    // Similar for ore...
}
```

When no materials in chests:
1. Check if companion has the right tool (axe/pickaxe)
2. Search for nearby resources (trees/ore deposits)
3. If found, set `_needsResourceGathering = true`
4. Complete behavior, let IdleBehavior rotation start ResourceGathering

---

## Summary of Files Modified

| File | Changes |
|------|---------|
| `ChestInteractionService.cs` | Use `SetInUse()` for proper lid animation |
| `InteractionPointHelper.cs` | Tightened distances (1.3m/0.8m/1.2m) |
| `SmelterOperatorBehavior.cs` | Use centralized interaction constants |
| `SmelterOperatorBehavior.StationPhases.cs` | Circuit breaker fix, resource gathering fallback |
| `SmelterOperatorBehavior.Helpers.cs` | `_lastPullCount` tracking |
| `ResourceGatheringBehavior.cs` | New phases for tool retrieval |
| `ResourceGatheringBehavior.Tools.cs` | Tool retrieval methods, tightened chest distance |
| `ResourceGatheringBehavior.Lifecycle.cs` | Phase routing for tool retrieval |
| `CompanionChatHelper.cs` | Added `NoToolAvailable` message |

---

## Testing Checklist

### Chest Interaction
- [ ] Companion opens chest - lid visually opens with sound
- [ ] Companion closes chest - lid visually closes with sound
- [ ] Companion stands IN FRONT of chest (where lid opens)
- [ ] Companion walks around chest if approaching from back

### Smelter/Kiln Interaction
- [ ] Companion stands at correct side of kiln
- [ ] Companion doesn't interact from behind smelter
- [ ] Circuit breaker works - doesn't spam failed pulls
- [ ] Falls back to resource gathering when no materials

### Tool Retrieval
- [ ] Commanded to mine without pickaxe ? walks to chest
- [ ] Opens chest, pulls pickaxe, equips it
- [ ] Proceeds to mine ore
- [ ] Works for axes and trees too

---

## Changelog

| Date | Fix | Description |
|------|-----|-------------|
| 2025-03-01 | Chest Lid Animation | Use `SetInUse()` for proper visual/audio |
| 2025-03-01 | Interaction Distances | Tightened to 1.3m/0.8m/1.2m |
| 2025-03-01 | Tool Retrieval | Physical walk to chest for tools |
| 2025-03-01 | Circuit Breaker | Track `_lastPullCount` directly |
| 2025-03-01 | Resource Fallback | Gather resources when chests empty |
| 2025-03-01 | **Interactable Point Detection** | Find actual Switch positions, not prefab centers |

---

## Fix #6: Interactable Point Detection (Smelter Teleport Loop Fix)

### Problem
Companions were getting stuck in a teleport loop when commanded to operate smelters/kilns:
- Companion walks to calculated interaction point
- Never gets close enough (2.7-2.9m away instead of 1.8m threshold)
- Pathfinding times out after 8 seconds
- Teleports to "standoff position" but still too far
- Repeat forever

### Root Cause
The `_targetPosition` was being set to `_commandedTarget.transform.position` which is the **prefab center**, not the actual interactable point.

Looking at the decompiled `Smelter` class:
```csharp
public Switch m_addWoodSwitch;  // Actual interaction point for fuel
public Switch m_addOreSwitch;   // Actual interaction point for ore
public Switch m_emptyOreSwitch; // Actual interaction point for output
```

These `Switch` components are child GameObjects positioned at specific locations on the smelter/kiln prefab. The prefab center is often inside/above the model geometry, which is unreachable.

### Fix Applied

**File: `InteractionPointHelper.cs`** - Complete rewrite to find actual interactable points:

```csharp
/// <summary>
/// Finds the best interactable point on a Smelter (kiln, smelter, etc.)
/// Smelters have multiple Switch components - we find the most accessible one.
/// </summary>
public static Vector3 FindSmelterInteractablePoint(Smelter smelter)
{
    List<Vector3> switchPositions = new List<Vector3>();
    
    // Get positions of all Switch components
    if (smelter.m_addOreSwitch != null)
        switchPositions.Add(smelter.m_addOreSwitch.transform.position);
    if (smelter.m_addWoodSwitch != null)
        switchPositions.Add(smelter.m_addWoodSwitch.transform.position);
    if (smelter.m_emptyOreSwitch != null)
        switchPositions.Add(smelter.m_emptyOreSwitch.transform.position);
    
    // Return average position (center of interaction area)
    if (switchPositions.Count > 0)
    {
        Vector3 avgPos = Vector3.zero;
        foreach (var pos in switchPositions)
            avgPos += pos;
        return avgPos / switchPositions.Count;
    }
    
    // Fallback to prefab position
    return smelter.transform.position;
}
```

**File: `SmelterOperatorBehavior.Lifecycle.cs`** - Use the new helper:

```csharp
// BEFORE (broken)
_targetPosition = _commandedTarget.transform.position;

// AFTER (fixed)
_targetPosition = InteractionPointHelper.FindSmelterInteractablePoint(_targetSmelter);
```

### Also Updated Constants

**`InteractionPointHelper.cs`** - Loosened thresholds to work with Valheim's 2m use distance:
```csharp
// BEFORE (too tight - caused teleport loops)
public const float DEFAULT_INTERACTION_DISTANCE = 1.3f;
public const float ARRIVAL_THRESHOLD = 0.8f;
public const float MAX_INTERACTION_RANGE = 1.2f;

// AFTER (works with Valheim's m_useDistance of 2m)
public const float DEFAULT_INTERACTION_DISTANCE = 1.5f;
public const float ARRIVAL_THRESHOLD = 1.8f;
public const float MAX_INTERACTION_RANGE = 2.0f;
```

### How This Fixes the Teleport Loop

**Before:**
1. Target position = prefab center (e.g., inside the kiln model)
2. Companion walks toward it but can't reach (geometry blocks)
3. Distance never gets below ARRIVAL_THRESHOLD (was 0.8m)
4. Pathfinding times out ? teleport ? still can't reach ? loop

**After:**
1. Target position = Switch position (actual interactable point on the side)
2. Companion can walk directly to this reachable point
3. Distance reaches ARRIVAL_THRESHOLD (now 1.8m) successfully
4. Interaction proceeds normally

### Key Insight from Valheim

From `CraftingStation.cs`:
```csharp
public float m_useDistance = 2f;

public bool InUseDistance(Humanoid human)
{
    return Vector3.Distance(human.transform.position, transform.position) < m_useDistance;
}
```

Valheim uses **2 meters** as the standard interaction distance. Our previous ARRIVAL_THRESHOLD of 0.8m was way too tight - the companion would never consider itself "arrived" at the interaction point.

Now with ARRIVAL_THRESHOLD = 1.8m, the companion arrives at a comfortable interaction distance that matches what Valheim expects.

---

## Fix #7: Teleport Position Within Arrival Threshold (2025-03-01)

### Problem
Even after Fix #6, companions were still stuck in teleport loops because:
1. `FindSafeTeleportPosition()` was teleporting to `INTERACTION_DISTANCE + 0.5f` (~2.5m) away
2. `ARRIVAL_DISTANCE` threshold is 1.8m  
3. After teleport, companion is STILL outside arrival threshold
4. Loop continues forever

### Logs Showing the Issue
```
[SmelterOperator] Asny the Delver pathfinding failed (no progress for 8.0s) - TELEPORTING near smelter (distance: 2.5m)
[SmelterOperator] Asny the Delver pathfinding failed (no progress for 8.0s) - TELEPORTING near smelter (distance: 2.5m)
[SmelterOperator] Asny the Delver pathfinding failed (no progress for 8.0s) - TELEPORTING near smelter (distance: 2.5m)
```

The distance stays at 2.5m even after teleporting - proving the teleport wasn't helping!

### Root Cause
```csharp
// BEFORE (broken)
float safeDistance = INTERACTION_DISTANCE + 0.5f;  // ~2.5m
Vector3 testPos = stationPos + dir * safeDistance;  // Teleports to 2.5m from STATION center
```

But we're checking arrival against `_targetPosition` which is the **standing position** (already 1.5m from switch). So after teleport, companion is at a random 2.5m position that's not necessarily close to `_targetPosition`.

### Fix Applied

**File: `SmelterOperatorBehavior.Helpers.cs`**

Changed `FindSafeTeleportPosition()` to:
1. Teleport to 0.3m from the TARGET POSITION (standing point), not 2.5m from station
2. Parameter renamed from `stationPos` to `targetPos` to clarify it receives the standing position

```csharp
// BEFORE (broken - teleported 2.5m from station center)
float safeDistance = INTERACTION_DISTANCE + 0.5f;
Vector3 testPos = stationPos + dir * safeDistance;

// AFTER (fixed - teleports 0.3m from the standing position we need to reach)  
float safeDistance = 0.3f;
Vector3 testPos = targetPos + dir * safeDistance;
```

### Why 0.3m?
- Close enough to be WITHIN `ARRIVAL_DISTANCE` (1.8m) - guaranteed!
- Far enough to not clip into the companion's own collision
- Provides a small buffer for physics settling

### Fallback Also Fixed
```csharp
// BEFORE (broken - fallback was 3m in random direction!)
Vector3 fallbackDir = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)).normalized;
Vector3 fallbackPos = stationPos + fallbackDir * 3f;

// AFTER (fixed - fallback directly to target position)
Vector3 fallbackPos = targetPos;
```

### Flow After Fix
```
1. Companion at position A, target position is B
2. Distance A->B = 2.5m (can't reach due to pathfinding issues)
3. Stuck detection triggers after 8s of no progress
4. FindSafeTeleportPosition(B) called with _targetPosition
5. Teleport to 0.3m from B
6. Distance (new pos)->B = 0.3m < 1.8m (ARRIVAL_DISTANCE)  
7. SUCCESS! Companion has "arrived"
8. Operation continues normally (no more loop!)
```

---

## Updated Changelog

| Date | Fix | Description |
|------|-----|-------------|
| 2025-03-01 | #1 Chest Lid Animation | Use `SetInUse()` for proper visual/audio |
| 2025-03-01 | #2 Interaction Distances | Loosened to 1.5m/1.8m/2.0m for Valheim compatibility |
| 2025-03-01 | #3 Tool Retrieval | Physical walk to chest for tools |
| 2025-03-01 | #4 Circuit Breaker | Track `_lastPullCount` directly |
| 2025-03-01 | #5 Resource Fallback | Gather resources when chests empty |
| 2025-03-01 | #6 Interactable Point Detection | Find actual Switch positions, not prefab centers |
| 2025-03-01 | #7 Teleport Position | Teleport WITHIN arrival threshold to break loop |
