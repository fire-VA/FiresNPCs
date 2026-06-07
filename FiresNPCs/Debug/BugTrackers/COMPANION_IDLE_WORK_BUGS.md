# Companion Idle Work & Directed Work Bug Tracker

## !! IMPORTANT: ASSISTANT INSTRUCTIONS !!

**DO NOT make autonomous decisions about what's "best" when given specific instructions.**

When asked to fix something specific:
1. Fix ONLY what was asked
2. Do NOT change other unrelated values/settings
3. Do NOT "improve" code that wasn't mentioned
4. Ask for clarification if unsure, don't assume

**Example of WRONG behavior:**
- User asks: "Make companions stop more often before wandering"
- Assistant changes: Standing pause logic (correct) + wander chance (WRONG) + max wander legs (WRONG)
- Result: User has to waste time reverting unwanted changes

**The assistant should do EXACTLY what was asked and nothing more.**

---

## Overview
This document tracks bugs related to companions not properly executing idle behaviors (when "stayed") or directed commands (when following and commanded to interact with resources).

## Issue Summary
- **Date Identified:** 2024
- **Priority:** HIGH
- **Affected Systems:**
  - Idle Behaviors (smelter operation, kiln operation, resource gathering, fire tending)
  - Directed Commands (gather resource, interact with workstations)
  - CompanionStateController command flow
  - CompanionIdleBehavior sub-behavior management

---

## FIX #21: Companion Chat Phrases - Player Name Integration (2025-03-02)

### Enhancement
Companions now occasionally (20% chance) use their owner's name when responding to commands, making interactions feel more personal and connected.

### Implementation
**New File: `CompanionChatHelper.Phrases.cs`** (Partial class)

1. **Player Name Helper Methods**:
   - `GetOwnerName(companion)` - Gets owner's display name via `Player.GetPlayerName()`
   - `ShouldUseOwnerName()` - Returns true 20% of the time (configurable via `OwnerNameChance`)
   - `GetPersonalizedPhrase()` - Chooses between normal and personalized phrases

2. **Personalized Phrase Collections**:
   - `FollowingPersonal` - "Right behind you, {owner}.", "Lead the way, {owner}!", etc.
   - `StayingPersonal` - "I'll wait here for you, {owner}.", "Be careful out there, {owner}.", etc.
   - `MovingToPositionPersonal` - "On my way, {owner}.", "As you wish, {owner}.", etc.
   - `EnteringCombatPersonal` - "I've got your back, {owner}!", "Stand behind me, {owner}!", etc.
   - `LowHealthPersonal` - "I need help, {owner}!", "{owner}, I can't take much more!", etc.
   - `TaskCompletePersonal` - "All done, {owner}!", "Mission accomplished, {owner}!", etc.
   - `StartingTaskPersonal` - "On it, {owner}!", "Leave it to me, {owner}.", etc.
   - `GatheringPersonal` - "Found some good stuff, {owner}!", "Look what I found, {owner}!", etc.
   - `InventoryFullPersonal` - "I can't carry any more, {owner}.", "Bags are full, {owner}!", etc.

3. **Updated QuickMessages**:
   - `FollowingOwner()` - Now uses personalized phrases 20% of the time
   - `StayingHere()` - Now uses personalized phrases 20% of the time
   - `EnteringCombat()` - Now uses personalized phrases 20% of the time
   - `MovingToPosition()` - Now uses personalized phrases 20% of the time
   - `LowHealth()` - Now uses personalized phrases 20% of the time
   - `InventoryFull()` - Now uses personalized phrases 20% of the time
   - etc.

### Example Outputs
- Normal (80%): "Following you."
- Personal (20%): "Right behind you, TravisB!"

---

## FIX #20: Companion Chat Helper - Phrase System Refactor (2025-03-02)

### Problem
Chat messages sometimes contained nonsensical text like "just going to fetch the collect output" due to:
1. Technical terms leaking into display text ("output", "m_addWoodSwitch", etc.)
2. Status conversion logic not handling all edge cases
3. Redundant phrase definitions scattered throughout the code

### Fix Applied (2025-03-02)
**New File: `CompanionChatHelper.Phrases.cs`** (Partial class)

Created organized phrase collections in a separate file:
1. **Movement Phrases**: `GoingToFire`, `GoingToKiln`, `GoingToSmelter`, `GoingToCookingStation`, `GoingToWorkbench`, `GoingToChest`, `GoingToGatherWood`, `GoingToMineOre`, `GoingToGatherPlants`, `GoingToGenericResource`
2. **Collection Phrases**: `CollectingCharcoal`, `CollectingMetal`, `CollectingGeneric`, `CollectingLoot`, `CollectingDrops`
3. **Working Phrases**: `AddingFuel`, `AddingWoodToKiln`, `AddingOreToSmelter`, `GettingFromChests`, `StoringItems`, `Operating`, `TendingFire`, `Cooking`
4. **Gathering Phrases**: `Gathering`, `Picking`, `Mining`, `Chopping`
5. **Waiting Phrases**: `Waiting`, `Checking`, `Looking`
6. **Combat Phrases**: `EnteringCombat`, `Victory`, `LowHealth`
7. **Status Phrases**: `Following`, `Staying`, `MovingToPosition`, `InventoryFull`, `TaskComplete`, `CantDoTask`
8. **Material Phrases**: `NoFuelAvailable`, `NoOreAvailable`, `NoWoodForKiln`, `NoToolAvailable`, `StationFull`
9. **Training Phrases**: `BowTraining`, `MeleeTraining`
10. **Repair/Upgrade Phrases**: `ItemRepaired`, `ItemUpgraded`

### Helper Methods Added
- `GetPhrase(phrases, cycleCategory)` - Gets random phrase with optional cycling to avoid repeats
- `GetPhraseWithVariable(templates, variable, cycleCategory)` - Inserts variables into phrase templates

---

## FIX #19: Bow Training - Verify Bow Equipped Before Animations (2025-03-02)

### Problem
Companions would sometimes play bow drawing/shooting animations while holding a different weapon (like a pickaxe) because:
1. `EnsureBowEquipped()` would call weapon swap methods but not verify the swap completed
2. Animation phase would start immediately without checking if bow was actually in the left hand slot
3. Shields weren't being unequipped before bow equip, blocking the bow from going to left hand

### Fix Applied (2025-03-02)
**File: `BowTrainingBehavior.cs`**

1. **Added `VerifyBowInHand()` method**:
   - Checks if left hand slot actually contains a bow
   - Returns false if no bow equipped, preventing animations from starting

2. **Updated `Start()` method**:
   - After calling `EnsureBowEquipped()`, now calls `VerifyBowInHand()`
   - If bow not in hand, immediately calls `Complete()` to abort the behavior
   - Prevents bow animations from playing without a bow

3. **Updated `EnsureBowEquipped()` method**:
   - Now calls `FinalizeEquipmentChange()` even when bow is already equipped (ensures visuals match)
   - Falls back to `WeaponSwapManager.ForceSwapToRanged()` for storage inventory weapons

4. **Updated `UnequipRightHandWeapon()` method**:
   - **CRITICAL**: Now also unequips shields from left hand (shields block bow equip)
   - Only calls `FinalizeEquipmentChange()` if changes were actually made
   - Properly moves shields to LeftBack slot or storage

5. **Added `FinalizeEquipmentChange()` helper**:
   - Calls `RecalculateEquipmentBonusesPublic()`
   - Calls `ApplyVisualEquipment()` 
   - Calls `SaveToZDO()`
   - Ensures visual equipment matches inventory state after any swap

### Flow After Fix
```
Start()
  -> EnsureBowEquipped()
      -> UnequipRightHandWeapon() (clears melee AND shield)
      -> EquipBowFromSlot() OR WeaponSwapManager.ForceSwapToRanged()
      -> FinalizeEquipmentChange() (update visuals)
  -> VerifyBowInHand()
      -> If false: Complete() and return (abort behavior)
  -> Continue with bow animations (bow is guaranteed to be equipped)
```

---

## FIX #18: ChestDeposit MovingToChest Timeout Fix (2025-03-02)

### Problem
Companions were repeatedly timing out in the `MovingToChest` phase even when standing right next to chests:
```
[Warning: Unity Log] [ChestDeposit] Grid the Tremendous Phase MovingToChest timed out
```

### Root Cause
Two issues combined:

1. **Moving to chest CENTER instead of interaction point**: The behavior was calling `MoveToPosition(_targetChest.transform.position)` which targets the center of the chest. If the companion approached from behind or the side, they could be 2+ meters from the chest center even while physically next to the chest.

2. **Arrival only checked against move target**: `ContinueMovement()` in `WorkBehaviorBase` checks `DistanceTo(CurrentMoveTarget.Value) < ARRIVAL_THRESHOLD`. If the companion was close to the physical chest but far from the calculated move target, arrival was never detected.

### Fix Applied (2025-03-02)
**File: `ChestDepositBehaviorV2.cs`**

1. **Use proper interaction point for movement**:
```csharp
// Use InteractionPointHelper to get proper standing position in front of chest
Vector3 interactionPoint = InteractionPointHelper.GetContainerInteractionPoint(
    _targetChest, Transform.position, InteractionPointHelper.DEFAULT_INTERACTION_DISTANCE);
MoveToPosition(interactionPoint);
```

2. **Check distance to CHEST, not just move target**:
```csharp
// Check if we're close enough to the CHEST (not the move target) to interact
float distToChest = DistanceTo(_targetChest.transform.position);
if (distToChest < INTERACTION_RANGE)
{
    // Close enough to interact!
    StopMovement();
    FaceTarget(_targetChest.transform.position);
    SetPhase(DepositPhase.Depositing);
    return false;
}
```

### How It Works Now
1. `UpdateScanning` finds a chest and calculates the proper interaction point IN FRONT of the chest
2. `UpdateMovingToChest` checks BOTH:
   - Distance to the chest itself (if < 2.5m, we can interact)
   - Arrival at the move target (via `ContinueMovement()`)
3. Either condition triggers transition to `Depositing` phase
4. This handles cases where pathfinding takes a different route or companion is already near the chest

### Files Modified
- `ChestDepositBehaviorV2.cs`: 
  - `UpdateScanning()` - Now uses `InteractionPointHelper.GetContainerInteractionPoint()`
  - `UpdateMovingToChest()` - Now checks distance to chest, not just move target
  - `UpdateFindingNextChest()` - Now uses proper interaction point

---

## FIX #17: Stay Mode Work Search Radius Expanded to 50m (2025-03-02)

### Problem
Staying companions weren't finding work opportunities (smelters, fires, resources, archery targets) because the search radii were too small:
- Fire detection: 10m (x2 = 20m)
- Smelter detection: 15m (x2 = 30m)
- Workstation detection: 8-10m (x2 = 16-20m)
- Archery target: 25m

**Desired behavior**: Staying companions should search for work within **50 meters** of their home position, or within the entire territory bounds if in a territory.

### Fix Applied (2025-03-02)

#### 1. Added `StayModeWorkRadius` Setting (50m default)
**File: `CompanionSettings.cs`**

```csharp
public static float StayModeWorkRadius => 50f;

public static float GetStayModeWorkSearchRadius(Vector3 homePosition)
{
    float baseRadius = StayModeWorkRadius; // 50m
    
    // Check if position is in a territory
    var territory = Territories.TerritorySyncedState.GetCurrent(homePosition);
    if (territory != null)
    {
        // Use the larger of territory radius or default work radius
        return Mathf.Max(baseRadius, territory.Radius);
    }
    
    return baseRadius;
}
```

#### 2. Updated `IdleSubBehavior.GetEffectiveSearchRadius()`
**File: `IdleSubBehavior.cs`**

Changed from a simple 2x multiplier to use the new 50m setting:
```csharp
protected float GetEffectiveSearchRadius(float baseRadius)
{
    // For staying companions, use the stay mode work radius (50m default)
    if (IdleBehavior != null && IdleBehavior.HasHomePosition && Companion != null && !Companion.ShouldBeFollowing)
    {
        float stayModeRadius = CompanionSettings.GetStayModeWorkSearchRadius(SearchCenter);
        return Mathf.Max(stayModeRadius, baseRadius);
    }
    
    // Following companions use the base radius
    return baseRadius;
}
```

#### 3. Updated Individual Behaviors
- **SmelterOperatorBehavior.cs**: `FindNearbySmelterForAutonomousOperation()` uses `GetStayModeWorkSearchRadius()`
- **ResourceGatheringBehavior.cs**: `FindNeededResourceNearHome()`, `CheckIfNearbySmelterNeedsOre()`, `CheckIfNearbyKilnNeedsWood()` use stay mode radius
- **BowTrainingBehavior.cs**: `FindNearbyArcheryTarget()` uses `GetEffectiveSearchRadius()` and searches from `SearchCenter`

### Search Radius Summary After Fix

| Scenario | Search Radius |
|----------|---------------|
| Following companion | Uses behavior's default (10-25m) |
| Staying companion (no territory) | **50 meters** |
| Staying companion in territory | **max(50m, territory.Radius)** |

---

## FIX #16: Idle Behavior Variety Improvements (2025-03-02)

### Problem
Companions set to "stay" mode were:
1. Almost always interacting with workbenches
2. Rarely tending fires
3. Rarely practicing archery (bow training)
4. Rarely gathering resources or organizing chests

### Root Cause
The behavior selection system was based on `InventoryPriority` values, but most behaviors returned `0` (default). Since workbenches are extremely common in player bases, `WorkstationInteractionBehaviorV2.CanStart()` would return `true` much more often than other behaviors, causing it to dominate the selection.

### Fix Applied (2025-03-02)

#### 1. WorkstationInteractionBehaviorV2 - Lowered Priority (-20)
```csharp
public override int InventoryPriority => -20;
```
This gives workstation interaction a penalty so other behaviors are chosen more frequently.

#### 2. FireTendingBehaviorV2 - Elevated Priority (+10) & Earlier Trigger
```csharp
public override int InventoryPriority => 10;
```
AND changed the fuel threshold from 50% to 70%:
```csharp
return currentFuel < maxFuel * 0.7f; // Was 0.5f
```
Companions now tend fires more proactively.

#### 3. BowTrainingBehavior - Elevated Priority (+15)
```csharp
public override int InventoryPriority => 15;
```
Rangers will now actually practice archery when near an archery target.

#### 4. LootPickupBehaviorV2 - Elevated Priority (+5)
```csharp
public override int InventoryPriority 
{
    get
    {
        if (IsInventoryCompletelyFull()) return -50;
        return 5;
    }
}
```
Companions will prioritize picking up dropped items before they despawn.

### Priority System Overview (after changes)
| Behavior | Priority | Notes |
|----------|----------|-------|
| ChestDepositBehaviorV2 | +100 (when inventory needs deposit) | Highest - deposit first |
| BowTrainingBehavior | +15 | High - practice when possible |
| FireTendingBehaviorV2 | +10 | Elevated - keep fires burning |
| LootPickupBehaviorV2 | +5 (or -50 if inventory full) | Pick up loot |
| SmelterOperatorBehavior | 0 (or -10 if inventory full) | Dynamic |
| ResourceGatheringBehavior | 0 (or negative if full) | Dynamic |
| CraftingUpgradeBehaviorV2 | 0 | Normal |
| WorkstationInteractionBehaviorV2 | **-20** | Lowered to reduce dominance |

---

## FIX #14: Chest Interaction Now Uses Proper Open/Close (2025-03-01)

### Problem
Companions were interacting with chests without properly opening/closing them:
- No chest lid animation
- No "in use" state set (other players could open same chest)
- Items transferred instantly without visual feedback

### Fix Applied (2025-03-01)
**File: `ChestInteractionService.cs`**

Added proper chest open/close via ZDO manipulation:

1. **Opening Chest (`TryOpenChest()`):**
   - Gets ZNetView and ZDO from container
   - Sets `ZDOVars.s_inUse = 1` (marks chest as "in use")
   - This is the same mechanism Valheim uses when players open containers

2. **Closing Chest (`TryCloseChest()`):**
   - Clears `ZDOVars.s_inUse = 0` (releases the chest)
   - Called after item transfer OR when operation fails/cancels

3. **New Phase: `ClosingChest`**
   - Added between `Interacting` and `ReturningToOrigin`
   - Waits 0.5 seconds for close animation

### Complete Interaction Flow Now:
```
1. FindingChest      - Use ContainerRegistry to find chest with items
2. MovingToChest     - Pathfind using vanilla BaseAI.MoveTo()
3. OpeningChest      - Face chest, set ZDO "InUse" flag, play interact anim
4. Interacting       - Transfer items between inventories
5. ClosingChest      - Clear ZDO "InUse" flag, wait for close
6. ReturningToOrigin - Pathfind back to starting position
```

### Safety: Chest Always Closed
- `TryCloseChest()` is called in `Cancel()` and `FailOperation()`
- Ensures chest is never left "in use" if operation is interrupted

### Container Finding
Containers are found via `ContainerRegistry` which:
- Tracks all containers globally via Harmony patches
- Uses `Container.Awake` to register, `Container.OnDestroyed` to unregister
- Much faster than `Physics.OverlapSphere` every frame
- Position comes from `Container.transform.position`

### Pathfinding
Movement uses `CompanionAI.RequestPathfindingMovement()` which:
- Acquires authority via `UnifiedMovementAuthority`
- Uses vanilla `BaseAI.MoveTo()` for proper obstacle avoidance
- Handles NavMesh pathfinding automatically

---

## FIX #15: Proper Interaction Point Calculation (2025-03-01)

### Problem
Companions were pathfinding to object centers instead of proper interaction positions:
- Walking behind chests (where lid doesn't open)
- Standing inside smelters/kilns
- Taking long awkward paths when direct line goes through obstacles
- Standing too far away from interaction buttons

### Fix Applied (2025-03-01)
**New File: `InteractionPointHelper.cs`**

Created helper class to calculate proper standing positions:

1. **`GetContainerInteractionPoint()`** - Calculates position IN FRONT of chest:
   - Uses `chest.transform.forward` to find front side (where lid opens)
   - Calculates candidates: front, left, right sides
   - Picks best based on: distance from approach, path clearance, front preference
   - Gets ground height at calculated position

2. **`GetSmelterInteractionPoint()`** - Calculates position for smelter interaction:
   - Looks for child objects like "OrePoint", "AddOrePoint", "OutputPoint"
   - Falls back to front of smelter transform
   - Respects station layout

3. **`GetInteractionPoint()`** - Auto-detects object type:
   - Container ? uses container logic
   - Smelter ? uses smelter logic
   - Piece ? uses generic front-facing logic
   - Fallback ? offset toward companion

### Constants:
```csharp
DEFAULT_INTERACTION_DISTANCE = 1.5f  // How far to stand from object
ARRIVAL_THRESHOLD = 0.8f             // How close = "arrived"
```

### Changes to ChestInteractionService:
- Now calculates `_interactionPoint` using `InteractionPointHelper`
- Pathfinds to `_interactionPoint` instead of `_targetChestPosition`
- Uses `ARRIVAL_THRESHOLD` (0.8m) instead of old 2.5m distance

### Changes to SmelterOperatorBehavior:
- `MoveToPosition()` now uses `InteractionPointHelper.GetSmelterInteractionPoint()`
- Added `MoveToChest()` helper that uses `InteractionPointHelper.GetContainerInteractionPoint()`

### Expected Behavior After Fix:
- Companions stand IN FRONT of chests (where lid opens)
- Companions stand at proper interaction distance from smelters
- Shorter, more direct paths (avoid walking around to back)
- More natural-looking interaction sequences

---

## CRITICAL BUG #13: Companions Move Erratically, Never Stop (2025-03-01)

### Symptoms
- Companions constantly wander around without pausing
- Movement appears jittery and purposeless
- Companions never stand still to "think" or "rest"
- Feels like MonsterAI's constant movement behavior

### Root Cause (CORRECTED - 2025-03-01 Second Fix)
There were **TWO separate idle wandering systems** causing confusion and bugs:

1. **`CompanionIdleBehavior`** - Feature-rich system with multi-leg wandering, terrain preference, emotes, sub-behaviors, chair sitting, path preference, etc.
2. **`CompanionAI.UpdateIdleWander()`** - Basic redundant wandering that picked random points in a circle

The redundant `UpdateIdleWander()` was constantly running and bypassing all the sophisticated idle behavior logic.

### Final Fix Applied (2025-03-01) - CONSOLIDATED TO SINGLE SYSTEM

**Removed redundant `CompanionAI.UpdateIdleWander()` entirely.**

Now there is ONE source of idle behavior: `CompanionIdleBehavior`

**File 1: `CompanionAI.Idle.cs` - `UpdateIdleState()`**
- Removed call to `UpdateIdleWander(dt)`
- Now delegates to `idleBehavior.UpdateIdleBehavior()` instead
- Removed redundant fields: `_isInIdleStandingPause`, `_idleStandingPauseEndTime`, `IDLE_STANDING_PAUSE_MIN/MAX`
- Removed the entire `UpdateIdleWander()` method

**File 2: `CompanionIdleBehavior.cs` - `HandleStandingState()`**
- Already has proper standing pause logic (8-45 seconds)
- Already has `_isInStandingPause`, `_nextDecisionTime` tracking
- Already properly calls `TryStartIdleBehavior()` only after pause completes

### Architecture After Fix

```
BEFORE (Broken - Two Competing Systems):
CompanionAI.UpdateIdleState()
    ? UpdateIdleWander()           // REMOVED - was picking random targets constantly
    ? No coordination with CompanionIdleBehavior

CompanionIdleBehavior.Update()
    ? UpdateIdleBehavior()         // Feature-rich, had standing pauses
    ? But was being bypassed!

AFTER (Fixed - Single System):
CompanionAI.UpdateIdleState()
    ? idleBehavior.UpdateIdleBehavior()   // Delegate to SINGLE source of truth
    
CompanionIdleBehavior.UpdateIdleBehavior()
    ? HandleStandingState()        // Standing pauses (8-45 sec)
    ? HandleWanderingState()       // Multi-leg terrain-aware wandering
    ? HandleEmoteState()           // Emotes, sitting, etc.
    ? Sub-behaviors                // Smelting, mining, etc.
```

### Fields Removed from CompanionAI
- `_isInIdleStandingPause` - now in CompanionIdleBehavior
- `_idleStandingPauseEndTime` - now in CompanionIdleBehavior
- `IDLE_STANDING_PAUSE_MIN/MAX` - now in CompanionIdleBehavior as `_standingPauseMinDuration/MaxDuration`

### Fields Kept in CompanionAI (still needed)
- `_currentWanderTarget` - used by `SetIdleDestination()` from CompanionIdleBehavior
- `_hasWanderTarget` - tracking for idle destination
- `idleWanderRadius` - configuration (CompanionIdleBehavior reads this)
- `idleWanderInterval` - configuration (CompanionIdleBehavior reads this)

### Expected Behavior After Fix
- **CompanionIdleBehavior is the SINGLE source of all idle behavior**
- Standing pauses: 8-45 seconds standing still before making decisions
- Multi-leg wandering with terrain preference (prefers paved paths)
- Emotes, chair sitting, sub-behaviors all work correctly
- No more constant wandering or jittery movement

---

## CRITICAL BUG #12: CompanionAI Fighting with Sub-Behaviors for Movement (2025-03-01)

### Symptoms
- Logs show constant authority re-acquisition:
  ```
  [MovementAuthority] Asny the Delver SmelterOperator ACQUIRED PlayerCommand authority
  [MovementAuthority] Asny the Delver SmelterOperator ACQUIRED PlayerCommand authority
  ```
- Companion moves one direction but faces another
- Movement appears glitchy with constant interruptions
- Multiple behaviors appear to be cycling rapidly on idle companions

### Root Cause
Two issues combined:

1. **CompanionAI.UpdateAI() keeps running while sub-behavior is active**
   - Even when a sub-behavior has authority, CompanionAI was still trying to run its state machine
   - This caused Following/Idle states to try to acquire movement authority
   
2. **Redundant authority acquisition every frame**
   - `TryMoveToPosition()` in `IdleSubBehavior` called `RequestPathfindingMovement()` every frame
   - Which called `TryAcquireAuthority()` every frame
   - Even though the behavior already had authority, it was logging "ACQUIRED" messages

### Fix Applied (2025-03-01)

**File 1: `CompanionAI.cs` - Skip AI movement when sub-behavior active**
```csharp
// In UpdateAI(), after emote freeze check:
if (idleBehavior != null && idleBehavior.IsInSubBehavior)
{
    // Sub-behavior is handling all movement - CompanionAI should do nothing
    return true;
}
```

**File 2: `IdleSubBehavior.cs` - Check authority before acquiring**
```csharp
// In TryMoveToPosition():
if (MovementAuthority != null)
{
    bool hasAuthority = MovementAuthority.HasAuthority(BehaviorName);
    if (!hasAuthority)
    {
        // Only try to acquire if we don't already have it
        if (!MovementAuthority.TryAcquireAuthority(source, BehaviorName, 5f))
        {
            return false;
        }
    }
}
```

**File 3: `CompanionAI.cs` - RequestPathfindingMovement only acquires if needed**
```csharp
// In RequestPathfindingMovement():
bool hasAuthority = authority.HasAuthority(authorityOwner);
if (!hasAuthority)
{
    // Only acquire if we don't already have authority
    if (!authority.TryAcquireAuthority(...)) { return false; }
}
```

---

## CRITICAL BUG #11: Multiple Sub-Behaviors Starting Simultaneously (2025-03-01)

### Symptoms
- Logs show multiple sub-behaviors starting on the same companion:
  ```
  [CompanionIdleBehavior] Dagny Ironside STARTED sub-behavior SmelterOperator
  ...
  [CompanionIdleBehavior] Dagny Ironside started sub-behavior: WorkstationInteraction
  ```
- Movement authority constantly fighting between behaviors
- Companion appears glitchy with multiple systems trying to move them

### Root Cause
`TryStartSubBehavior()` in `CompanionIdleBehavior.SubBehaviors.cs` didn't check if `_activeSubBehavior` was already set before starting a new behavior. This allowed the idle behavior rotation to start additional behaviors even when one was already running.

### Fix Applied (2025-03-01)
**File: `CompanionIdleBehavior.SubBehaviors.cs`**

Added early return check at the start of `TryStartSubBehavior()`:
```csharp
private bool TryStartSubBehavior()
{
    // CRITICAL: Don't start a new behavior if one is already active!
    if (_activeSubBehavior != null)
    {
        if (VerboseLogging)
            Debug.Log($"[CompanionIdleBehavior] {_companion?.companionName} TryStartSubBehavior blocked - already running {_activeSubBehavior.BehaviorName}");
        return false;
    }
    
    // ... rest of method ...
}
```

---

## CRITICAL BUG #10: Container Registry Not Finding Chests (2025-03-01)

### Symptoms
- Companions say "I cannot find any ore or fuel" despite chests being nearby
- `ContainerRegistry.GetNearby()` returns 0 containers
- Smelter/kiln behavior `CanStart()` returns false because no materials found

### Root Cause Analysis

#### Issue A: Layer Check Too Restrictive
**Location:** `ContainerRegistry.cs`

The original code checked:
```csharp
bool isPlayerPiece = container.gameObject.layer == LayerMask.NameToLayer("piece");
```

But Valheim uses multiple layers for buildable items:
- `piece` - Standard building pieces
- `piece_nonsolid` - Non-solid pieces
- Some chests may be on `Default` or other layers from mods

#### Issue B: ZDO Creator/Owner Not Always Set
Player-placed chests don't always have `creator` or `owner` ZDO fields set immediately.

### Fix Applied (2025-03-01)
**File: `ContainerRegistry.cs`**

1. Added prefab name pattern matching as fallback:
```csharp
bool isChestPrefab = containerName.Contains("chest") ||
                    containerName.Contains("karve") ||
                    containerName.Contains("longship") ||
                    containerName.Contains("cart") ||
                    containerName.Contains("storage") ||
                    containerName.Contains("box");
```

2. Accept containers if ANY of these are true:
   - Has `creator` ZDO field
   - Has `owner` ZDO field  
   - Is on `piece` or `piece_nonsolid` layer
   - Matches known chest prefab patterns

3. Added comprehensive debug logging to track what's being registered/skipped

### Additional Debugging Added
**File: `SmelterOperatorBehavior.cs`**

Added extensive logging to `ScoreSmelterNeedsAttention()`:
- Logs chest counts found near smelter AND companion
- Logs exact items searched for (ore names, fuel names)
- Logs what items ARE in the chests if ore/fuel not found
- Logs final score with breakdown

---

## CRITICAL BUG #9: Authority Re-Acquisition Spam (2025-03-01)

### Symptoms
- Log spam: `[MovementAuthority] SmelterOperator ACQUIRED SubBehavior authority` repeated endlessly
- Companions appear to "glitch" with jittery movement
- Movement commands don't complete properly

### Root Cause
`SmelterOperatorBehavior.StopMovement()` was calling `base.StopMovement()` which releases authority. Every time the companion stopped to interact with the smelter (add ore/fuel), it would release authority, then have to re-acquire it when moving again.

### Fix Applied (2025-03-01)
**File: `SmelterOperatorBehavior.Helpers.cs`**

1. Rewrote `StopMovement()` to NOT release authority:
```csharp
private new void StopMovement()
{
    // DON'T call base.StopMovement() - it releases authority!
    // Just stop movement without releasing authority
    _combatMovement?.ClearMoveDestination();
    // Zero horizontal velocity
    if (_rigidbody != null) ...
    // Zero character movement
    if (_character != null) ...
}
```

2. Added `ReleaseMovementAuthorityFinal()` method that's only called when behavior completes or is cancelled

3. Updated `Cancel()` to explicitly call `ReleaseMovementAuthorityFinal()`

---

## Bug #1: Stayed Companions Not Triggering Idle Behaviors

### Symptoms
- Companions set to "stay" mode do not engage in autonomous idle behaviors
- Smelter/kiln operation not starting automatically
- Resource gathering not starting automatically
- Fire tending not starting automatically
- Companions just stand in place doing nothing

### Observed Logs
```
No logs indicating idle behavior attempts for stayed companions
Sub-behaviors don't appear to be initialized or checked
```

### Potential Root Causes

#### 1. Idle Behavior Rotation Not Running
**File:** `CompanionIdleBehavior.cs`
- `TryStartSubBehavior()` may not be called regularly
- `subBehaviorChance` or `subBehaviorCooldown` values may be too restrictive
- `AvailableForIdleRotation` returning false for behaviors

#### 2. State Controller Blocking Idle
**File:** `CompanionStateController.cs`
- `IsIdleBehaviorAllowed` only returns true for `Idle` or `Following` states
- Stayed companions may be in wrong state preventing idle behavior start

#### 3. Home Position / Wander Radius Issues
**File:** `CompanionIdleBehavior.cs`
- `HasHomePosition` may not be set when companion is told to stay
- `maxWanderRadius` may be too restrictive
- Sub-behaviors check distance from home and may refuse to start

#### 4. Combat State Not Properly Cleared
**File:** `CompanionAI.cs`
- `_lastCombatTime` check in `CompanionIdleBehavior.cs` may block idle behaviors
- `combatCooldownForIdle` may be too long

### Investigation Steps
1. [ ] Add logging to `CompanionIdleBehavior.UpdateIdleBehavior()` to see if it runs
2. [ ] Add logging to `TryStartSubBehavior()` to see if sub-behaviors are attempted
3. [ ] Check if `_activeSubBehavior` is ever set
4. [ ] Verify `CanStart()` returns true for idle behaviors when stayed
5. [ ] Check state of companion when stayed (should be `Idle` not `Following`)
6. [ ] **IMPORTANT:** Bug #2's fix may also affect idle behaviors since they use the same `SetMoveDestination()` path

---

## Bug #1.5: Stay Mode May Not Properly Set Home Position

### Potential Issue
When a companion is told to "stay", the system should:
1. Set `shouldFollow = false`
2. Set home position to current location
3. Enable idle behavior evaluation

Need to verify `CommandCompanionStay()` in `CompanionCommandSystem.cs` properly sets up all required state.

---

## Bug #2: Directed Resource Gathering Commands Not Executing

### Symptoms
- Player commands companion to gather a resource (tree, rock, etc.)
- Companion moves to resource but doesn't swing/attack
- Logs show command starts successfully but no actual gathering action occurs
- ResourceGatheringBehavior starts but no attack phase begins

### Observed Logs
```
[COMMAND] Grid the Tremendous StartCommand: type=Gather, pos=(609.45, 40.92, 312.53), timeout=120s, absolutePriority=False
[CompanionStateController] Grid the Tremendous PlayerCommand ABSOLUTE priority granted
[COMMAND] Grid the Tremendous TryEnterState(PlayerCommand): True, CurrentState=PlayerCommand
[COMMAND] Grid the Tremendous command started successfully. State=PlayerCommand, IsInFrozenState=False, IsAnimationBlocking=False
[CompanionIdleBehavior] Grid the Tremendous STARTED sub-behavior ResourceGathering - _activeSubBehavior is now SET, IsInSubBehavior=True
[CompanionCommandSystem] Issued GatherResource command to Grid the Tremendous at (609.45, 40.92, 312.53)
```
But NO logs showing:
- Attacking phase entry
- Tool equipping
- Damage application

### ROOT CAUSE IDENTIFIED ?

#### The Problem Chain:
1. `CompanionCommandSystem.IssueCommand()` starts a `Gather` command via StateController
2. `ExecuteGatherCommand()` starts `ResourceGatheringBehavior` via `TryStartSubBehavior()`
3. `ResourceGatheringBehavior.Start()` is called
4. Start calls `MoveToPosition()` to move to the resource
5. `MoveToPosition()` calls `_combatMovement.SetMoveDestination()`
6. **`CompanionCombatMovement.SetMoveDestination()` issues a NEW `Move` command with ABSOLUTE priority**
7. The `Move` command cancels the existing `Gather` command
8. The sub-behavior is orphaned - it still runs but the command system thinks it was cancelled

#### The Problematic Code Flow:

**File:** `CompanionCombatMovement.cs` (line ~2756-2764)
```csharp
public void SetMoveDestination(Vector3 destination, bool useWalk)
{
    // CRITICAL: Use the StateController's command system for absolute priority
    if (_stateController != null)
    {
        // Start a Move command - this has ABSOLUTE priority
        _stateController.StartCommand(
            CompanionStateController.CommandType.Move,  // <-- ISSUES NEW COMMAND
            destination,
            ...
        );
    }
}
```

**File:** `ResourceGatheringBehavior.cs` (line ~2167-2174)
```csharp
private void MoveToPosition(Vector3 position)
{
    if (_combatMovement != null)
    {
        _combatMovement.SetMoveDestination(position, useWalk: true);  // <-- TRIGGERS THE BUG
    }
}
```

**File:** `CompanionStateController.cs` (line ~1201)
```csharp
bool isAbsolutePriority = (type == CommandType.Move || type == CommandType.Attack);
// ALL Move commands automatically have absolute priority!
```

#### Why This Breaks Everything:
- Sub-behaviors use `SetMoveDestination()` for movement
- `SetMoveDestination()` issues a new StateController command
- New commands with absolute priority cancel existing commands
- This creates a conflict: the sub-behavior is running but its parent command was cancelled
- The command system thinks the gather failed, but the sub-behavior continues orphaned

#### 2. ResourceGatheringBehavior Phase Transition Issues
**File:** `ResourceGatheringBehavior.cs`
- `UpdateMovingToResource()` may complete but not transition to `UpdateAttacking()`
- Tool equipping in `EquipBestToolForResource()` may fail silently
- `AttackResource()` may not be called

#### 3. Animation State Blocking
**File:** `CompanionStateController.cs`
- `IsAnimationBlocking` may be returning true
- Attack animations may not be starting due to state controller blocking

#### 4. Weapon/Tool Not Found
**File:** `ResourceGatheringBehavior.cs`
- `HasToolForResource()` may return false
- Pickaxe/axe may not be found in inventory
- Tool may not be equipped properly

### Investigation Steps
1. [x] **FOUND:** Move command overriding Gather command - see section 1 above
2. [ ] Add logging to `ExecuteGatherCommand` to trace full flow
3. [ ] Verify `_targetResource` is set properly
4. [ ] Check `SetPhase(GatherPhase.Attacking)` is called
5. [ ] Add logging to `AttackResource()` method
6. [ ] Check if companion has required tool in inventory

---

## Bug #3: Command Cancellation Loop

### Symptoms
- Commands are issued and immediately cancelled in a loop
- Multiple command starts for the same action
- Companion never completes any commanded action

### Observed Logs
```
[COMMAND] Grid the Tremendous StartCommand: type=Gather...
[COMMAND] Grid the Tremendous StartCommand: type=Move... 
[COMMAND] Grid the Tremendous cancelling existing command: Gather
...
[COMMAND] CancelCommand for Grid the Tremendous
[COMMAND] ForceStopAllBehaviors for Grid the Tremendous
[COMMAND] Grid the Tremendous StartCommand: type=Gather...  (REPEAT)
```

### Root Cause Analysis
The log shows the same pattern repeating:
1. Gather command issued
2. Move command issued (cancels Gather)
3. CancelCommand called
4. ForceStopAllBehaviors called
5. New Gather command issued
6. REPEAT

This suggests either:
1. The command system is re-issuing commands on every frame
2. Something is triggering repeated command requests
3. The input processing is not properly debounced

### Potential Fix Locations
- `CompanionCommandSystem.ProcessCommandInput()` - may need debouncing
- `CompanionCommandSystem.ExecuteGatherCommand()` - should not use Move with absolute priority
- `CompanionStateController.StartCommand()` - may need guard against rapid re-issue

---

## Bug #4: Sub-Behavior Never Reaches Attack Phase

### Analysis of ResourceGatheringBehavior Flow
Looking at the phase flow in `ResourceGatheringBehavior.cs`:
```
FindingResource -> MovingToResource -> Attacking -> WaitingForDrops -> ...
```

The logs show:
```
[CompanionIdleBehavior] Grid the Tremendous STARTED sub-behavior ResourceGathering
```

But NO logs for:
- Phase transitions
- Attack attempts
- Damage application

### Potential Issues
1. `UpdateMovingToResource()` never returns true (destination never reached)
2. `_targetResource` is null or invalid
3. Phase enum stuck at `FindingResource` or `MovingToResource`
4. Movement destination calculated incorrectly

---

## Proposed Fixes

### Fix #1: Add `skipCommandOverride` Parameter to SetMoveDestination (RECOMMENDED)
**File:** `CompanionCombatMovement.cs`
**Method:** `SetMoveDestination`

Add an optional parameter to skip the StateController command override when called from sub-behaviors:

```csharp
public void SetMoveDestination(Vector3 destination, bool useWalk = false, bool skipCommandOverride = false)
{
    if (_stateController != null && !skipCommandOverride)
    {
        // Only issue new command if not being called from a sub-behavior
        _stateController.StartCommand(
            CompanionStateController.CommandType.Move,
            destination,
            ...
        );
    }
    
    // Always update the actual movement handler
    _commandHandler?.SetMoveDestination(destination, useWalk);
}
```

Then update all sub-behavior calls to use `skipCommandOverride: true`:
```csharp
// In ResourceGatheringBehavior.MoveToPosition():
_combatMovement.SetMoveDestination(position, useWalk: true, skipCommandOverride: true);
```

### Fix #2: Check for Existing SubBehavior Command Before Overriding
**File:** `CompanionCombatMovement.cs`
**Method:** `SetMoveDestination`

Don't issue a new Move command if a SubBehavior command is already active:

```csharp
public void SetMoveDestination(Vector3 destination, bool useWalk)
{
    if (_stateController != null)
    {
        // DON'T override if we're already in a sub-behavior command (Gather, Train, etc.)
        var currentCommand = _stateController.ActiveCommandType;
        bool isSubBehaviorCommand = currentCommand == CommandType.Gather ||
                                    currentCommand == CommandType.Train ||
                                    currentCommand == CommandType.SubBehavior ||
                                    currentCommand == CommandType.Sit;
        
        if (!isSubBehaviorCommand)
        {
            _stateController.StartCommand(CommandType.Move, destination, ...);
        }
    }
    
    // Always update actual movement
    _commandHandler?.SetMoveDestination(destination, useWalk);
}
```

### Fix #3: Sub-behaviors Use Direct Movement Instead of SetMoveDestination
**File:** Multiple sub-behavior files
**Impact:** Larger change but cleaner separation

Create a new method for sub-behavior movement that doesn't interact with the command system:

```csharp
// In CompanionCombatMovement:
public void SetSubBehaviorDestination(Vector3 destination, bool useWalk)
{
    // Direct movement without command system interaction
    _commandHandler?.SetMoveDestination(destination, useWalk);
}
```

Update all sub-behaviors to use the new method.

### Fix #4: Add Command Debouncing (addresses Bug #3)
**File:** `CompanionCommandSystem.cs`

Add debouncing to prevent rapid re-issue of commands:

```csharp
private Dictionary<CompanionController, float> _lastCommandTime = new();
private const float COMMAND_DEBOUNCE_INTERVAL = 0.5f;

private void IssueCommand(CompanionController companion, ...)
{
    // Check debounce
    if (_lastCommandTime.TryGetValue(companion, out float lastTime))
    {
        if (Time.time - lastTime < COMMAND_DEBOUNCE_INTERVAL)
        {
            Debug.Log($"[CompanionCommandSystem] Debouncing command for {companion.companionName}");
            return;
        }
    }
    _lastCommandTime[companion] = Time.time;
    
    // ... rest of method
}
```

---

## Testing Checklist

### Stay Mode Testing
- [ ] Tell companion to stay at location with nearby smelter
- [ ] Verify companion attempts smelter operation
- [ ] Tell companion to stay near trees/rocks
- [ ] Verify companion attempts resource gathering
- [ ] Check logs for sub-behavior initialization

### Directed Command Testing
- [ ] Command following companion to gather specific tree
- [ ] Verify companion moves to tree
- [ ] Verify companion equips axe
- [ ] Verify companion swings at tree
- [ ] Verify companion collects dropped items
- [ ] Check for command cancellation in logs

### State Flow Testing
- [ ] Track state changes through full command cycle
- [ ] Verify PlayerCommand state is maintained during execution
- [ ] Check for unexpected state exits

---

## Code Locations for Investigation

| Component | File | Key Methods |
|-----------|------|-------------|
| Command System | `CompanionCommandSystem.cs` | `ExecuteGatherCommand`, `ProcessCommandInput` |
| State Controller | `CompanionStateController.cs` | `StartCommand`, `UpdateCommandState` |
| Idle Behavior | `CompanionIdleBehavior.cs` | `UpdateIdleBehavior`, `TryStartSubBehavior` |
| Resource Gathering | `ResourceGatheringBehavior.cs` | `Update`, `UpdateMovingToResource`, `AttackResource` |
| Smelter Operation | `SmelterOperatorBehavior.cs` | `CanStart`, `Update`, `UpdateFillingStation` |
| Companion AI | `CompanionAI.cs` | `UpdateIdleState`, `SetState` |

---

## Related Configuration

### CompanionIdleBehavior Settings
```csharp
public float subBehaviorChance = 0.3f;  // 30% chance to start sub-behavior when idle
public float subBehaviorCooldown = 30f;  // Minimum 30s between attempts
```

### CompanionStateController Priority
```csharp
public enum MovementPriority
{
    None = 0,
    Idle = 10,
    SubBehavior = 20,  // Sub-behaviors lower than Following
    Following = 30,
    Combat = 40,
    Animation = 50,
    Forced = 90,
    Command = 100      // Player commands ALWAYS win
}
```

---

## Notes

The primary issue appears to be in `CompanionCommandSystem.ExecuteGatherCommand` where issuing a Move command with `absolutePriority=true` immediately cancels the Gather command that was just started. This creates a situation where:

1. Gather command starts ResourceGatheringBehavior
2. Move command cancels it before it can reach the attack phase
3. Companion reaches destination but has no active behavior to execute

The fix should either:
- Not issue a separate Move command (let ResourceGatheringBehavior handle its own movement)
- Issue the Move without absolute priority
- Use a different command flow that doesn't conflict

For idle behaviors, need to verify that companions in "stay" mode are actually triggering the idle behavior evaluation loop and that sub-behaviors are being considered.

---

## ? IMMEDIATE ACTION COMPLETED

### Priority: Fix Bug #2 First (SetMoveDestination Command Override) - **IMPLEMENTED**

This was blocking ALL directed work commands AND potentially causing issues with autonomous idle behaviors.

**Fix Applied:** Added `skipCommandOverride` parameter to `SetMoveDestination()`

See "FIX IMPLEMENTATION DETAILS" section below for full details.

---

## Changelog

| Date | Change |
|------|--------|
| 2024 | Initial bug tracker created |
| 2024 | Root cause identified: SetMoveDestination issues Move command that cancels sub-behavior commands |
| 2024 | **FIX IMPLEMENTED:** Added `skipCommandOverride` parameter to `SetMoveDestination()` |
| 2024 | **BUG FOUND:** `m_minToolTier` reads garbage value (1073741823) instead of actual tier |
| 2024 | **FIX IMPLEMENTED:** Added sanity checks to clamp `m_minToolTier` to valid range (0-100) |
| 2024 | **IMPROVEMENT:** Made quick emote duration configurable instead of hardcoded 3f (default 2.5s) |
| 2024 | **IMPROVEMENT:** Added EmoteDurations dictionary with accurate animation times from Valheim dumps |
| 2024 | **BUG FIX:** Fixed emote animation bools not being cleared in CancelAllIdleBehaviors() causing sliding |
| 2024 | **BUG FIX:** Fixed OnCombatStarted() not clearing active emotes before sub-behavior handling |
| 2024 | **BUG FIX:** Fixed weapon quality not persisting after workstation upgrades (quality lost in inventory screen) |
| 2024 | **BUG FIX:** Fixed tooltip showing wrong quality - was reading from prefab instead of actual equipped item |
| 2024 | **BUG FIX:** Fixed TryAddWoodToKiln() RPC order - was calling RPC before removing item, causing resource loss |
| 2024 | **BUG FIX:** Fixed animation states not being cleared after sitting/laying down - missing `sleeping` bool and comprehensive reset |
| 2024 | **REVERT:** Reverted opportunistic gathering from 100% back to 70% chance - user wanted RNG/luck factor preserved |
| 2024 | **FEATURE:** Added tool retrieval from nearby chests - companions now check chests for pickaxes/axes if needed for mining |
| 2024 | **BUG FIX:** Companions no longer deposit their only pickaxe or axe to chests - essential tools are protected |
| 2024 | **FEATURE:** `HasToolForResource` now checks nearby chests too - allows behavior to start even if tool is in chest |
| 2024 | **IMPORTANT:** DO NOT change TryStartSubBehavior random chance to 100% - randomness is INTENTIONAL for organic behavior |
| 2024 | **BUG FIX:** Animation-Movement Synchronization - companions no longer slide while in standing animation |

---

## Bug #6: Companions Not Gathering Ore from MineRocks (INVESTIGATING)

### Symptoms
- Companions with pickaxes do not autonomously gather ore from nearby MineRock deposits
- The `FindNeededResourceNearHome()` method should trigger gathering when smelter needs ore
- Despite having proper tools and nearby ore deposits, companions don't start the behavior

### Potential Root Causes
1. `FindNearbyOreDeposit()` may not be finding ore rocks correctly
2. Name filter too strict - only looks for specific strings like "ore", "copper", etc.
3. Tool tier validation may be failing (related to Bug #5 garbage tier values)
4. `CanStart()` returning false due to state checks

### Investigation Notes
- Need to add verbose logging to `FindNeededResourceNearHome()` 
- Check if `CheckIfNearbySmelterNeedsOre()` is detecting smelter need correctly
- Verify `FindNearbyOreDeposit()` is finding the MineRock components

---

## Bug #7: Smelter/Kiln Consuming Excessive Resources (PARTIAL FIX APPLIED)

### Symptoms
- Companion goes through "100 wood and produces maybe 10 ingots"
- Massive resource loss - should be 1:1 for kiln (wood -> coal)
- Either items are being consumed without being added, or being duplicated/lost

### Root Cause Found: RPC Order Bug in TryAddWoodToKiln

The `TryAddWoodToKiln()` method was calling the RPC **before** removing the item from inventory:

```csharp
// BUG - WRONG ORDER:
nview.InvokeRPC("RPC_AddOre", dropName);  // Add to kiln first
storageInv.RemoveOneItem(item);            // Then remove from inventory
```

This is backwards! If the item removal fails or is interrupted, the kiln still received the item
but inventory wasn't decremented. Compare to `TryAddOre()` which correctly does:

```csharp
// CORRECT ORDER:
storageInv.RemoveOneItem(item);            // Remove from inventory FIRST
nview.InvokeRPC("RPC_AddOre", dropName);  // Then add to station
```

### Fix Applied
Updated `TryAddWoodToKiln()` to match the correct order used in `TryAddOre()`:
1. Validate item exists and has stack > 0
2. Remove one item from inventory
3. Only if removal succeeds, call RPC to add to kiln
4. Save inventory immediately after

### Additional Issues to Investigate
The fix above addresses one source of resource loss, but there may be other issues:

#### A. Items Being Pulled Multiple Times
The `PullMaterialsFromChests()` method pulls items from chests to companion inventory.
If the behavior restarts or the phase is re-entered, items might be pulled again.

#### B. Chest Inventory Not Being Saved
`ChestHelper.PullItemsByPrefabName()` modifies chest inventory but doesn't call any save method.
Valheim chests should auto-sync but if not, changes might be lost and items re-pulled.

### Files Modified
- `SmelterOperatorBehavior.cs`: Fixed `TryAddWoodToKiln()` RPC order

---

## Bug Fix Details: Weapon Quality Not Persisting (Equipment Screen)

### Problem
When companions upgraded weapons at workstations, the upgrade message would show (e.g., "UPGRADED Root Fang from quality 1 to 2!") but when you opened the companion's inventory screen, the item still showed quality 1 in the tooltip.

### Root Cause Analysis (THREE separate bugs)

#### Bug A: Quality Lost When Dragging Items in Inventory Screen
**Location:** `CompanionInventoryScreen.cs`

The `_pendingEquipment` dictionary only stored prefab names, not quality levels. When `ApplyEquipmentToCompanion()` ran, it:
1. Got the prefab name from `_pendingEquipment`
2. Looked up the prefab in `ObjectDB`
3. Cloned the prefab's `ItemDrop.ItemData` - **which always has quality 1**
4. Equipped the cloned item with wrong quality

**Fix:** Added parallel dictionary `_pendingEquipmentQuality` to track quality alongside prefab names:
```csharp
private Dictionary<EquipmentSlot, string> _pendingEquipment = new();
private Dictionary<EquipmentSlot, int> _pendingEquipmentQuality = new();  // NEW
```

Updated all methods to store/use quality:
- `OnItemDropped()` - accepts and stores quality
- `ApplyEquipmentToCompanion()` - uses stored quality instead of prefab default
- `LoadSlotFromPrefabName()` - loads quality from companion inventory
- `ReturnItemToInventory()` - returns with correct quality
- `DropItemOnGround()` - drops with correct quality
- All slot drag handlers - preserve quality during drag operations

#### Bug B: Tooltip Reading from Prefab Instead of Equipped Item
**Location:** `CompanionInventoryScreen.cs`, `UpdateSlotTooltip()` method

The tooltip hover code was getting the wrong `ItemDrop.ItemData`:
```csharp
// BAD - gets prefab's ItemData which always has m_quality = 1
var prefab = ZNetScene.instance?.GetPrefab(slot.CurrentPrefab);
var itemDrop = prefab.GetComponent<ItemDrop>();
hoveredItem = itemDrop.m_itemData;  // Quality 1!
```

Even though we later got `hoveredItemQuality` from the companion's stored quality, we passed the **prefab's item** to `ShowTooltipPanel()`, and `item.GetTooltip()` uses `item.m_quality` internally.

**Fix:** Get the actual equipped item from companion's inventory:
```csharp
// GOOD - gets actual equipped item with correct quality
var compInv = _currentCompanion.GetInventory();
var compSlot = MapUISlotToCompanionSlot(slot.SlotType);
var equippedItem = compInv.GetEquippedItem(compSlot);  // Has real quality!
hoveredItem = equippedItem;
hoveredItemQuality = equippedItem.m_quality;
```

#### Bug C: (Already Working) UpdateItemQuality Method
The `CompanionInventory.UpdateItemQuality()` method was actually correct - it:
1. Updates `item.m_quality` on the actual `ItemDrop.ItemData` object
2. Updates `_equipXXXQuality` field via `SetEquipmentQuality()`
3. Saves to ZDO
4. Saves to Vault

The issue was that Bugs A and B prevented the correct quality from being displayed/used.

### Files Modified
- `CompanionInventoryScreen.cs`:
  - Added `_pendingEquipmentQuality` dictionary
  - Updated 9 locations to track/use quality
  - Fixed tooltip to read from actual equipped item

### Data Flow After Fix
1. **Workstation upgrades item** ? `UpdateItemQuality()` saves to ZDO + Vault ?
2. **Inventory screen opens** ? `LoadSlotFromPrefabName()` loads quality from `_equipXXXQuality` into `_pendingEquipmentQuality` ?
3. **Tooltip displays** ? Gets actual equipped `ItemDrop.ItemData` from companion which has correct `m_quality` ?
4. **Item dragged/dropped** ? Quality passed through all operations ?

---

## FIX IMPLEMENTATION DETAILS (2024)

### Summary
Added a new `skipCommandOverride` parameter to `CompanionCombatMovement.SetMoveDestination()` that prevents sub-behaviors from accidentally cancelling their parent commands when they need to move.

### Files Modified

1. **`CompanionCombatMovement.cs`** - Added new overload with `skipCommandOverride` parameter
   - `SetMoveDestination(Vector3 destination)` - calls 3-param version with `skipCommandOverride: false`
   - `SetMoveDestination(Vector3 destination, bool useWalk)` - calls 3-param version with `skipCommandOverride: false`
   - `SetMoveDestination(Vector3 destination, bool useWalk, bool skipCommandOverride)` - new method with full control

2. **Sub-behavior files updated to use `skipCommandOverride: true`:**
   - `ResourceGatheringBehavior.cs` (line ~2174)
   - `SmelterOperatorBehavior.cs` (line ~3437)
   - `FireTendingBehavior.cs` (line ~1000)
   - `ChestDepositBehavior.cs` (line ~1037)
   - `BowTrainingBehavior.cs` (line ~927)
   - `CraftingUpgradeBehavior.cs` (line ~563)
   - `LootPickupBehavior.cs` (line ~579)
   - `WorkstationInteractionBehavior.cs` (line ~965)

### How It Works

**Before (Bug):**
1. Player commands companion to gather resource
2. `CompanionCommandSystem.IssueCommand()` calls `StateController.StartCommand(Gather, ...)`
3. `ExecuteGatherCommand()` starts `ResourceGatheringBehavior`
4. `ResourceGatheringBehavior.Start()` calls `MoveToPosition()`
5. `MoveToPosition()` calls `_combatMovement.SetMoveDestination()`
6. `SetMoveDestination()` issues `StateController.StartCommand(Move, ...)` with absolute priority
7. **BUG:** Move command cancels the Gather command
8. Sub-behavior is orphaned - it still runs but parent command was cancelled

**After (Fixed):**
1. Player commands companion to gather resource
2. `CompanionCommandSystem.IssueCommand()` calls `StateController.StartCommand(Gather, ...)`
3. `ExecuteGatherCommand()` starts `ResourceGatheringBehavior`
4. `ResourceGatheringBehavior.Start()` calls `MoveToPosition()`
5. `MoveToPosition()` calls `_combatMovement.SetMoveDestination(..., skipCommandOverride: true)`
6. `SetMoveDestination()` skips StateController command, just updates movement handler
7. **FIXED:** Gather command stays active, sub-behavior executes correctly

### Backward Compatibility
- Existing code calling `SetMoveDestination(destination)` or `SetMoveDestination(destination, useWalk)` will continue to work as before (absolute priority)
- Only sub-behaviors that explicitly pass `skipCommandOverride: true` will use the new non-overriding behavior
- Player commands through `CompanionCommandSystem` continue to have absolute priority (they don't pass `skipCommandOverride: true`)

---

## Bug #5: Tool Tier Reading Garbage Values (1073741823)

### Symptoms
- Companions refuse to gather resources even with high-tier tools (blackmetal pickaxe/axe)
- Error message shows companion "needs pickaxe tier 1073741823" 
- Value 1073741823 is near `int.MaxValue/2`, clearly not a valid tool tier
- Iron deposits (should require tier 2) are showing tier requirement of 1073741823

### Observed Logs
```
[Warning: Unity Log] [ResourceDataHelper] Tool tier too low: $item_pickaxe_blackmetal has tier 5, needs 1073741823
[Info   : Unity Log] [ResourceGathering] Asny the Delver searching storage for Pickaxe tier>=1073741823. Storage has 5 items:
```

### Root Cause Analysis
The `MineRock.m_minToolTier` field is being read as a garbage value. Looking at the vanilla Valheim code:
```csharp
public class MineRock : MonoBehaviour, IDestructible, Hoverable
{
    public int m_minToolTier;  // This should be 0-5 for normal resources
    // ...
}
```

The value 1073741823 suggests:
1. Uninitialized memory being read
2. Object not fully loaded when we access it
3. Possible mod conflict corrupting the value
4. ZDO data not synced properly

### Fix Applied
Added sanity checks in `ResourceDataHelper.cs` to clamp invalid tier values:

```csharp
private static ResourceData CreateMineRockData(MineRock rock)
{
    // Sanity check: m_minToolTier should be 0-10 max
    int minTier = rock.m_minToolTier;
    if (minTier < 0 || minTier > 100)
    {
        Debug.LogWarning($"[ResourceDataHelper] MineRock {rock.name} has invalid m_minToolTier={minTier}, defaulting to 0");
        minTier = 0;
    }
    // ... rest of method using minTier
}
```

### Files Modified
- `ResourceDataHelper.cs`:
  - `CreateTreeBaseData()` - Added tier validation
  - `CreateTreeLogData()` - Added tier validation  
  - `CreateMineRockData()` - Added tier validation
  - `CreateMineRock5Data()` - Added tier validation

### Expected Behavior After Fix
- When `m_minToolTier` is > 100 or < 0, it will be clamped to 0
- A warning log will be printed to help identify which objects have corrupted data
- Companions will be able to gather resources with appropriate tools
- Blackmetal pickaxe (tier 5) will work on iron deposits (normally tier 2)

### Further Investigation Needed
- Why is `m_minToolTier` reading garbage values?
- Is this a mod conflict (Balrond's ore mods, custom terrain, etc.)?
- Is the object not fully initialized when we access it?
- Should we defer reading until object is confirmed valid?

---

## Improvement #1: Configurable Quick Emote Duration

### Summary
Changed the quick (one-shot) emote duration from a hardcoded `3f` seconds to a configurable field `quickEmoteDuration` with a default of 2.5 seconds.

### Reason for Change
- The hardcoded 3 second duration didn't match Valheim's actual emote animation lengths
- Most Valheim quick emotes (wave, point, cheer, etc.) are approximately 1.5-2.5 seconds
- Having a configurable value allows tuning based on actual gameplay feedback
- 2.5s default is based on observed animation lengths from DebugFXEnumerator dumps

### Files Modified
- `CompanionIdleBehavior.cs`:
  - Added new field: `public float quickEmoteDuration = 2.5f;`
  - Updated `StartIdleEmote()` to use `quickEmoteDuration` instead of hardcoded `3f`

### Configuration
The field can be adjusted in Unity inspector or via code:
```csharp
// In CompanionIdleBehavior
[Tooltip("Duration for quick one-shot emotes like wave, cheer, point (seconds)")]
public float quickEmoteDuration = 2.5f;
```

### Related Animation Durations (from debug dumps)
Common quick emotes and their approximate animation lengths:
- `emote_wave`: ~2.0s
- `emote_point`: ~1.5s  
- `emote_cheer`: ~2.5s
- `emote_bow`: ~2.0s
- `emote_flex`: ~2.5s
- `emote_thumbsup`: ~1.5s
- `emote_laugh`: ~2.0s

**Note:** Use `listanimations` console command to dump exact animation durations to file.

---

## Improvement #2: Accurate Emote Duration Dictionary

### Summary
Added a comprehensive `EmoteDurations` dictionary that maps emote names to their actual Valheim animation clip durations, ensuring emotes play for exactly the right amount of time.

### Problem Solved
Previously, quick emotes used a configurable but static duration (2.5s default) that didn't account for varying animation lengths. Some emotes would get cut off (bow at 3.5s) while others would leave the companion frozen after the animation ended (thumbsup at 1.25s).

### Solution
Created a lookup dictionary based on actual animation dump data:

```csharp
private static readonly Dictionary<string, float> EmoteDurations = new Dictionary<string, float>
{
    // Quick one-shot emotes (accurate to animation clip)
    { "emote_blowkiss", 2.0f },      // Blowkiss [Duration:2.00s, ONCE]
    { "emote_bow", 3.5f },           // Bow [Duration:3.50s, ONCE]
    { "emote_challenge", 3.2f },     // ChallengeAccepted [Duration:3.13s, ONCE]
    { "emote_cheer", 2.5f },         // Cheer [Duration:2.46s, ONCE]
    { "emote_cry", 4.0f },           // Cry [Duration:4.00s, ONCE]
    { "emote_flex", 2.9f },          // Flex [Duration:2.83s, ONCE]
    { "emote_laugh", 3.1f },         // Laugh 2 [Duration:3.03s, ONCE]
    { "emote_nonono", 2.1f },        // No no no [Duration:2.08s, ONCE]
    { "emote_point", 2.5f },         // Point [Duration:2.50s, ONCE]
    { "emote_roar", 2.2f },          // Roar [Duration:2.17s, ONCE]
    { "emote_shrug", 2.7f },         // Shrug [Duration:2.67s, ONCE]
    { "emote_thumbsup", 1.3f },      // Thumbsup [Duration:1.25s, ONCE]
    { "emote_wave", 2.5f },          // Wave [Duration:2.50s, ONCE]
    { "emote_toast", 2.7f },         // Toast [Duration:2.67s, ONCE]
    { "emote_loveyou", 2.7f },       // Love You [Duration:2.67s, LOOP]
    { "emote_comehere", 2.4f },      // GetOverHere [Duration:2.33s, ONCE]
    
    // Persistent/looping emotes - duration is one full animation cycle
    { "emote_sit", 10.87f },         // Sitting idle [Duration:10.87s, LOOP]
    { "emote_despair", 6.7f },       // Despair [Duration:6.67s, ONCE]
    { "emote_kneel", 2.0f },         // Kneel Loop [Duration:2.00s, LOOP]
    { "emote_headbang", 1.4f },      // Headbang [Duration:1.33s, LOOP]
    { "emote_dance", 5.2f },         // Lisa Dance [Duration:5.13s, LOOP]
    // ... etc
};
```

### How It Works

**For Quick Emotes:**
1. Look up the emote in `EmoteDurations`
2. Use the exact animation duration if found
3. Fall back to `quickEmoteDuration` config value if not found
4. Emote ends precisely when animation completes

**For Persistent Emotes:**
1. Look up base cycle duration from `EmoteDurations`
2. Use this as minimum duration (ensures at least one full cycle)
3. Randomize total duration between `persistentEmoteDurationMin` and `persistentEmoteDurationMax`
4. Emote continues looping until random duration expires

### Files Modified
- `CompanionIdleBehavior.cs`:
  - Added `EmoteDurations` dictionary
  - Updated `StartIdleEmote()` to use dictionary lookup

### Source Data
Animation durations were extracted using the `listanimations` console command which dumps all animation clips from the player animator. Key files:
- `BepInEx/config/FiresRPGmaker/animations_dump.txt`

### Animation Categories (from dump)

| Animation Type | Count | Typical Duration |
|----------------|-------|------------------|
| Emotes | 72 | 1.25s - 35.17s |
| Attacks/Combat | ~100 | 0.4s - 2.2s |
| Movement | ~200 | 0.7s - 1.5s (loops) |
| Items/Tools | ~50 | 0.5s - 4.0s |
| Reactions | ~30 | 0.17s - 4.0s |

### Notes
- Looping animations (Dance, Headbang, Sit, etc.) have their cycle duration listed
- One-shot animations have their full duration listed
- Small buffer (~0.1s) is added to account for transition time
- Unknown emotes fall back to configurable default duration

---

## Feature: Tool Retrieval from Nearby Chests

### Summary
When a companion needs to mine ore but doesn't have a pickaxe in their inventory, they will now automatically search nearby chests for a suitable pickaxe and retrieve it.

### Implementation Details

**File Modified:** `ResourceGatheringBehavior.cs`

#### New Method: `TryGetToolFromNearbyChests()`
```csharp
private ItemDrop.ItemData TryGetToolFromNearbyChests(ResourceDataHelper.ToolType requiredTool, int minTier, Inventory storage)
```

This method:
1. Finds nearby chests within `CompanionSettings.ChestSearchRadius`
2. Searches each chest for tools matching the required type (pickaxe or axe)
3. Finds the best tool (highest tier that meets minimum requirements)
4. Pulls the tool from the chest into companion's storage inventory
5. Returns the tool item so it can be equipped

#### Modified Method: `TryEquipToolFromStorage()`
The existing method now has a fallback:
1. First tries to find tool in companion's own storage (existing behavior)
2. If not found, calls `TryGetToolFromNearbyChests()` to check nearby chests
3. If tool found in chest, pulls it to storage and equips it

### How It Works
1. Companion is told to mine or decides to mine opportunistically
2. `TryEquipToolFromStorage()` is called to equip a pickaxe
3. Companion's storage is searched first
4. If no pickaxe found, nearby chests are searched
5. If chest has pickaxe, it's moved to companion's inventory
6. Pickaxe is equipped and mining proceeds

### Configuration
- Search radius controlled by `CompanionSettings.ChestSearchRadius`
- Uses existing `ChestHelper.FindNearbyChests()` for chest discovery

---

## Feature: Essential Tool Protection in Chest Deposits

### Summary
Companions will no longer deposit their only pickaxe or axe when depositing items to chests. This prevents companions from accidentally losing their essential gathering tools.

### Implementation Details

**File Modified:** `ChestDepositBehavior.cs`

#### New Method: `CountToolsOfType()`
```csharp
private int CountToolsOfType(string toolType)
```

This method counts all tools of a specific type (pickaxe or axe) in the companion's inventory:
- Checks all equipped slots (RightHand, LeftHand, RightBack, LeftBack)
- Checks storage inventory
- Returns total count

#### Modified Method: `ShouldDepositItem()`
Added protection logic before depositing:
```csharp
// CRITICAL: Never deposit the ONLY pickaxe or axe
string itemPrefab = item.m_dropPrefab?.name?.ToLowerInvariant() ?? "";
string itemSharedName = item.m_shared?.m_name?.ToLowerInvariant() ?? "";

bool isPickaxe = itemPrefab.Contains("pickaxe") || itemSharedName.Contains("pickaxe");
bool isAxe = (itemPrefab.Contains("axe") && !itemPrefab.Contains("pickaxe")) ||
             (itemSharedName.Contains("axe") && !itemSharedName.Contains("pickaxe"));

if (isPickaxe || isAxe)
{
    int toolCount = CountToolsOfType(isPickaxe ? "pickaxe" : "axe");
    if (toolCount <= 1)
    {
        // Don't deposit - this is our only one!
        return false;
    }
}
```

### How It Works
1. Before depositing any item, `ShouldDepositItem()` is called
2. If item is a pickaxe or axe, count how many the companion has
3. If this is the only one, refuse to deposit it
4. If companion has 2+ of the tool type, allow depositing extras

### Tool Type Detection
Uses substring matching on both prefab name and shared name:
- **Pickaxe:** Contains "pickaxe" (e.g., "PickaxeIron", "$item_pickaxe_bronze")
- **Axe:** Contains "axe" but NOT "pickaxe" (e.g., "AxeBlackMetal", "$item_axe_stone")

---

## Revert: Opportunistic Gathering Probability

### Summary
Reverted the opportunistic gathering chance from 100% back to 70%.

### Reason
User feedback indicated that always gathering (100%) removed the luck/RNG factor that made companions feel more organic and varied in their behavior.

### Change Details
**File:** `ResourceGatheringBehavior.cs`

**Before (reverted):**
```csharp
// Always try to gather when stayed - user explicitly wants them to work
{
    // Try to find trees first (wood is always useful)
```

**After (restored):**
```csharp
// Use a high chance (70%) to make companions actively gather when they have tools
if (Random.value < 0.7f) // 70% chance to opportunistically gather
{
    // Try to find trees first (wood is always useful)
```

### Impact
- Companions will still frequently gather resources when stayed (70% per check)
- There's now a 30% chance they'll skip gathering on any given check
- This preserves the randomness and luck factor in companion behavior
- Companions feel more natural, not robotic 100% efficiency machines

---

## ?? PROTECTED CODE: TryStartSubBehavior Random Chance

### DO NOT CHANGE THE RANDOM CHANCE TO 100%!

**Location:** `CompanionIdleBehavior.cs` ? `TryStartSubBehavior()` method

### Why This Random Chance MUST Be Preserved

The random chance in `TryStartSubBehavior()` is **INTENTIONAL** and serves an important purpose:

1. **Organic Behavior**: Makes companions feel lifelike, not robotic
2. **Variety**: Creates natural variation in companion activities
3. **Immersion**: Companions that work 100% of the time feel unrealistic
4. **Balance**: Prevents companions from being overpowered automation

### Current Values (DO NOT CHANGE)
```csharp
// Staying companions: 80% max chance (subBehaviorChance * 3, capped at 0.8)
// Following companions: Base subBehaviorChance (default ~30%)
float effectiveChance = isStaying ? Mathf.Min(subBehaviorChance * 3f, 0.8f) : subBehaviorChance;
```

### If Behaviors Aren't Working, Check These Instead:
1. **`CanStart()` methods** - Maybe the behavior is returning false
2. **Tool detection** - Check `HasToolForResource()` 
3. **Resource finding** - Check `FindNeededResourceNearHome()`
4. **Cooldowns** - Maybe `subBehaviorCooldown` is too long
5. **Home position** - Is `_hasHomePosition` set correctly?

### The Solution is NEVER to Set This to 100%
If you set it to 100%, companions will feel robotic and unnatural. The issue is always somewhere else in the code.

---

## Animation-Movement Synchronization Fix (Sliding Bug)

### Problem
Companions were "sliding" across the ground while in a standing animation. This occurred because:
1. Movement velocity was applied via `SetMoveDir()` 
2. But the animator hadn't transitioned to the walk/run animation yet
3. Result: Character visually stands still but slides across the ground

### Root Cause
The `DetectAnimationState()` method in `CompanionStateController` was using rigidbody velocity to determine animation state, not the actual animator state. This created a desync where movement could be applied while the character was visually idle.

### Fix Implementation

**Files Modified:**
- `CompanionStateController.cs`
- `CompanionCombatMovement.cs`
- `MovementModeController.cs`

**Key Changes:**

1. **New `IsAnimatorInLocomotion()` method** - Checks if the animator is actually playing a locomotion animation (walk/run), not just if we intend to move.

2. **New `IsInStandingSlideState` property** - Returns true when velocity is applied but animator hasn't caught up. Movement is blocked in this state.

3. **Updated `DetectAnimationState()`** - Now checks actual animator state instead of just velocity. If velocity exists but animator isn't in locomotion, returns `AnimationState.Standing` to block further movement.

4. **Updated `SetMoveDirSafe()`** - Blocks movement commands if `IsInStandingSlideState` is true. When starting from standstill, applies tiny initial velocity to trigger animation transition without visible sliding.

5. **Updated `StopMovementGradually()`** - Only switches to standing animation when BOTH velocity is near zero AND animator has exited locomotion state.

### How It Works

```
BEFORE (Bug):
1. AI decides to move
2. SetMoveDir(direction) called ? velocity applied
3. Character slides while animator blends to walk
4. Eventually walk animation plays (but sliding already happened)

AFTER (Fixed):
1. AI decides to move  
2. SetWalk(true) called ? signals intent to walk
3. IsInStandingSlideState blocks full velocity
4. Small initial velocity applied ? triggers animation blend
5. IsAnimatorInLocomotion() returns true ? full movement allowed
6. Character walks properly without sliding
```

### Key Principle
**Movement velocity should NEVER be applied faster than the animation can respond.**

The animator must be in a locomotion state before significant velocity is applied. This maintains visual consistency between what the character looks like they're doing and what physics is actually doing.

---

## Bug Fix #3: Emote Animation Bools Not Being Cleared (Sliding Bug)

### Problem
Companions would continue performing emote animations (sitting, dancing, kneeling, etc.) while walking/running, causing a "sliding" visual bug where they glide across the ground while stuck in the emote pose.

### Root Cause
Multiple methods were clearing `_currentEmote` and `_isPlayingEmote` flags **BEFORE** using the emote name to clear the actual animation bool:

```csharp
// BAD - emote name is cleared before we can use it to clear the bool!
_isPlayingEmote = false;
_currentEmote = "";  // Now we've lost the name
// ... later only clears "sitting" which doesn't cover all emotes
```

This happened in:
1. `CancelAllIdleBehaviors()` 
2. `ForceResetToStanding()`
3. `OnCombatStarted()` - different code path that didn't clear emotes at all

### Solution
1. **Save and clear the specific emote bool FIRST**, before clearing the string:

```csharp
// GOOD - clear the specific emote bool while we still know its name
if (_isPlayingEmote && !string.IsNullOrEmpty(_currentEmote))
{
    if (_zanim != null)
        _zanim.SetBool(_currentEmote, false);
    if (_animator != null && HasAnimatorParameter(_currentEmote))
        _animator.SetBool(_currentEmote, false);
}

// NOW safe to clear the tracking fields
_isPlayingEmote = false;
_currentEmote = "";
_isPersistentEmote = false;
```

2. **Call `ForceAnimationStateReset()`** which clears ALL known emote bools as a safety net

3. **Added emote clearing to `OnCombatStarted()`** which has a code path that skips `CancelAllIdleBehaviors()` when preserving sub-behaviors

### Files Modified
- `CompanionIdleBehavior.cs`:
  - Fixed `CancelAllIdleBehaviors()`
  - Fixed `ForceResetToStanding()`
  - Fixed `OnCombatStarted()`

### Related Animation Bools
The comprehensive list of animation bools that can cause sliding if left true:
```csharp
// Persistent emotes
"emote_sit", "emote_relax", "emote_rest", "emote_vibe",
"emote_kneel", "emote_despair", "emote_headbang", "emote_dance",

// Sitting/resting
"sitting", "resting", "sleeping",

// Attach points
"attach_chair", "attach_stool", "attach_bed", "attach_mast"
```
