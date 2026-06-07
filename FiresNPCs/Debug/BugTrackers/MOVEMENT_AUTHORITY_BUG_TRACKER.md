# Movement Authority Bug Tracker

## Overview
This document tracks bugs and fix attempts related to the `UnifiedMovementAuthority` system and companion movement.

## Current Status (2025-03-02)

### Movement System Architecture - COMPLETE
The movement system has been fully refactored to use vanilla pathfinding with authority coordination:

1. **Authority = Coordination Only** - Decides WHO moves, not HOW
2. **Vanilla `MoveTo()` = Pathfinding** - Uses NavMesh for obstacle avoidance
3. **Movement must be called EVERY FRAME** - Waypoint-based navigation

### All Behaviors Audited and Fixed

| Behavior | Status | Notes |
|----------|--------|-------|
| `SmelterOperatorBehavior` | ? | Uses `base.TryMoveToPosition()` |
| `ResourceGatheringBehavior` | ? | Uses `base.TryMoveToPosition()`, calls every frame |
| `BowTrainingBehavior` | ? | Uses `base.TryMoveToPosition()`, calls every frame |
| `FireTendingBehaviorV2` | ? | Uses `ContinueMovement()` |
| `ChestDepositBehaviorV2` | ? | Uses `ContinueMovement()` |
| `LootPickupBehaviorV2` | ? | Uses `ContinueMovement()` |
| `WorkstationInteractionBehaviorV2` | ? | Uses `ContinueMovement()` |
| `CraftingUpgradeBehaviorV2` | ? | Uses `ContinueMovement()` |

### Key Fix Pattern
For **WorkBehaviorBase** behaviors:
```csharp
// Use ContinueMovement() in moving phases
if (ContinueMovement())
{
    StopMovement();
    SetPhase(NextPhase);
}
```

For **IdleSubBehavior** behaviors (non-WorkBehaviorBase):
```csharp
// Call MoveToPosition() EVERY FRAME in update
if (dist >= POSITION_TOLERANCE)
{
    MoveToPosition(target);  // Must be called every frame!
}
else
{
    StopMovement();
    SetPhase(NextPhase);
}

// MoveToPosition uses base class
private void MoveToPosition(Vector3 position)
{
    base.TryMoveToPosition(position, walk: true, run: false);
}
```

---

## CRITICAL ARCHITECTURAL INSIGHT (Bug #8)

### Date Identified
2024 (Current) - Major realization after investigating pathfinding failures

### The Core Mistake We Made

**We replaced vanilla pathfinding with direct movement, which broke everything.**

Looking at `BaseAI.MoveTo()` in the decompiled code:
```csharp
protected bool MoveTo(float dt, Vector3 point, float dist, bool run)
{
    // ... flying check ...
    
    if (!this.FindPath(point))  // <-- Calculates path around obstacles!
    {
        this.StopMoving();
        return true;
    }
    if (this.m_path.Count == 0)
    {
        this.StopMoving();
        return true;
    }
    Vector3 vector3 = this.m_path[0];  // <-- Gets next waypoint from path!
    if (Utils.DistanceXZ(vector3, transform.position) < b)
    {
        this.m_path.RemoveAt(0);  // <-- Follows waypoint list!
        // ...
    }
    else
    {
        this.MoveTowards((vector3 - transform.position).normalized, run);  // <-- Moves to waypoint
    }
    return false;
}
```

**What vanilla pathfinding does:**
1. `FindPath(target)` - Uses `Pathfinding` system to calculate a path around obstacles
2. Populates `m_path` with a list of waypoints
3. Follows waypoints one by one, not direct line to target
4. Handles terrain, obstacles, NavMesh navigation

**What our "replacement" did:**
```csharp
private bool MoveToThroughAuthority(Vector3 destination, bool run, float reachDistance = 1.5f)
{
    Vector3 direction = destination - transform.position;  // <-- DIRECT LINE! No pathfinding!
    direction.y = 0;
    direction = direction.normalized;
    SetMoveDirThroughAuthority(direction, run);  // <-- Just moves straight toward target
}
```

**The Result:**
- Companions walk directly into walls/obstacles
- They get stuck on terrain
- The "stuck detection" and "teleporting" were band-aids for broken pathfinding
- Following worked because owner typically doesn't go behind walls

### What Authority SHOULD Do vs What We Made It Do

**WRONG APPROACH (What We Did):**
```
Authority System replaces vanilla pathfinding
?
Systems request authority to move
?
Authority calculates direction (badly - no pathfinding!)
?
Authority calls SetMoveDir
?
Harmony patch blocks OTHER SetMoveDir calls
?
Companions can't navigate around obstacles
```

**CORRECT APPROACH (What We Should Do):**
```
Authority System COORDINATES movement requests
?
Only one system at a time gets authority to move
?
That system uses VANILLA pathfinding (BaseAI.MoveTo, FindPath, etc.)
?
Vanilla pathfinding calls SetMoveDir with proper obstacle avoidance
?
Harmony patch ALLOWS all movement (no blocking needed!)
?
Companions navigate properly using Valheim's tested pathfinding
```

### The Fix Strategy

**1. Authority Purpose: COORDINATION ONLY**
- Authority decides WHICH system gets to move the companion
- It does NOT handle the actual pathfinding
- Just prevents multiple systems from fighting over movement

**2. Let Vanilla Handle Pathfinding**
- Use `BaseAI.MoveTo()` - it has working pathfinding
- Use `BaseAI.FindPath()` - it uses Valheim's NavMesh
- Use `BaseAI.m_path` - the waypoint list calculated by pathfinding

**3. Remove SetMoveDir Blocking**
- The Harmony patch should only block for TRUE frozen states (UI, emote, sitting)
- It should NOT block vanilla pathfinding calls
- Authority coordination happens at a HIGHER level (which system calls MoveTo)

**4. Systems Request Authority Before Calling Vanilla MoveTo**
```csharp
// Example: CompanionAI wants to follow owner
if (authority.TryAcquireAuthority(MovementSource.Following, "CompanionAI"))
{
    // Got authority - can use vanilla pathfinding
    MoveTo(dt, ownerPosition, 0f, run);  // <-- Vanilla pathfinding!
}
// If authority denied, another system has priority - don't move
```

### What Needs To Change

| Component | Current (Broken) | Should Be |
|-----------|-----------------|-----------|
| `MoveToThroughAuthority` | Direct line movement | Use `base.MoveTo()` |
| `SetMoveDirThroughAuthority` | Replace vanilla | Just set walk/run flags |
| Harmony `SetMoveDir` patch | Blocks non-authority | Only block TRUE frozen |
| `MoveTowardThroughAuthority` | Direct movement | Use vanilla pathfinding |
| Authority role | Pathfinding + coordination | Coordination ONLY |

### Required Refactor Steps

1. **Remove movement blocking from Harmony patch**
   - Only block for `IsInFrozenState` (UI, emote, chair)
   - Let vanilla pathfinding work unimpeded

2. **Change authority to coordination-only**
   - `TryAcquireAuthority()` - returns bool if system can move
   - `ReleaseAuthority()` - releases when done
   - Remove `SetMoveDirection()` - not needed

3. **Update all movement callers**
   - Check authority BEFORE calling vanilla MoveTo
   - Use vanilla `MoveTo()` / `MoveTowards()` / `FindPath()`
   - Let vanilla handle pathfinding

4. **Remove broken replacement methods**
   - `MoveToThroughAuthority()` - delete or refactor
   - `SetMoveDirThroughAuthority()` - delete
   - `MoveTowardThroughAuthority()` - delete

### Status
**REFACTORED** - Now using vanilla pathfinding with authority coordination

### Changes Made

**File: `CompanionAI.Idle.cs`**
1. Replaced `MoveTowardThroughAuthority()` (direct line movement) with `MoveToWithAuthority()` (vanilla pathfinding)
2. Following now uses `MoveTo()` for proper obstacle avoidance
3. Idle wandering now uses `MoveTo()` for proper pathfinding
4. Removed `IdleMovementThroughAuthority()` - just use `StopMoving()`
5. Simplified `StopMovementThroughAuthority()` to release authority and use vanilla stop

**File: `CompanionAI.Combat.cs`**
1. Replaced `MoveTowardThroughAuthority()` calls with `MoveToWithAuthority()`
2. Combat movement now uses proper pathfinding for chasing targets

**File: `CompanionAI.Pathfinding.cs`**
1. `MoveToThroughAuthority()` - Now just acquires authority then calls vanilla `MoveTo()`
2. `SetMoveDirThroughAuthority()` - Kept for edge cases (sidestep, direct escape)
3. Removed attempt to manually access `m_path` - let vanilla handle it

**File: `CompanionPatches.cs`**
1. Simplified Harmony patch - only blocks for TRUE frozen states
2. Removed complex authority blocking logic
3. Let vanilla pathfinding work unimpeded

### Key Architecture Change

```
BEFORE (Broken):
Authority replaces pathfinding ? Direct line movement ? Gets stuck on obstacles

AFTER (Fixed):
Authority = coordination only ? Check if we can move
Vanilla MoveTo() = pathfinding ? FindPath() + m_path waypoints ? Obstacle avoidance
```

### Bug #8 Complete Refactor Summary

**Files Modified:**

| File | Changes |
|------|---------|
| `CompanionAI.cs` | Added `RequestPathfindingMovement()` and `ReleasePathfindingMovement()` public API |
| `CompanionAI.Idle.cs` | Replaced direct movement with `MoveToWithAuthority()` using vanilla `MoveTo()` |
| `CompanionAI.Combat.cs` | Updated to use `MoveToWithAuthority()` for combat chasing |
| `CompanionAI.Pathfinding.cs` | `MoveToThroughAuthority()` now wraps vanilla `MoveTo()` |
| `IdleSubBehavior.cs` | `TryMoveToPosition()` now uses `CompanionAI.RequestPathfindingMovement()` |
| `CompanionPatches.cs` | Simplified - only blocks TRUE frozen states, allows vanilla pathfinding |
| `ChestInteractionService.cs` | **NEW** - Physical chest interaction with proper pathfinding |

**Movement Architecture Now:**

```
???????????????????????????????????????????????????????????????????
?                    MOVEMENT REQUEST                              ?
?                                                                  ?
?   IdleSubBehavior.TryMoveToPosition(target)                     ?
?           ?                                                      ?
?   CompanionAI.RequestPathfindingMovement(target, source, owner) ?
?           ?                                                      ?
?   Authority.TryAcquireAuthority(source, owner) ? Coordination   ?
?           ?                                                      ?
?   BaseAI.MoveTo(target) ? VANILLA PATHFINDING                   ?
?           ?                                                      ?
?   FindPath() ? m_path waypoints ? Obstacle avoidance            ?
?           ?                                                      ?
?   Character.SetMoveDir() ? Actual movement                      ?
???????????????????????????????????????????????????????????????????

Combat movement (strafing, dodging, retreating):
???????????????????????????????????????????????????????????????????
?   CompanionCombatMovement.SetMoveDirSafe(direction)             ?
?           ?                                                      ?
?   Authority.TryAcquireAuthority(Combat, owner) ? Coordination   ?
?           ?                                                      ?
?   Authority.SetMoveDirection() ? Direct movement (no pathfinding)?
?           ?                                                      ?
?   Character.SetMoveDir() ? Immediate response for combat        ?
???????????????????????????????????????????????????????????????????

Chest Interaction (NEW):
???????????????????????????????????????????????????????????????????
?   ChestInteractionService.StartPullOperation("Coal", 10)        ?
?           ?                                                      ?
?   Find chest with item ? ResourceAccessService                  ?
?           ?                                                      ?
?   Phase: MovingToChest                                          ?
?   CompanionAI.RequestPathfindingMovement(chestPos)              ?
?           ?                                                      ?
?   Phase: Interacting                                            ?
?   ResourceAccessService.PullItemFromChests()                    ?
?           ?                                                      ?
?   Phase: ReturningToOrigin                                      ?
?   CompanionAI.RequestPathfindingMovement(originalPos)           ?
?           ?                                                      ?
?   Phase: Complete ? LastResult contains success info            ?
???????????????????????????????????????????????????????????????????
```

**Why This Works:**
1. **Idle behaviors** use vanilla pathfinding via `CompanionAI.RequestPathfindingMovement()`
   - Navigates around obstacles properly
   - Uses Valheim's tested NavMesh system
   - No more getting stuck on terrain

2. **Combat movement** uses direct `SetMoveDir()` via authority
   - Immediate response for strafing/dodging
   - No pathfinding delay when milliseconds matter
   - Appropriate for close-range combat positioning

3. **Authority system** only does COORDINATION
   - Prevents multiple systems fighting over movement
   - Doesn't replace pathfinding - just decides WHO gets to call it

4. **ChestInteractionService** handles physical chest trips
   - Uses vanilla pathfinding to walk to chests
   - Handles open/interact/close animations
   - Returns to origin after operation

---

## Integration Status

### ? COMPLETE - Core Architecture

| Component | Status | Notes |
|-----------|--------|-------|
| `UnifiedMovementAuthority` | ? Complete | Coordination only, silent extensions (Bug #9) |
| `CompanionAI.RequestPathfindingMovement()` | ? Complete | Public API for behaviors |
| `IdleSubBehavior.TryMoveToPosition()` | ? Complete | Uses vanilla pathfinding |
| `ChestInteractionService` | ? Complete | Physical chest interaction |
| Harmony Patch | ? Complete | Only blocks frozen states |

### ? COMPLETE - Behavior Integration (Bug #9)

| Behavior | Integration Status | Notes |
|----------|-------------------|-------|
| `SmelterOperatorBehavior` | ? Fixed 2025-03-01 | Now uses `base.TryMoveToPosition()` |
| `ResourceGatheringBehavior` | ? Fixed 2025-03-02 | Now uses `base.TryMoveToPosition()` |
| `BowTrainingBehavior` | ? Fixed 2025-03-02 | Now calls `MoveToPosition()` every frame + uses base class |
| `FireTendingBehaviorV2` | ? Uses `ContinueMovement()` | Proper every-frame movement |
| `ChestDepositBehaviorV2` | ? Uses `ContinueMovement()` | Proper every-frame movement |
| `LootPickupBehaviorV2` | ? Uses `ContinueMovement()` | Proper every-frame movement |
| `WorkstationInteractionBehaviorV2` | ? Uses `ContinueMovement()` | Proper every-frame movement |
| `CraftingUpgradeBehaviorV2` | ? Fixed 2025-03-02 | Now uses `ContinueMovement()` |

### ??? V1 Behaviors Marked for Removal

After V2 testing confirms working (TODO markers added 2025-03-01):

| File | Replacement | Status |
|------|-------------|--------|
| `ChestDepositBehavior.cs` | `ChestDepositBehaviorV2` | ?? TODO marker added |
| `LootPickupBehavior.cs` | `LootPickupBehaviorV2` | ?? TODO marker added |
| `FireTendingBehavior.cs` | `FireTendingBehaviorV2` | ?? TODO marker added |
| `CraftingUpgradeBehavior.cs` | `CraftingUpgradeBehaviorV2` | ?? TODO marker added |
| `WorkstationInteractionBehavior.cs` | `WorkstationInteractionBehaviorV2` | ?? TODO marker added |

See: `Debug/VersionMds/CHEST_INTERACTION_INTEGRATION.md` for integration plan.

---

## Bug #1: Companions in "Stay" Mode Never Move (Idle Wander Not Working)

### Date Identified
2024 (Current)

### Symptoms
- Companions set to "stay" mode (not following) never move during idle wander
- Logs show movement commands being ACCEPTED by `UnifiedMovementAuthority`:
  ```
  [MovementAuthority] Dagny Ironside CompanionAI ACQUIRED IdleWander authority
  [MovementAuthority] Dagny Ironside CompanionAI SetMoveDirection ACCEPTED: dir=(0.58, 0.00, 0.82), walk=True, run=False
  ```
- But companions remain stationary - no actual movement occurs
- Following mode works correctly

### Root Cause Analysis

#### Investigation Path
1. **Initial Observation**: Logs show `SetMoveDirection ACCEPTED` but no `APPLYING` logs
2. **Traced code flow**:
   - `CompanionAI.UpdateIdleWander()` ? `MoveTowardThroughAuthority()` ? `SetMoveDirThroughAuthority()`
   - `UnifiedMovementAuthority.SetMoveDirection()` stores `_targetMoveDirection`
   - `UnifiedMovementAuthority.Update()` ? `ApplyMovement()` ? `ApplyMoveDirectionInternal()`
   - `ApplyMoveDirectionInternal()` calls `_character.SetMoveDir(direction)`
   - Harmony patch `Character_SetMoveDir_Prefix` intercepts this call

3. **Found the bug**: The Harmony patch was checking `CompanionStateController.ShouldFreeze()` BEFORE checking if the call came from the authority.

#### The Problematic Code Flow
```
1. Authority calls _character.SetMoveDir(direction)
2. Harmony patch Character_SetMoveDir_Prefix runs
3. Patch checks stateController.ShouldFreeze()
4. ShouldFreeze() returns TRUE because:
   - IsAnimationBlocking includes AnimationState.Standing
   - DetectAnimationState() returns Standing when:
     - Animator not in locomotion state
     - But has velocity > 0.3
5. Movement blocked! ? Companion never moves
6. Chicken-and-egg: Can't get into locomotion without movement
```

#### The Core Issue
`CompanionStateController.ShouldFreeze()` was designed to prevent sliding by blocking movement when the animator isn't in locomotion. But this created a deadlock:
- Need movement to trigger locomotion animation
- But movement blocked because not in locomotion yet

### Fix Attempt #1 - 2024 (CURRENT)

#### Changes Made
**File: `CompanionPatches.cs`**

1. **`Character_SetMoveDir_Prefix`** - Reordered checks:
   - Check `UnifiedMovementAuthority.LastAppliedFrame` FIRST
   - If call is from authority (same frame), ALWAYS allow it through
   - Only check freeze states for NON-authority calls
   - Changed from `ShouldFreeze()` to `IsInFrozenState` (true frozen states only)

2. **`Character_UpdateMotion_Prefix`** - Changed freeze check:
   - Was: `stateController.ShouldFreeze()` (includes animation blocking)
   - Now: `stateController.IsInFrozenState` (only UI/Emote/Chair)

3. **`Character_SyncVelocity_Prefix`** - Same change:
   - Was: `stateController.ShouldFreeze()`
   - Now: `stateController.IsInFrozenState`

#### Rationale
- `UnifiedMovementAuthority` is the SINGLE SOURCE OF TRUTH for movement
- Authority manages its own smooth transitions between standing/locomotion
- Harmony patches should not second-guess the authority
- Only block for TRUE frozen states (UI interaction, emotes, sitting)
- Animation blocking (standing state) should be handled internally by authority

#### Code Diff Summary
```csharp
// BEFORE - Character_SetMoveDir_Prefix
var stateController = __instance.GetComponent<CompanionStateController>();
if (stateController != null && stateController.ShouldFreeze())
{
    return false; // Frozen state blocks all movement
}
// ... then check authority frame

// AFTER - Character_SetMoveDir_Prefix  
var movementAuthority = companion.GetMovementAuthority();
if (movementAuthority != null)
{
    // Check if this call came from the authority FIRST
    if (Time.frameCount == movementAuthority.LastAppliedFrame)
    {
        return true; // Authority's call - ALWAYS allow
    }
    // Only check freeze for non-authority calls
    if (movementAuthority.IsMovementFrozen)
    {
        return false;
    }
}
// Check TRUE frozen states only (not animation blocking)
if (stateController != null && stateController.IsInFrozenState)
{
    return false;
}
```

#### Status
**PARTIALLY WORKING** - Movement now applies, but revealed Bug #2

#### Testing Results
- [x] Movement commands now reach the character (APPLYING logs visible)
- [x] Authority calls are allowed through Harmony patch
- [ ] Stay mode companions wander - **BLOCKED by Bug #2**
- [ ] Following mode - **Works initially, then blocked by Bug #2**

---

## Bug #2: Authority Frame Gets "Stuck" After PlayerCommand

### Date Identified
2024 (Current) - Discovered while testing Bug #1 fix

### Symptoms
- After a `PlayerCommand` (Move to position) completes or fails
- External `SetMoveDir` calls are blocked even though authority should be released
- Logs show huge frame gap:
  ```
  [CompanionPatches] BLOCKED external SetMoveDir for Grid the Tremendous: dir=(-0.31, 0.18, -0.95), authorityFrame=18019, currentFrame=18540
  ```
- authorityFrame=18019, currentFrame=18540 (521 frame difference!)
- This means `_lastAppliedFrame` isn't being updated after command ends

### Observed Log Sequence
```
1. Command starts: PlayerCommand ABSOLUTE priority granted
2. Command executes: APPLYING dir=..., frame=18000
3. Command fails: "Path blocked"
4. Later frames: BLOCKED external SetMoveDir, authorityFrame=18019, currentFrame=18540
```

### Root Cause Analysis

#### Hypothesis 1: Authority Not Released After Command
When `PlayerCommand` fails with "Path blocked", the authority may not be properly released:
- `CompanionStateController.FailCommand()` should call `ReleaseMovementPriority()`
- But does it also release `UnifiedMovementAuthority`?
- The `CompanionAI` or `CompanionCombatMovement` may be trying to acquire authority at `IdleWander` level
- But the Harmony patch still has a stale `authorityFrame` check

#### Hypothesis 2: _lastAppliedFrame Only Set During Movement
Looking at `UnifiedMovementAuthority.ApplyMoveDirectionInternal()`:
```csharp
_lastAppliedFrame = Time.frameCount;
_character.SetMoveDir(direction);
```
This is only set when APPLYING movement. If authority is released but `ApplyMovement()` isn't called (because `_targetMoveDirection` is zero), the frame never updates.

The Harmony patch then sees:
- `Time.frameCount` = 18540 (current)
- `movementAuthority.LastAppliedFrame` = 18019 (stale from last movement)
- These don't match, so call is BLOCKED

#### The Core Problem
The Harmony patch checks `Time.frameCount == movementAuthority.LastAppliedFrame` to identify authority calls. But:
1. After command ends, no more APPLYING happens
2. `_lastAppliedFrame` stays at old value
3. New systems try to acquire authority
4. Their `SetMoveDir` calls are blocked because frame doesn't match

### Proposed Fix

**Option A**: Update frame tracking to include "authority is actively applying"
- Add a flag like `IsActivelyApplying` that's true only during the exact frame of application
- Check this flag instead of frame comparison

**Option B**: Clear/reset frame when authority is released
- When authority is released or changes, set `_lastAppliedFrame = -1`
- Harmony patch treats -1 as "not from authority"

**Option C**: Use a different identification mechanism
- Instead of frame comparison, set a flag BEFORE calling SetMoveDir and clear AFTER
- Or use a unique token/counter that's checked

### Fix Attempt #1 - 2024 (CURRENT)

#### Changes Made
**File: `UnifiedMovementAuthority.cs`**
1. Added `_isCurrentlyApplying` private field (bool)
2. Added `IsCurrentlyApplying` public property to expose the flag
3. In `ApplyMoveDirectionInternal()`:
   - Set `_isCurrentlyApplying = true` BEFORE calling `_character.SetMoveDir()`
   - Clear `_isCurrentlyApplying = false` in `finally` block AFTER the call

**File: `CompanionPatches.cs`**
1. Changed authority identification from frame comparison to flag check:
   - Was: `Time.frameCount == movementAuthority.LastAppliedFrame`
   - Now: `movementAuthority.IsCurrentlyApplying`
2. Removed `authorityFrame` from blocked log message (no longer relevant)

#### Rationale
Frame comparison was unreliable because:
- `_lastAppliedFrame` was only updated when movement was applied
- After command ends, authority stops applying ? frame goes stale
- New systems try to move ? their calls blocked because frame doesn't match

The new `IsCurrentlyApplying` flag is:
- Set synchronously BEFORE the `SetMoveDir` call
- Cleared synchronously AFTER the `SetMoveDir` call returns
- Only true during the exact execution of authority's `SetMoveDir`
- No timing issues, no stale values

#### Code Flow
```
1. Authority.ApplyMoveDirectionInternal() called
2. _isCurrentlyApplying = true  ? Flag set
3. _character.SetMoveDir(dir)   ? This triggers Harmony patch
4. Harmony patch sees IsCurrentlyApplying=true ? ALLOWS call
5. SetMoveDir returns
6. _isCurrentlyApplying = false ? Flag cleared (in finally block)

Later, when authority is released and other systems try to move:
1. CompanionAI acquires IdleWander authority
2. Authority.SetMoveDirection() called ? stores direction
3. Authority.Update() ? ApplyMovement() ? ApplyMoveDirectionInternal()
4. Flag set ? SetMoveDir ? patch allows ? Flag cleared
```

#### Status
**IMPLEMENTED** - Build successful, awaiting testing

---

## Bug #3: IdleWander Releases Authority Every Frame

### Date Identified
2024 (Current) - Found while comparing Following vs IdleWander code paths

### Symptoms
- Companions in stay mode don't wander even though authority is acquired
- Logs show authority acquired and direction accepted, but no movement happens
- Following mode works correctly

### Root Cause Analysis

**Following mode (WORKS):**
```csharp
// UpdateFollowMovement() - called every frame when following
switch (_currentFollowSpeed)
{
    case FollowSpeed.Walking:
        MoveTowardThroughAuthority(ownerPos, false);  // Called EVERY FRAME
        break;
}
```
- Every frame while walking/running, it calls `MoveTowardThroughAuthority()`
- This maintains the authority and keeps sending direction

**Idle Wander (BROKEN):**
```csharp
// UpdateIdleWander() - only moves once per interval!
if (Time.time - _lastIdleWanderTime > idleWanderInterval)
{
    _lastIdleWanderTime = Time.time;
    MoveTowardThroughAuthority(wanderTarget, false);  // Only called when interval elapses
}
else
{
    IdleMovementThroughAuthority(dt);  // RELEASES AUTHORITY!
}
```

**The Bug:**
1. Frame 1: `idleWanderInterval` (10s) elapses ? picks wander target ? acquires authority ? sets direction
2. Frame 2: `idleWanderInterval` NOT elapsed ? calls `IdleMovementThroughAuthority()` ? **RELEASES AUTHORITY**
3. Authority released ? movement stops ? companion doesn't move

**Why IdleMovementThroughAuthority releases:**
```csharp
private void IdleMovementThroughAuthority(float dt)
{
    StopMovementThroughAuthority();  // <-- Releases authority every frame!
}
```

### The Fix

Change `UpdateIdleWander()` to continuously move toward the wander target until reached, similar to how following works.

**Before:**
```csharp
private void UpdateIdleWander(float dt)
{
    if (Time.time - _lastIdleWanderTime > idleWanderInterval)
    {
        _lastIdleWanderTime = Time.time;
        Vector2 randomPoint = Random.insideUnitCircle * idleWanderRadius;
        Vector3 wanderTarget = _stayPosition + new Vector3(randomPoint.x, 0, randomPoint.y);
        MoveTowardThroughAuthority(wanderTarget, false);
    }
    else
    {
        IdleMovementThroughAuthority(dt);  // RELEASES AUTHORITY!
    }
}
```

**After:**
```csharp
private Vector3 _currentWanderTarget;
private bool _hasWanderTarget = false;

private void UpdateIdleWander(float dt)
{
    // Check if we need a new wander target
    if (!_hasWanderTarget || Time.time - _lastIdleWanderTime > idleWanderInterval)
    {
        _lastIdleWanderTime = Time.time;
        Vector2 randomPoint = Random.insideUnitCircle * idleWanderRadius;
        _currentWanderTarget = _stayPosition + new Vector3(randomPoint.x, 0, randomPoint.y);
        _hasWanderTarget = true;
    }
    
    // Move toward wander target if we have one
    if (_hasWanderTarget)
    {
        float distToTarget = Vector3.Distance(transform.position, _currentWanderTarget);
        if (distToTarget < POSITION_REACHED_THRESHOLD)
        {
            // Reached target, stop and wait for next interval
            _hasWanderTarget = false;
            StopMovementThroughAuthority();
        }
        else
        {
            // Keep moving toward target every frame (like following does)
            MoveTowardThroughAuthority(_currentWanderTarget, false);
        }
    }
}
```

### Status
**IMPLEMENTED** - Build successful

### Fix Applied
**File: `CompanionAI.Idle.cs`**
- Changed `UpdateIdleWander()` to track wander target persistently
- Move toward target every frame until reached (like following does)
- Only pick new target when: no current target OR interval elapsed

**File: `CompanionAI.cs`**
- Added `_currentWanderTarget` (Vector3) field
- Added `_hasWanderTarget` (bool) field

---

## Files Modified (All Bugs)

| File | Change Type | Description |
|------|-------------|-------------|
| `CompanionPatches.cs` | Modified | Bug #1: Fixed Harmony patches to not block authority calls |
| `CompanionPatches.cs` | Modified | Bug #1: Changed from `ShouldFreeze()` to `IsInFrozenState` |
| `UnifiedMovementAuthority.cs` | Modified | Bug #2: Added `IsCurrentlyApplying` flag for reliable call identification |
| `CompanionPatches.cs` | Modified | Bug #2: Changed from frame comparison to flag check |
| `CompanionAI.Idle.cs` | Modified | Bug #3: Fixed idle wander to move continuously toward target |
| `CompanionAI.cs` | Modified | Bug #3: Added wander target tracking fields |
| `CompanionAI.Pathfinding.cs` | Modified | Bug #4: Added `MoveToThroughAuthority()` method |
| `CompanionAI.cs` | Modified | Bug #4: Replaced `MoveTo()` with `MoveToThroughAuthority()` |

---

## Bug #4: BaseAI.MoveTo() Bypasses Authority System

### Date Identified
2024 (Current) - Found by analyzing blocked movement logs

### Symptoms
- Companions in stay mode have movement BLOCKED by Harmony patch
- Following works but commands/stay mode don't
- Logs show blocked directions with Y component (e.g., `dir=(0.07, 0.20, -1.00)`)
- Authority system normalizes Y to 0, but blocked directions have Y != 0
- This proves movement is coming from somewhere OTHER than authority

### Root Cause Analysis

**The blocked directions have Y components:**
```
[CompanionPatches] BLOCKED external SetMoveDir for Grid the Tremendous: dir=(0.07, 0.20, -1.00)
[CompanionPatches] BLOCKED external SetMoveDir for Asny the Delver: dir=(-0.98, 0.07, -0.19)
```

**Our authority always normalizes Y to 0:**
```csharp
// In SetMoveDirThroughAuthority and MoveTowardThroughAuthority:
direction.y = 0;  // We ALWAYS zero out Y
```

**So where is the Y component coming from?**

Found it in `CompanionAI.cs`:
```csharp
if (_hasCommandDestination)
{
    if (MoveTo(dt, _commandDestination, 0f, run: !shouldWalk))  // <-- BaseAI.MoveTo()!
```

**BaseAI.MoveTo()** is Valheim's built-in pathfinding method. It internally calls `m_character.SetMoveDir(direction)` DIRECTLY, bypassing our authority system. It also doesn't normalize Y to 0.

### The Full Problem

1. **BaseAI.MoveTo()** - Called for command destinations, pathfinding
2. **BaseAI.MoveAndAvoid()** - Another base method that calls SetMoveDir
3. **MonsterAI inheritance** - Some code paths may still trigger base AI movement

These all bypass `UnifiedMovementAuthority` and get BLOCKED by our Harmony patch.

### The Fix

Replace ALL `MoveTo()` calls with authority-based equivalents:

**Instead of:**
```csharp
if (MoveTo(dt, _commandDestination, 0f, run: !shouldWalk))
```

**Use:**
```csharp
if (MoveToThroughAuthority(_commandDestination, !shouldWalk))
```

Create `MoveToThroughAuthority()` that:
1. Routes through `UnifiedMovementAuthority`
2. Returns true when destination is reached
3. Handles pathfinding state internally

### Status
**IMPLEMENTED** - Build successful

### Fix Applied
**File: `CompanionAI.Pathfinding.cs`**
- Added `MoveToThroughAuthority()` method as replacement for `BaseAI.MoveTo()`
- Routes movement through `UnifiedMovementAuthority` instead of calling base game code
- Returns true when destination reached, false while moving

**File: `CompanionAI.cs`**
- Changed command destination movement from `MoveTo(dt, _commandDestination, 0f, run: !shouldWalk)` 
  to `MoveToThroughAuthority(_commandDestination, run: !shouldWalk)`

---

## Bug #5: Sub-Behaviors Interrupted by Following

### Date Identified
2024 (Current) - Found when testing mining command

### Symptoms
- Companion commanded to mine copper walks away when player moves
- Logs show `CompanionAI ACQUIRED Following authority` during mining operation
- Sub-behavior tries to move to mine but following movement overrides it

### Root Cause Analysis

**The Problem:**
When a companion is executing a sub-behavior (like mining), `CompanionAI` was still:
1. Checking `_shouldFollow` and transitioning to `Following` state
2. Requesting `Following` authority from `UnifiedMovementAuthority`
3. The `Following` authority (priority 50) could override `SubBehavior` (priority 60)

**Code Flow:**
```csharp
// UpdateIdleState - would transition to Following even during sub-behavior!
private void UpdateIdleState(float dt)
{
    if (_shouldFollow && _followTarget != null)
    {
        SetState(AIState.Following);  // <-- Wrong! Sub-behavior is active!
        return;
    }
}
```

### The Fix

**File: `CompanionAI.Idle.cs`**
1. Added check for `idleBehavior.IsInSubBehavior` at start of `UpdateIdleState()`
2. If sub-behavior is active, return early - let sub-behavior handle movement
3. Same check added to `UpdateFollowingState()` - stop following if sub-behavior active

**File: `CompanionAI.Pathfinding.cs`**
1. Updated `GetAIMovementSource()` to check for active sub-behavior first
2. Returns `SubBehavior` priority when sub-behavior is active

### Status
**IMPLEMENTED** - Build successful

---

## Current Status Summary (Bug #5 Testing)

### What's Working Now
Based on the latest logs:

1. **SubBehavior Authority Working**: When a command is issued, companions correctly acquire `SubBehavior` authority:
   ```
   [MovementAuthority] Asny the Delver CompanionAI ACQUIRED SubBehavior authority
   ```

2. **Resource Gathering Commands Working**: Mining commands now correctly use SubBehavior priority and complete successfully:
   ```
   [CompanionIdleBehavior] Asny the Delver STARTED sub-behavior ResourceGathering - _activeSubBehavior is now SET, IsInSubBehavior=True
   [CompanionAutoPickup] Asny the Delver collected 14 items: Wood x9, Beech Seeds x3, Resin x2
   [COMMAND] CompleteCommand for Asny the Delver: Success
   ```

3. **Following Properly Resumes After Sub-Behavior**: After completing a task, companions return to following:
   ```
   [MovementAuthority] Asny the Delver CompanionAI ACQUIRED Following authority
   ```

### Remaining Issues Identified

#### Issue #1: Pathfinding Timeout ? Teleport (20s is too aggressive)
Companions are timing out and teleporting when they're actually making progress:
```
[SmelterOperator] Grid the Tremendous pathfinding timeout after 20s - TELEPORTING near smelter (distance: 15.0m)
[SmelterOperator] Dagny Ironside pathfinding timeout after 20s - TELEPORTING near smelter (distance: 4.4m)
```
- 20 seconds may not be enough for complex paths
- Distance of 4.4m is very close - shouldn't need teleport

#### Issue #2: No Stuck Detection During Movement
The current timeout is time-based only, doesn't check if companion is actually making progress:
- If stuck on terrain/obstacle, they wait full 20s before teleporting
- If making slow progress, they still teleport even though they'd arrive

#### Issue #3: Station Occupancy Not Properly Preventing Duplicate Workers
Multiple companions may be trying to work the same station, causing movement conflicts

---

## Bug #6: Pathfinding Improvements Needed

### Symptoms
- Companions teleport after 20s even when close to target
- No detection of actual "stuck" state vs "slow progress"
- No obstacle avoidance during sub-behavior movement

### Implemented Fixes

**File: `SmelterOperatorBehavior.cs`**
Added new progress tracking fields:
```csharp
// PATHFINDING PROGRESS TRACKING - for better stuck detection
private Vector3 _lastProgressPosition;
private float _lastProgressTime;
private float _noProgressDuration = 0f;
private const float PROGRESS_CHECK_INTERVAL = 1.0f;      // How often to check progress
private const float PROGRESS_THRESHOLD = 0.3f;           // Min distance to count as progress
private const float NO_PROGRESS_TIMEOUT = 8.0f;          // How long without progress before stuck
private const float PATHFINDING_TIMEOUT_BASE = 30f;      // Base timeout (increased from 20)
private const float PATHFINDING_TIMEOUT_PER_METER = 1.5f; // Extra time per meter of distance
private const float MAX_PATHFINDING_TIMEOUT = 60f;       // Maximum timeout regardless of distance
```

**File: `SmelterOperatorBehavior.StationPhases.cs`**
1. **Added `UpdateProgressTracking()`** - Tracks whether companion is actually making movement progress
2. **Added `ResetProgressTracking()`** - Resets tracking when reaching destination or teleporting
3. **Updated `UpdateMovingToStation()`**:
   - Now uses dynamic timeout based on distance (30s base + 1.5s per meter, max 60s)
   - Teleports early if NO progress for 8 seconds (stuck on obstacle)
   - Better logging with reason for teleport

**File: `SmelterOperatorBehavior.Lifecycle.cs`**
- Reset progress tracking fields in `Start()`

### Key Changes Summary

| Before | After |
|--------|-------|
| Fixed 20s timeout | Dynamic: 30s base + 1.5s/meter (max 60s) |
| Time-based only | Progress-based stuck detection |
| No early stuck detection | Teleports after 8s of no progress |
| Teleported even when close | Only teleports when truly stuck |

### Status
**IMPLEMENTED** - Build successful

---

## Bug #7: Excessive Logging Spam

### Date Identified
2024 (Current) - User reported logs being flooded with unhelpful messages

### Symptoms
- Logs filled with "SetMoveDirection ACCEPTED" every frame
- "APPLYING" logs every 60 frames still too frequent
- Makes debugging actual issues impossible

### Root Cause
Logging was implemented during debugging but never throttled for production:
- `SetMoveDirection()` logged every call
- `ApplyMoveDirectionInternal()` logged every second
- `Character_SetMoveDir_Prefix` logged every 2 frames

### Implemented Fixes

**File: `UnifiedMovementAuthority.cs`**
1. Added log throttling fields:
   ```csharp
   private Vector3 _lastLoggedDirection;
   private float _lastDirectionLogTime;
   private const float DIRECTION_LOG_INTERVAL = 2.0f;
   private const float DIRECTION_CHANGE_THRESHOLD = 0.3f;
   ```
2. `SetMoveDirection()` now only logs:
   - When direction changes significantly (> 0.3 difference)
   - When starting to move from stopped
   - When stopping from moving
   - At most once per 2 seconds if moving steadily
3. `SetMoveDestination()` only logs when destination is set
4. Removed per-frame `APPLYING` logs entirely

**File: `CompanionPatches.cs`**
1. Removed authority call logging entirely (was logging every 120 frames)
2. Reduced blocked movement logging to once per 5 seconds (was once per 60 frames)

### What Gets Logged Now
- Authority acquisition/release (important state changes)
- Significant direction changes (> 0.3 difference)
- Start/stop of movement
- Teleport events with reason
- Actual errors and warnings

### What's NO LONGER Logged
- Every SetMoveDirection call (was 60+ per second per companion)
- Every movement application (was every 60 frames)
- Repeated authority allows (was every 120 frames)
- Small direction adjustments

### Status
**IMPLEMENTED** - Build successful

---

## Files Modified Summary (All Bugs)

| File | Changes |
|------|---------|
| `CompanionPatches.cs` | Bug #1, #2, #7: Authority identification, freeze checks, log throttling |
| `UnifiedMovementAuthority.cs` | Bug #2, #7: `IsCurrentlyApplying` flag, log throttling |
| `CompanionAI.Idle.cs` | Bug #3, #5: Wander tracking, sub-behavior checks |
| `CompanionAI.cs` | Bug #3, #4: Wander fields, MoveTo replacement |
| `CompanionAI.Pathfinding.cs` | Bug #4, #5: `MoveToThroughAuthority`, sub-behavior priority |
| `SmelterOperatorBehavior.cs` | Bug #6: Progress tracking fields |
| `SmelterOperatorBehavior.StationPhases.cs` | Bug #6: Progress tracking, dynamic timeout |
| `SmelterOperatorBehavior.Lifecycle.cs` | Bug #6: Progress tracking reset |

---

## Architecture Reference

### CORRECT Architecture (After Bug #8 Fix)

```
???????????????????????????????????????????????????????????????????
?                    MOVEMENT COORDINATION                         ?
?                                                                  ?
?   Multiple systems want to move companion:                       ?
?   - CompanionAI (following, combat, idle)                       ?
?   - IdleBehaviors (mining, smelting, chest deposit)             ?
?   - CompanionCombatMovement (combat positioning)                ?
?   - Commands (player ping, attack target)                       ?
?                                                                  ?
?   UnifiedMovementAuthority decides WHO gets to move:            ?
?   ????????????????????                                          ?
?   ? TryAcquire(src)  ? ? Returns true if this source can move   ?
?   ? Release()        ? ? Release when done moving               ?
?   ? CurrentAuthority ? ? Which source has control               ?
?   ????????????????????                                          ?
?           ?                                                      ?
?   Winner calls VANILLA PATHFINDING:                             ?
?   ????????????????????                                          ?
?   ? BaseAI.MoveTo()  ? ? Valheim's pathfinding                  ?
?   ? BaseAI.FindPath()? ? NavMesh path calculation               ?
?   ? BaseAI.m_path    ? ? Waypoint list around obstacles         ?
?   ????????????????????                                          ?
?           ?                                                      ?
?   Vanilla calls Character.SetMoveDir()                          ?
?           ?                                                      ?
?   Character moves with proper obstacle avoidance                ?
???????????????????????????????????????????????????????????????????
```

### Movement Authority Priority Levels
```
Priority | Source         | Use Case
---------|----------------|----------------------------------
100      | Forced         | Knockback, teleport, ragdoll
 90      | PlayerCommand  | Ping move, attack commands
 80      | Animation      | Root motion, attack animations
 70      | Combat         | Combat AI movement
 60      | SubBehavior    | Idle sub-behaviors (chores)
 50      | Following      | Following owner
 40      | IdleWander     | Idle wandering
  0      | None           | No movement
```

### Freeze States vs Animation Blocking

**True Frozen States** (`IsInFrozenState`):
- `UIInteraction` - Player has UI panel open
- `Emote` - Playing persistent emote
- `ChairSit` - Sitting on furniture

These COMPLETELY lock movement - nothing should move.

**Animation Blocking** (`IsAnimationBlocking`):
- `Standing` - Standing pose (not in locomotion)
- `Attacking` - Attack animation
- `Staggered` - Hit stagger
- `Emoting` - Emote animation
- `Sitting` - Sitting animation
- `Interacting` - Object interaction

These are meant to prevent sliding, but should NOT block the authority's movement.

### Key Insight
The `UnifiedMovementAuthority` handles smooth transitions internally via `_movementBlendSpeed`. It doesn't need external systems blocking its calls. The authority already:
1. Blends movement direction smoothly
2. Manages its own freeze state (`_isMovementFrozen`)
3. Tracks animation readiness (`IsAnimatorReady`)

External patches should only enforce TRUE frozen states, not animation transitions.

---

## Lessons Learned

1. **Authority is Authority**: When you have a "single source of truth" system, don't let other systems override it. The Harmony patches were undermining the authority.

2. **Chicken-and-Egg Detection**: When debugging "nothing happens" bugs, look for circular dependencies. Movement needs animation but animation needs movement = deadlock.

3. **Log Analysis**: The logs showing "ACCEPTED" but no "APPLYING" were the key clue. The authority was doing its job, but something downstream was blocking.

4. **Check Order Matters**: In prefix patches, check the "should this be allowed" conditions in the right order. Authority calls should be checked FIRST.

---

## Files Modified

| File | Change Type | Description |
|------|-------------|-------------|
| `CompanionPatches.cs` | Modified | Bug #1, #2: Fixed Harmony patches to not block authority calls |
| `UnifiedMovementAuthority.cs` | Modified | Bug #2: Added `IsCurrentlyApplying` flag |
| `CompanionAI.Idle.cs` | Modified | Bug #3, #5: Fixed idle wander and sub-behavior checks |
| `CompanionAI.cs` | Modified | Bug #3, #4: Added wander target fields, replaced MoveTo |
| `CompanionAI.Pathfinding.cs` | Modified | Bug #4, #5: Added MoveToThroughAuthority, sub-behavior check |

---

## Related Documentation
- `UNIFIED_MOVEMENT_AUTHORITY_PLAN.md` - Original design doc
- `COMPANION_IDLE_WORK_BUGS.md` - Related idle behavior bugs

---

## Bug #9: Authority Re-Acquisition Spam + SmelterOperator Not Moving

### Date Identified
2025-03-01 (Today)

### Symptoms
1. **Log Spam**: `[MovementAuthority] Grid the Tremendous CompanionCombatMovement ACQUIRED Following authority` repeated every frame
2. **SmelterOperator Not Moving**: Companions commanded to operate smelters stay in place, don't walk to station or chests
3. **WorkstationInteraction Timeout**: `[WorkstationInteraction] Phase MovingToWorkstation timed out` repeatedly

### Root Cause Analysis

#### Issue 1: Authority Log Spam
**Location**: `UnifiedMovementAuthority.TryAcquireAuthority()`

The method was logging "ACQUIRED" even when the same owner was just extending their authority duration (which happens every frame).

```csharp
// BEFORE - Logged every frame when same owner re-acquires
if (source == CurrentAuthority && owner == CurrentAuthorityOwner)
{
    // Just extend the duration
    if (duration > 0)
    {
        AuthorityDuration = duration;
        _authorityTimeoutTime = Time.time + duration;
    }
    return true;  // <-- No logging, but FELL THROUGH to log at end!
}
// ... later ...
Debug.Log($"[MovementAuthority] {_companion?.companionName} {owner} ACQUIRED {source} authority"...);
```

Actually the issue was that even silent extensions were returning early, but a separate case later was logging for ALL acquisitions including those where source/owner didn't change.

#### Issue 2: SmelterOperator Bypassing Authority
**Location**: `SmelterOperatorBehavior.Helpers.cs` - `MoveToPosition()`

The behavior had its own `MoveToPosition()` method that called `_combatMovement.SetMoveDestination()` directly, bypassing:
1. The base class `IdleSubBehavior.TryMoveToPosition()`
2. The `CompanionAI.RequestPathfindingMovement()` API
3. Proper authority acquisition with `SubBehavior` priority
4. Vanilla pathfinding via `BaseAI.MoveTo()`

```csharp
// BEFORE - Bypassed proper authority flow
private void MoveToPosition(Vector3 position)
{
    if (_combatMovement != null)
    {
        _combatMovement.SetMoveDestination(standoffPos, useWalk: true, skipCommandOverride: true);
    }
}
```

### Fixes Applied

#### Fix 1: Silent Authority Extensions
**File**: `UnifiedMovementAuthority.cs`

Changed `TryAcquireAuthority()` to only log when authority actually changes hands:

```csharp
// AFTER - Silent extensions, only log real changes
if (source == CurrentAuthority && owner == CurrentAuthorityOwner)
{
    // Just extend the duration - NO LOGGING
    if (duration > 0)
    {
        AuthorityDuration = duration;
        _authorityTimeoutTime = Time.time + duration;
    }
    return true;
}

// ... later, after granting authority ...

// Only log when authority ACTUALLY changes hands
if (previousSource != source || previousOwner != owner)
{
    Debug.Log($"[MovementAuthority] {_companion?.companionName} {owner} ACQUIRED {source} authority"...);
}
```

#### Fix 2: SmelterOperator Uses Base Class Movement
**File**: `SmelterOperatorBehavior.Helpers.cs`

Replaced custom `MoveToPosition()` with call to base class `TryMoveToPosition()`:

```csharp
// AFTER - Uses proper authority + pathfinding
private void MoveToPosition(Vector3 position)
{
    Vector3 standoffPos = CalculateStandoffPosition(position);
    _targetPosition = position;
    
    // CRITICAL: Use base class TryMoveToPosition for proper authority integration
    // This uses CompanionAI pathfinding which handles obstacles correctly
    TryMoveToPosition(standoffPos, walk: true, run: false);
}

private new void StopMovement()
{
    base.StopMovement();  // Releases authority properly
    _combatMovement?.ClearMoveDestination();
    // ... velocity cleanup ...
}
```

### Movement Flow After Fix

```
SmelterOperatorBehavior.MoveToPosition(target)
    ?
    ?
IdleSubBehavior.TryMoveToPosition(standoffPos, walk=true, run=false)
    ?
    ?
CompanionAI.RequestPathfindingMovement(destination, run=false, source=SubBehavior, owner="SmelterOperator")
    ?
    ?
UnifiedMovementAuthority.TryAcquireAuthority(SubBehavior, "SmelterOperator", 5f)
    ?
    ?
BaseAI.MoveTo(destination) ? VANILLA PATHFINDING with obstacle avoidance
    ?
    ?
Character walks to smelter using NavMesh waypoints
```

### Files Modified

| File | Changes |
|------|---------|
| `UnifiedMovementAuthority.cs` | Silent authority extensions - no logging for same owner re-acquiring |
| `SmelterOperatorBehavior.Helpers.cs` | `MoveToPosition()` now uses `base.TryMoveToPosition()` for proper authority/pathfinding |

### Status
**IMPLEMENTED** - Build successful, awaiting in-game testing

### Expected Results After Fix
1. **No more log spam**: Authority acquisition only logged on actual transitions
2. **SmelterOperator movement**: Companions should now walk to smelters using vanilla pathfinding
3. **Proper authority flow**: SubBehavior authority properly acquired and held throughout operation

### Testing Checklist
- [ ] Command companion to operate smelter (ping a smelter)
- [ ] Verify companion walks to smelter (not stuck in place)
- [ ] Verify companion adds fuel/ore to smelter
- [ ] Verify companion walks to chests if needed for materials
- [ ] Verify logs show single "ACQUIRED SubBehavior authority" at start
- [ ] Verify no repeated "ACQUIRED Following authority" spam

---

## Bug #13: Behaviors Not Calling Movement Every Frame (2025-03-02)

### Date Identified
2025-03-02

### Symptoms
1. **BowTraining**: Companion runs to target and stands there, never repositions
2. **ResourceGathering**: Companion walks partway then stops
3. **CraftingUpgrade**: Companion doesn't walk to workstation
4. **Following companions not responding to commands**: Behaviors start but no movement occurs

### Root Cause Analysis

**The Core Issue:**
Vanilla pathfinding (`BaseAI.MoveTo()`) uses waypoint-based navigation:
- `FindPath()` calculates a path and stores waypoints in `m_path`
- Each call to `MoveTo()` only progresses ONE waypoint
- Movement requires being called EVERY FRAME to follow the full path

**What behaviors were doing wrong:**
```csharp
// WRONG: Only called MoveToPosition() ONCE when starting the phase
void UpdateFindingTarget()
{
    MoveToPosition(_firingPosition);  // Called once
    SetPhase(TrainingPhase.MovingToPosition);
}

void UpdateMovingToPosition()
{
    if (dist < POSITION_TOLERANCE)  // Just checks distance
    {
        SetPhase(TrainingPhase.Aiming);
    }
    // NO MOVEMENT CALL HERE!
}
```

**What they should do:**
```csharp
void UpdateMovingToPosition()
{
    if (dist < POSITION_TOLERANCE)
    {
        SetPhase(TrainingPhase.Aiming);
    }
    else
    {
        MoveToPosition(_firingPosition);  // Called EVERY FRAME!
    }
}
```

### Fixes Applied

#### Fix 1: BowTrainingBehavior
**File**: `BowTrainingBehavior.cs`

1. `UpdateMovingToPosition()` - Now calls `MoveToPosition()` every frame
2. `UpdateRetrievingArrows()` - Now calls `MoveToPosition()` every frame while not at target
3. `MoveToPosition()` - Now uses `base.TryMoveToPosition()` for proper authority + vanilla pathfinding
4. `StopMovement()` - Now uses `base.StopMovement()` for proper authority release

#### Fix 2: ResourceGatheringBehavior
**File**: `ResourceGatheringBehavior.cs`

1. `MoveToPosition()` - Now uses `base.TryMoveToPosition()` instead of `_combatMovement.SetMoveDestination()`
2. `StopMovement()` - Now uses `base.StopMovement()` for proper authority release
3. Phases already called `MoveToPosition()` every frame in `UpdateMovingToResource()` - that was correct

#### Fix 3: CraftingUpgradeBehaviorV2
**File**: `CraftingUpgradeBehaviorV2.cs`

1. `UpdateMovingToWorkstation()` - Now uses `ContinueMovement()` instead of just checking distance

### Movement Flow After Fix

```
BowTrainingBehavior.MoveToPosition(target)
    |
    +-> base.TryMoveToPosition(target, walk=true, run=false)
        |
        +-> CompanionAI.RequestPathfindingMovement(target, SubBehavior, "BowTraining")
            |
            +-> UnifiedMovementAuthority.TryAcquireAuthority(SubBehavior, "BowTraining", 5f)
            |
            +-> BaseAI.MoveTo(target) -> VANILLA PATHFINDING
                |
                +-> FindPath() -> m_path waypoints -> follows one per frame
                |
                +-> Character moves around obstacles using NavMesh

Each Update() frame:
    BowTrainingBehavior.Update()
        |
        +-> UpdateMovingToPosition()
            |
            +-> MoveToPosition(_firingPosition)  <-- Called EVERY FRAME!
```

### Key Insight

The `WorkBehaviorBase.ContinueMovement()` pattern is the CORRECT approach:
- `MoveToPosition()` sets up the target and calls initial movement
- `ContinueMovement()` is called every frame and:
  - Checks if arrived -> returns true
  - If not arrived -> calls `base.TryMoveToPosition()` again to continue pathfinding

Non-WorkBehaviorBase behaviors (like `BowTrainingBehavior` and `ResourceGatheringBehavior`) need to:
1. Call their `MoveToPosition()` method EVERY FRAME during movement phases
2. Use `base.TryMoveToPosition()` which integrates with authority + vanilla pathfinding

### Files Modified

| File | Changes |
|------|---------|
| `BowTrainingBehavior.cs` | `MoveToPosition()` uses `base.TryMoveToPosition()`, `UpdateMovingToPosition()` calls it every frame |
| `ResourceGatheringBehavior.cs` | `MoveToPosition()` uses `base.TryMoveToPosition()`, `StopMovement()` uses `base.StopMovement()` |
| `CraftingUpgradeBehaviorV2.cs` | `UpdateMovingToWorkstation()` now uses `ContinueMovement()` |

### Status
**IMPLEMENTED** - Build successful

### Testing Checklist
- [ ] Command companion to train with bow -> should walk to position, shoot, walk to retrieve arrows
- [ ] Command companion to gather resources -> should walk to resource using pathfinding
- [ ] Stayed companion near workstation -> should walk to workbench and work
- [ ] All V2 behaviors should use `ContinueMovement()` in moving phases

---

## Bug #10: Following Authority Not Released When Commanded to Stay

### Date Identified
2025-03-01 (Today)

### Symptoms
After testing Bug #9 fixes:
1. **Repeated DENIED logs**: `[MovementAuthority] Grid the Tremendous CompanionAI (IdleWander) DENIED - CompanionAI (Following) has higher priority`
2. **SmelterOperator stuck**: Companions still not moving to smelters after being commanded to stay
3. **Authority conflict**: Sub-behaviors trying to acquire `SubBehavior` authority but `Following` authority is still held

### Root Cause Analysis

**The Problem Chain:**
1. Companion is set to follow owner
2. `CompanionAI` acquires `Following` authority (priority 50) with owner "CompanionAI"
3. Player commands companion to "Stay"
4. `SetShouldFollow(false)` is called
5. State transitions to `Idle` BUT authority is NOT released
6. Sub-behavior (SmelterOperator) starts and tries to acquire `SubBehavior` authority (priority 60)
7. It SHOULD work because SubBehavior (60) > Following (50)...
8. BUT the problem is CompanionAI keeps trying to re-acquire IdleWander (40) authority every frame
9. The logs show `IdleWander DENIED` spam because Following still has authority

**The Real Issue:**
When `SetShouldFollow(false)` is called, it:
- Sets `_shouldFollow = false`
- Sets `_followTarget = null`
- Transitions to `Idle` state

But it does NOT:
- Release the held `Following` authority
- Clear the authority system

### Fix Applied

**File: `CompanionAI.cs` - `SetShouldFollow()` method**

Added authority release when transitioning from following to stay mode:

```csharp
public void SetShouldFollow(bool shouldFollow)
{
    bool wasFollowing = _shouldFollow;
    _shouldFollow = shouldFollow;
    
    // CRITICAL BUG #10 FIX: When told to stop following (stay mode), release any held authority
    if (wasFollowing && !shouldFollow)
    {
        var authority = _companion?.GetMovementAuthority();
        if (authority != null)
        {
            authority.ReleaseAuthority(AI_AUTHORITY_OWNER);
        }
        
        _followTarget = null;
        
        if (_currentState == AIState.Following)
        {
            SetState(AIState.Idle);
        }
    }
    // ... rest of method
}
```

**File: `UnifiedMovementAuthority.cs` - DENIED log throttling**

Added throttling to reduce log spam when authority is denied:
- Only logs DENIED once per 5 seconds per source/owner combination
- Prevents flooding logs when systems repeatedly try to acquire authority

### Files Modified

| File | Changes |
|------|---------|
| `CompanionAI.cs` | `SetShouldFollow(false)` now releases authority |
| `UnifiedMovementAuthority.cs` | Added DENIED log throttling (5s cooldown) |

### Movement Flow After Fix

```
Player commands companion to "Stay"
    ?
    ?
CompanionController.CommandStay()
    ?
    ?
CompanionAI.SetShouldFollow(false)
    ?
    ??? Release Following authority ? NEW!
    ??? Set _followTarget = null
    ??? SetState(AIState.Idle)
    ?
    ?
Authority is now released - no owner
    ?
    ?
SmelterOperator.Start() ? TryMoveToPosition()
    ?
    ?
RequestPathfindingMovement(destination, SubBehavior, "SmelterOperator")
    ?
    ?
TryAcquireAuthority(SubBehavior, "SmelterOperator") ? SUCCESS!
    ?
    ?
Companion walks to smelter using vanilla pathfinding
```

### Status
**IMPLEMENTED** - Build successful, awaiting in-game testing

### Expected Results After Fix
1. **No more DENIED spam**: Authority is released when told to stay
2. **Sub-behaviors work**: SmelterOperator and other behaviors can acquire SubBehavior authority
3. **Companions move**: Should walk to smelters/workstations properly

---

## Bug #11: V2 Behaviors Only Call MoveToPosition() Once, Not Every Frame

### Date Identified
2025-03-01 (Today)

### Symptoms
1. **WorkstationInteraction timeout**: `[WorkstationInteraction] Phase MovingToWorkstation timed out` repeatedly
2. **BowTraining works**: Only behavior working correctly is `BowTrainingBehavior`
3. **Companions don't move**: Stayed companions just stand there, never walk to targets

### Root Cause Analysis

**The Problem:**
V2 behaviors using `WorkBehaviorBase` were calling `MoveToPosition()` **only once** when starting a moving phase, then just checking distance in their update loops.

**Vanilla `MoveTo()` requires every-frame calls** to work properly! It follows waypoints one at a time, and each call progresses through the path.

**Broken Pattern in V2 behaviors:**
```csharp
// In UpdateFindingWorkstation() - called ONCE when phase starts
MoveToPosition(_workPosition);
SetPhase(WorkPhase.MovingToWorkstation);

// In UpdateMovingToWorkstation() - called EVERY FRAME but...
if (IsWithinInteractionDistance(_workPosition, INTERACTION_DISTANCE))
{
    StopMovement();
    StartWorking();
}
// NO MOVEMENT CALL HERE! Just checks distance!
```

**Why BowTraining works:**
`BowTrainingBehavior` doesn't use `WorkBehaviorBase` and has its own movement logic that works differently.

### Fixes Applied

#### Fix 1: Added `ContinueMovement()` to `WorkBehaviorBase`
**File**: `WorkBehaviorBase.cs`

Added new `ContinueMovement()` method that must be called every frame in moving phases:

```csharp
protected Vector3? CurrentMoveTarget { get; private set; }
protected bool IsMoving => CurrentMoveTarget.HasValue;

protected new void MoveToPosition(Vector3 position, bool useWalk = true)
{
    CurrentMoveTarget = position;
    base.TryMoveToPosition(position, useWalk, !useWalk);  // Initial request
}

/// <summary>
/// MUST be called every frame while in a "Moving" phase!
/// </summary>
protected bool ContinueMovement()
{
    if (!CurrentMoveTarget.HasValue)
        return true;  // No target = done
    
    float dist = DistanceTo(CurrentMoveTarget.Value);
    if (dist < 2.5f)
    {
        CurrentMoveTarget = null;
        return true;  // Arrived
    }
    
    // CRITICAL: Call TryMoveToPosition EVERY FRAME!
    base.TryMoveToPosition(CurrentMoveTarget.Value, walk: true, run: false);
    return false;  // Still moving
}
```

#### Fix 2: Updated All V2 Behavior Moving Phases

| File | Method | Change |
|------|--------|--------|
| `WorkstationInteractionBehaviorV2.cs` | `UpdateMovingToWorkstation()` | Now calls `ContinueMovement()` |
| `FireTendingBehaviorV2.cs` | `UpdateMovingToFire()` | Now calls `ContinueMovement()` |
| `LootPickupBehaviorV2.cs` | `UpdateMovingToItem()` | Now calls `ContinueMovement()` |
| `ChestDepositBehaviorV2.cs` | `UpdateMovingToChest()` | Now calls `ContinueMovement()` |

**Updated Pattern:**
```csharp
private bool UpdateMovingToWorkstation()
{
    if (_targetStation == null) { ... }
    
    // CRITICAL: Call ContinueMovement() every frame!
    if (ContinueMovement())
    {
        // Arrived at destination
        StopMovement();
        StartWorking();
    }
    
    return false;
}
```

### Movement Flow After Fix

```
Frame 1: MoveToPosition(target) called
    ?
    ??? Sets CurrentMoveTarget = target
    ??? Calls base.TryMoveToPosition() ? Initial MoveTo()
    ??? Sets phase to MovingToWorkstation

Frame 2-N: UpdateMovingToWorkstation() called
    ?
    ??? ContinueMovement()
        ?
        ??? Check distance - not there yet
        ??? base.TryMoveToPosition() ? Continues MoveTo() pathfinding
            ?
            ??? BaseAI follows next waypoint in m_path

Frame N+1: ContinueMovement() returns true
    ?
    ??? CurrentMoveTarget = null
    ??? StopMovement(), StartWorking()
```

### Files Modified

| File | Changes |
|------|---------|
| `WorkBehaviorBase.cs` | Added `ContinueMovement()`, `CurrentMoveTarget`, `IsMoving` |
| `WorkstationInteractionBehaviorV2.cs` | `UpdateMovingToWorkstation()` now calls `ContinueMovement()` |
| `FireTendingBehaviorV2.cs` | `UpdateMovingToFire()` now calls `ContinueMovement()` |
| `LootPickupBehaviorV2.cs` | `UpdateMovingToItem()` now calls `ContinueMovement()` |
| `ChestDepositBehaviorV2.cs` | `UpdateMovingToChest()` now calls `ContinueMovement()` |

### Status
**IMPLEMENTED** - Build successful, awaiting in-game testing

### Expected Results After Fix
1. **WorkstationInteraction works**: Companions walk to workstations using vanilla pathfinding
2. **FireTending works**: Companions walk to fires/cooking stations
3. **LootPickup works**: Companions walk to loot items
4. **ChestDeposit works**: Companions walk to chests for depositing
5. **No more timeouts**: Moving phases complete successfully instead of timing out

### Testing Checklist
- [ ] Command companion to stay near workstation - verify they walk to it and work
- [ ] Command companion to stay near fire - verify they walk to it and add fuel
- [ ] Verify companions pick up loot when in stay mode
- [ ] Verify companions deposit items when inventory is full
- [ ] No more "Phase MovingToWorkstation timed out" messages

---
