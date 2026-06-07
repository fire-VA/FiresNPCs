# Unified Movement Authority - Complete Architecture

## The Sliding Bug - Root Cause Analysis

**The Problem:** Companions slide across the ground because multiple systems fight over movement control in the SAME FRAME.

When systems call `SetMoveDir()` with different values:
1. System A says "move forward"
2. System B says "move left"  
3. System C says "stop"
4. The animator receives inconsistent input
5. Visual animation doesn't match physics = **SLIDING**

**Systems that were calling SetMoveDir() independently:**
- `CompanionAI` - Pathfinding, following
- `CompanionCombatMovement` - Combat intents, strafing
- `CompanionIdleBehavior` - Wandering
- `IdleSubBehaviors` - Fire tending, smelting, etc.
- `MonsterAI/BaseAI` - Inherited pathfinding

## The Solution - Single Source of Truth

**`UnifiedMovementAuthority`** is now the ONLY component that can call `Character.SetMoveDir()` for companions.

### How It Works

1. **Harmony Patch Intercepts ALL SetMoveDir Calls**
   - `CompanionPatches.Character_SetMoveDir_Prefix` checks every `SetMoveDir()` call
   - If the call didn't come from `UnifiedMovementAuthority`, it's BLOCKED
   - Authority tracks `LastAppliedFrame` to identify its own calls

2. **Systems Request Movement Through Authority**
   ```csharp
   // OLD WAY - WRONG (causes sliding):
   _character.SetMoveDir(direction);
   
   // NEW WAY - CORRECT:
   var authority = companion.GetMovementAuthority();
   if (authority.TryAcquireAuthority(MovementSource.Combat, "CombatMovement", 2f))
   {
       authority.SetMoveDirection("CombatMovement", direction, walk, run);
   }
   ```

3. **Priority System Prevents Conflicts**
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

4. **Clean Authority Handoffs**
   - When authority changes, movement STOPS COMPLETELY first
   - New authority waits for velocity to settle
   - Then applies new movement direction
   - No overlap = no sliding

## Implementation Details

### UnifiedMovementAuthority Component
- Added to every companion via `CompanionController.Awake()`
- Tracks current authority holder
- Manages movement state machine (Stopped ? Starting ? Moving ? Stopping)
- Only place that calls `Character.SetMoveDir()`

### Harmony Patch (CompanionPatches.cs)
```csharp
[HarmonyPatch(typeof(Character), nameof(Character.SetMoveDir))]
public static bool Character_SetMoveDir_Prefix(Character __instance, Vector3 dir)
{
    var companion = __instance.GetComponent<CompanionController>();
    if (companion == null) return true; // Not a companion
    
    var authority = companion.GetMovementAuthority();
    if (authority != null)
    {
        // Only allow if call came from authority
        if (Time.frameCount == authority.LastAppliedFrame)
            return true; // Authority's call - allow
        
        if (authority.ShouldBlockExternalMovement)
            return false; // External call - BLOCK
    }
    
    return true;
}
```

### Movement State Machine
```
STOPPED ???? movement requested ????? STARTING
   ?                                      ?
   ?                                      ? animator ready
   ?                                      ?
STOPPING ???? stop requested ????????? MOVING
```

- **STOPPED**: No movement, velocity at zero
- **STARTING**: Movement requested, applying small velocity to trigger animation transition
- **MOVING**: Full movement with matching animation
- **STOPPING**: Blending velocity to zero, waiting for animator to settle

## Files Modified

| File | Changes |
|------|---------|
| `UnifiedMovementAuthority.cs` | Created - single source of truth |
| `CompanionController.cs` | Added authority component + getter |
| `CompanionPatches.cs` | Updated `SetMoveDir` patch to route through authority |
| `CompanionCombatMovement.Movement.cs` | Added authority helpers |
| `IdleSubBehavior.cs` | Added `TryMoveToPosition()` helper |
| `WorkBehaviorBase.cs` | Updated to use authority |
| `CompanionIdleBehavior.Wandering.cs` | Updated `SetDestination()` |
| `CompanionStateController.cs` | Connected freeze to authority |

## Testing Checklist

- [ ] Following: Companion follows smoothly without sliding
- [ ] Combat: Smooth transitions between follow ? combat ? follow
- [ ] Idle: Wandering doesn't cause jitter
- [ ] Sub-behaviors: Fire tending, smelting move properly
- [ ] Commands: Player commands take priority immediately
- [ ] Freeze states: Sitting, emoting completely frozen

## Debugging

Enable verbose logging:
```csharp
UnifiedMovementAuthority.VerboseLogging = true;
```

This will log:
- Authority acquisitions/releases
- Movement state transitions
- Blocked external SetMoveDir calls
m_character.SetMoveDir(direction);

// AFTER
var authority = _companion?.MovementAuthority;
if (authority != null && authority.TryAcquireAuthority(MovementSource.Following, "CompanionAI", 1f))
{
    authority.SetMoveDirection("CompanionAI", direction, walk, run);
}
```

**Files to update:**
- `CompanionAI.Pathfinding.cs` - `MoveDirectlyToward()`, `MoveToWithStuckDetection()`
- `CompanionAI.cs` - `UpdateFollowing()`

### Phase 5: Update CompanionIdleBehavior

```csharp
// BEFORE
_character.SetMoveDir(wanderDirection);

// AFTER
var authority = _stateController?.GetComponent<UnifiedMovementAuthority>();
if (authority != null && authority.TryAcquireAuthority(MovementSource.IdleWander, "IdleWander", 5f))
{
    authority.SetMoveDirection("IdleWander", wanderDirection, walk: true);
}
```

**Files to update:**
- `CompanionIdleBehavior.Wandering.cs` - `UpdateWandering()`
- `CompanionIdleBehavior.Chairs.cs` - Chair movement

### Phase 6: Update All IdleSubBehaviors

Each behavior needs to acquire `SubBehavior` priority:

```csharp
// BEFORE (in each behavior)
_character.SetMoveDir(direction);

// AFTER (via base class helper)
protected bool TryMoveToPosition(Vector3 position, bool walk = true)
{
    var authority = Companion?.GetComponent<UnifiedMovementAuthority>();
    if (authority == null) return false;
    
    if (!authority.TryAcquireAuthority(MovementSource.SubBehavior, BehaviorName, 5f))
        return false;
    
    authority.SetMoveDestination(BehaviorName, position, walk);
    return true;
}
```

**Files to update:**
- `IdleSubBehavior.cs` - Add helper methods
- `WorkBehaviorBase.cs` - Already uses `MoveToPosition()` helper, update implementation
- `SmelterOperatorBehavior.Helpers.cs` - `MoveToPosition()`
- `ResourceGatheringBehavior.cs` - `MoveToPosition()`
- All other behaviors that move

### Phase 7: Update CompanionStateController

Integrate with UnifiedMovementAuthority for state-based freezing:

```csharp
// When entering frozen state (sitting, emoting)
_movementAuthority?.FreezeMovement(reason, duration);

// When exiting frozen state
_movementAuthority?.UnfreezeMovement();
```

### Phase 8: Animation Notification Hooks

Add animation event callbacks:

```csharp
// Character patches or animation controller
public void OnAttackStart(float duration)
{
    _movementAuthority?.NotifyAnimationStart(duration);
}

public void OnAttackEnd()
{
    _movementAuthority?.NotifyAnimationEnd();
}
```

---

## Testing Checklist

After integration, verify these scenarios:

### Following Behavior
- [ ] Companion follows smoothly without sliding
- [ ] Walking/running transitions are clean
- [ ] Stopping has proper deceleration animation

### Combat Behavior
- [ ] Combat movement takes priority over following
- [ ] Attack animations lock movement
- [ ] Dodge/strafe works properly
- [ ] Returning to follow after combat is smooth

### Idle Behaviors
- [ ] Wandering doesn't fight with following
- [ ] Sub-behaviors (fire tending, smelting) control movement properly
- [ ] Transitioning between behaviors is clean

### Player Commands
- [ ] Ping-to-move has highest priority
- [ ] Commands interrupt other movement cleanly
- [ ] Attack commands work properly

### Frozen States
- [ ] Sitting completely stops movement
- [ ] Emotes prevent movement
- [ ] Exiting frozen state is smooth

---

## Priority Reference

| Priority | Source | Use Case |
|----------|--------|----------|
| 100 | Forced | Knockback, teleport, ragdoll |
| 90 | PlayerCommand | Ping move, attack commands |
| 80 | Animation | Root motion, attack animations |
| 70 | Combat | Combat AI movement |
| 60 | SubBehavior | Idle sub-behaviors (chores) |
| 50 | Following | Following owner |
| 40 | IdleWander | Idle wandering |
| 0 | None | No movement |

---

## Document Version

- **Version:** 1.0
- **Created:** 2024
- **Status:** Planning Phase - UnifiedMovementAuthority created
