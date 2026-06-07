# Companion Behavior Fixes - March 3, 2025

## Overview
This document tracks bug fixes made to address multiple companion behavior issues reported by the user.

---

## Issues Reported

1. **Log Spam** - SmelterOperator behavior flooding logs with decision info every frame
2. **Axe Search** - Companions not checking chests for axes when commanded to gather wood
3. **Busy Flag Not Resetting** - Companions staying "busy" after completing behaviors
4. **"No Available Companion"** - Move commands failing when companions are busy
5. **Fire Tending Not Adding Wood** - Fire tending behavior failing to add fuel to fires
6. **Fire Tending Command Not Starting** - TendFire command was looking for V1 behavior instead of V2
7. **Fire Tending CanStart Missing Logs** - No diagnostic info when fire tending can't start
8. **FIRE TENDING WRONG FLOW** - Companion goes to fire first, then can't find fuel
9. **WOOD GATHERING WRONG BEHAVIOR** - Using same class as ore mining, wrong tool check flow

---

## MAJOR FIX: Fire Tending Flow Corrected

### Problem
Fire tending was going:
1. Go to fire
2. Check for fuel
3. If no fuel, try to pull from chests (which aren't nearby anymore!)
4. Fail and complete

### Correct Flow (NOW IMPLEMENTED)
1. **Check inventory for fuel**
2. **If no fuel, check nearby chests**
3. **If fuel in chest, GO TO CHEST first**
4. **Take fuel from chest**
5. **THEN go to fire**
6. Add fuel
7. Complete

### Files Modified
- `FireTendingBehaviorV2.cs` - Completely rewritten with correct phase flow

### New Phase Enum
```csharp
public enum TendPhase
{
    CheckingResources,      // Check inventory and chests for fuel
    MovingToChest,          // Walking to chest to get fuel
    RetrievingFuel,         // Taking fuel from chest
    MovingToFire,           // Walking to fire
    AddingFuel,             // Adding fuel to fire
    CookingFood,
    CollectingFood,
    Complete
}
```

---

## MAJOR FIX: WoodGatheringBehavior Properly Implemented

### Original Problem
The initial `WoodGatheringBehavior` class was completely broken:
- Didn't equip axes
- Didn't play attack animations
- Didn't apply damage to trees
- Just timed out without doing anything

### Root Cause
The class was missing all the critical functionality:
- No `EquipBestAxe()` method
- No `PlayAxeSwingAnimation()` method  
- No `ApplyDamageDelayed()` coroutine
- No proper `AttackTree()` implementation

### Proper Fix Applied
Completely rewrote `WoodGatheringBehavior.cs` with ALL necessary functionality (~750 lines):

**1. Tool Management**
- `HasAxeAvailable()` - Checks inventory, equipment, AND nearby chests
- `HasAxeEquippedOrInInventory()` - Checks local sources only  
- `TryFindAxeInNearbyChests()` - Finds best axe in chests
- `TryPullAxeFromChest()` - Transfers axe from chest to companion
- `EquipBestAxe()` - Finds and equips highest tier axe
- `HolsterLeftHand()` - Holsters shields/bows before two-handed axe
- `IsAxe()` - Identifies axes (contains "axe" but not "pickaxe")

**2. Attack Logic**
- `AttackTree()` - Main attack: verifies axe, plays animation, schedules damage
- `ApplyDamageDelayed()` - Coroutine applying damage after 0.6s animation delay
- `PlayAxeSwingAnimation()` - Plays `swing_axe0/1/2` via ZSyncAnimation
- `RepositionAroundTarget()` - Moves to new angle when hits miss

**3. Tree Finding**
- `FindNearbyTree()` - Finds TreeBase, TreeLog, or stumps
- `FindNearbyStump()` - Finds Destructibles with "stub"/"stump"
- `FindAllNearbyLogs()` - Finds all logs after tree falls
- `IsStump()` - Checks if target is a stump

**4. Sapling Spawning (70% chance)**
- `OnStumpDestroyed()` - Triggers sapling spawn on stump clear
- `DetermineSaplingPrefab()` - Maps stump to sapling (Beech, Birch, etc.)
- `SpawnSapling()` - Spawns at ground level

**5. Phase Flow**
```
FindingTree ? MovingToChestForTool ? RetrievingToolFromChest 
? MovingToTree ? ChoppingTree ? WaitingForLogs ? ProcessingLogs 
? WaitingForDrops ? Complete
```

### Files Modified
- `WoodGatheringBehavior.cs` - Complete rewrite with proper implementation
- `CompanionCommandSystem.cs` - Uses WoodGatheringBehavior for trees
- `CompanionIdleBehavior.SubBehaviors.cs` - Registers WoodGatheringBehavior

### Testing Checklist
- [ ] Give companion an axe, command to chop tree ? should equip and chop
- [ ] No axe in inventory, axe in chest ? walk to chest, get axe, then chop
- [ ] No axe anywhere ? say "I need an axe" and not start
- [ ] Tree falls ? should find and process logs
- [ ] Stump destroyed ? 70% chance sapling spawns
- [ ] Verify `swing_axe0/1/2` animations play correctly

---

## Fix #1: SmelterOperator Log Spam

### Problem
The kiln operation was spamming logs with decision info every frame:
```
[SmelterOperator] Grid the Tremendous UpdateFillingStation decision:
[SmelterOperator]   Smelter: fuel=0.0/0, ore=0/50
[SmelterOperator]   Inventory: HasOre=True, HasFuel=True
[SmelterOperator]   Chests: HasOreInChests=True, HasFuelInChests=True, ChestCount=3
[SmelterOperator]   Decisions: NeedsFuel=False, CanAddOre=True
[SmelterOperator] Grid the Tremendous -> Adding ore (have ore in inventory)
```
This repeated hundreds of times per second.

### Root Cause
The logging condition was:
```csharp
bool shouldLog = CompanionIdleBehavior.VerboseLogging || (Time.time - _phaseStartTime < 0.1f);
```
This logged when EITHER verbose logging was on OR within 0.1s of phase start.

### Fix Applied
**File:** `SmelterOperatorBehavior.StationPhases.cs`

Changed to only log when BOTH conditions are true:
```csharp
// RATE-LIMITED LOGGING: Only log at phase start (first 0.1s) to prevent spam
// The filling station phase runs every frame, so we can't log every time
bool shouldLog = CompanionIdleBehavior.VerboseLogging && (Time.time - _phaseStartTime < 0.1f);
```

Also rate-limited the "Adding ore/fuel" logs to use the same `shouldLog` condition.

### Testing
- [ ] Enable VerboseLogging and command companion to operate kiln
- [ ] Verify logs only appear briefly at phase start, not every frame
- [ ] Verify kiln operation still works correctly

---

## Fix #2: TryAddOre Silently Failing (Kiln Stuck Loop)

### Problem
The companion was stuck in a loop trying to add ore/wood to the kiln but silently failing. The logs showed:
- `HasOre=True` (inventory has wood)
- `CanAddOre=True` (kiln can accept more)
- But nothing was being added

### Root Cause
The `TryAddOre()` method was using exact string matching for ore names:
```csharp
if (dropName.Equals(oreName, System.StringComparison.OrdinalIgnoreCase))
```
This could fail if the prefab names didn't match exactly.

### Fix Applied
**File:** `SmelterOperatorBehavior.Helpers.cs`

1. Added more flexible ore name matching (case-insensitive AND partial matching):
```csharp
bool matches = dropName.Equals(oreName, System.StringComparison.OrdinalIgnoreCase) ||
               dropName.ToLowerInvariant().Contains(oreName.ToLowerInvariant());
```

2. Added detailed diagnostic logging when ore matching fails:
```csharp
if (CompanionIdleBehavior.VerboseLogging)
{
    Debug.Log($"[SmelterOperator] TryAddOre: No matching ore found. Needed: [{string.Join(", ", neededOres)}]");
    Debug.Log($"[SmelterOperator]   Inventory has {allItems.Count} items:");
    foreach (var item in allItems)
    {
        if (item != null)
            Debug.Log($"[SmelterOperator]     - {item.m_dropPrefab?.name ?? "?"} x{item.m_stack}");
    }
}
```

3. Added logging when station is full to prevent spam:
```csharp
if (CompanionIdleBehavior.VerboseLogging)
    Debug.Log($"[SmelterOperator] TryAddOre: Station FULL ({queued}/{_targetSmelter.m_maxOre})");
```

### Testing
- [ ] Command companion to operate kiln with wood in inventory
- [ ] Verify wood is successfully added to kiln
- [ ] If it fails, check logs for diagnostic info about what items are in inventory vs what's needed

---

## Fix #3: Resource Gathering Not Checking Chests for Axes

### Problem
When commanded to gather wood, companions would fail if they didn't have an axe in their inventory, even if there was an axe in a nearby chest.

Log output:
```
[ResourceGathering] Asny the Delver HasToolEquippedOrInInventory: checking for Axe tier>=0 to gather Beech Tree
[ResourceGathering]   NO appropriate tool found in inventory/equipped
[Warning] [ResourceGathering] Asny the Delver cannot gather Beech Tree - no Axe available
```

### Root Cause
The `HasToolEquippedOrInInventory()` method only checked:
1. Currently equipped weapon
2. Equipment slots (RightHand, RightBack, LeftBack)
3. Storage inventory

It did NOT check nearby chests, even though the behavior has the ability to pull tools from chests.

### Fix Applied
**File:** `ResourceGatheringBehavior.Tools.cs`

Added chest checking at the end of `HasToolEquippedOrInInventory()`:
```csharp
// CRITICAL FIX: Also check nearby chests - we can pull tools from there!
// This allows the behavior to start if there's a tool available ANYWHERE
if (TryFindToolInNearbyChests(resource))
{
    if (shouldLog) Debug.Log($"[ResourceGathering]   FOUND tool in nearby chest - will pull before gathering");
    return true;
}
```

Also reduced log spam by changing from always-log to verbose-only:
```csharp
bool shouldLog = VerboseLogging || CompanionIdleBehavior.VerboseLogging;
```

### Testing
- [ ] Remove axe from companion inventory
- [ ] Place axe in nearby chest
- [ ] Command companion to gather wood from tree
- [ ] Verify companion walks to chest, pulls axe, then gathers wood
- [ ] Verify behavior fails gracefully if no axe anywhere

---

## Fix #4: Move Command "No Available Companion"

### Problem
Move commands (Shift+Middle Click on ground) were failing with "No available companion" when all companions were busy with other tasks.

### Root Cause
The `SelectBestCompanionForCommand()` was called with `allowInterrupt: false`:
```csharp
var selectedCompanion = coordinator.SelectBestCompanionForCommand(
    player,
    target.Value.targetObj,
    target.Value.position,
    allowInterrupt: false);  // <-- Would not select busy companions!
```

Move commands should have ABSOLUTE priority and should be able to interrupt any other activity.

### Fix Applied
**File:** `CompanionCommandSystem.cs`

Changed to pass `allowInterrupt: true` for Move commands:
```csharp
// CRITICAL: Move commands should ALWAYS work - they have absolute priority
// Use allowInterrupt=true for move commands so they can interrupt busy companions
bool isMoveCommand = target.Value.type == CommandType.MoveToPosition;

var selectedCompanion = coordinator.SelectBestCompanionForCommand(
    player,
    target.Value.targetObj,
    target.Value.position,
    allowInterrupt: isMoveCommand); // Move commands can interrupt
```

**File:** `CompanionCommandCoordinator.cs`

Added better logging to diagnose selection failures:
```csharp
if (selected == null && VerboseLogging)
{
    Debug.Log($"[CompanionCommandCoordinator] Could not select companion: " +
        $"{companions.Count} available, bestAvailable={bestAvailable?.companionName ?? "none"}, " +
        $"bestBusy={bestBusy?.companionName ?? "none"}, allowInterrupt={allowInterrupt}");
}
```

### Testing
- [ ] Have companion busy with a sub-behavior (e.g., operating kiln)
- [ ] Issue a Move command (Shift+Middle Click on ground)
- [ ] Verify companion stops current task and moves to position
- [ ] Verify "No available companion" message no longer appears

---

## Fix #5: Fire Tending Not Adding Wood

### Problem
Fire tending behavior would find fires that needed fuel, walk to them, but then fail to actually add wood.

### Root Cause
The `TryAddFuel()` method was searching for fuel by DISPLAY name instead of PREFAB name:
```csharp
// BROKEN - Uses display name like "$item_wood"
string fuelItemName = _targetFireplace.m_fuelItem.m_itemData.m_shared.m_name;
ItemDrop.ItemData fuelItem = storage.GetItem(fuelItemName);
```

Valheim's `Inventory.GetItem()` uses display names, but the RPC expects prefab names.

### Fix Applied
**File:** `FireTendingBehaviorV2.cs`

Changed `TryAddFuel()` to search by prefab name:
```csharp
// CRITICAL FIX: Use the PREFAB name to find fuel, not the display name
// m_fuelItem.gameObject.name gives the prefab name (e.g., "Wood")
// m_itemData.m_shared.m_name gives the display name (e.g., "$item_wood")
string fuelPrefabName = _targetFireplace.m_fuelItem.gameObject.name;

// Find fuel item in storage by prefab name
ItemDrop.ItemData fuelItem = null;
foreach (var item in storage.GetAllItems())
{
    if (item == null) continue;
    string itemPrefab = item.m_dropPrefab?.name ?? "";
    if (itemPrefab.Equals(fuelPrefabName, System.StringComparison.OrdinalIgnoreCase))
    {
        fuelItem = item;
        break;
    }
}
```

Also fixed to remove item from inventory BEFORE calling RPC (prevents duplication):
```csharp
// CRITICAL: Remove from inventory FIRST, then invoke RPC
// This ensures we don't add fuel without consuming the item
if (!storage.RemoveOneItem(fuelItem))
{
    LogVerbose($"TryAddFuel: Failed to remove fuel from inventory");
    return false;
}

nview.InvokeRPC("RPC_AddFuel");
SaveInventory();
```

Also updated `HasFuelItem()` to use the same prefab name matching:
```csharp
// Check by prefab name, not display name
foreach (var item in storage.GetAllItems())
{
    if (item == null) continue;
    string itemPrefab = item.m_dropPrefab?.name ?? "";
    if (itemPrefab.Equals(fuelPrefabName, System.StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }
}
```

### Testing
- [ ] Place wood in companion inventory
- [ ] Have a campfire/hearth that needs fuel (< 70% capacity)
- [ ] Set companion to stay mode near fire
- [ ] Verify companion walks to fire and adds wood
- [ ] Verify fire's fuel level increases

---

## Fix #6: Companion Busy Flag Not Resetting

### Problem
After a behavior completed, companions could still be reported as "busy" by `IsCompanionBusy()`, which prevented them from being selected for new commands.

### Root Cause
The `IsInSubBehavior` property only checked if `_activeSubBehavior != null`:
```csharp
public bool IsInSubBehavior => _activeSubBehavior != null;
```

If a behavior completed but the reference wasn't immediately cleared (race condition), the companion would still appear busy.

### Fix Applied
**File:** `CompanionIdleBehavior.cs`

Changed to also check `IsActive` flag:
```csharp
/// <summary>
/// Returns true if companion is currently executing a sub-behavior.
/// CRITICAL: This is the SINGLE SOURCE OF TRUTH for sub-behavior status.
/// Used by CompanionCommandCoordinator.IsCompanionBusy() to check if companion is available.
/// 
/// IMPORTANT: Both _activeSubBehavior != null AND IsActive must be true.
/// This ensures we don't report busy if the behavior finished but wasn't cleared yet.
/// </summary>
public bool IsInSubBehavior => _activeSubBehavior != null && _activeSubBehavior.IsActive;
```

The `IsActive` flag is set to `false` in both `Cancel()` and `Complete()` methods of behaviors, so this ensures the check is accurate.

### Testing
- [ ] Have companion complete a sub-behavior (e.g., deposit items)
- [ ] Immediately issue a new command
- [ ] Verify companion responds to new command without "no available companion" error

---

## Fix #7: Fire Tending Command Using Wrong Behavior Version

### Problem
When you command a companion to tend a fire, the command system was looking for `FireTendingBehavior` (V1), but only `FireTendingBehaviorV2` is registered. The logs showed:
```
[COMMAND] Issued TendFire command to Asny the Delver at (586.73, 40.15, 314.81)
[MovementAuthority] Asny the Delver CompanionAI ACQUIRED Following authority
```
The companion immediately went back to following instead of tending the fire.

### Root Cause
`ExecuteFireTendCommand()` was calling `GetSubBehavior<FireTendingBehavior>()` but only `FireTendingBehaviorV2` is registered in `CompanionIdleBehavior.SubBehaviors.cs`.

### Fix Applied
**File:** `CompanionCommandSystem.cs`

Changed to try V2 behavior first:
```csharp
private void ExecuteFireTendCommand(CompanionController companion, ActiveCommand command)
{
    Debug.Log($"[COMMAND] ExecuteFireTendCommand called for {companion.companionName}");
    
    // Cancel current activities
    var idleBehavior = companion.GetComponent<CompanionIdleBehavior>();
    idleBehavior?.OnCombatStarted();

    var interactionBehavior = companion.GetComponent<CompanionInteractionBehavior>();
    interactionBehavior?.ForceDetach();

    // CRITICAL: Try V2 behavior first (the active one), then fall back to V1
    var fireBehaviorV2 = idleBehavior?.GetSubBehavior<FireTendingBehaviorV2>();
    if (fireBehaviorV2 != null)
    {
        Debug.Log($"[COMMAND] {companion.companionName} found FireTendingBehaviorV2");
        fireBehaviorV2.SetCommandedTarget(command.TargetObject);
        
        if (idleBehavior.TryStartSubBehavior<FireTendingBehaviorV2>())
        {
            Debug.Log($"[COMMAND] {companion.companionName} started FireTendingBehaviorV2 successfully");
            ShowMessage($"{companion.companionName}: Tending fire");
            return;
        }
        else
        {
            Debug.LogWarning($"[COMMAND] {companion.companionName} FireTendingBehaviorV2.CanStart() returned false");
        }
    }
    
    // Try V1 behavior as fallback
    var fireBehavior = idleBehavior?.GetSubBehavior<FireTendingBehavior>();
    if (fireBehavior != null)
    {
        Debug.Log($"[COMMAND] {companion.companionName} trying fallback FireTendingBehavior (V1)");
        fireBehavior.SetCommandedTarget(command.TargetObject);
        
        if (idleBehavior.TryStartSubBehavior<FireTendingBehavior>())
        {
            ShowMessage($"{companion.companionName}: Tending fire");
            return;
        }
    }
    
    // Fallback: move to fire
    Debug.Log($"[COMMAND] {companion.companionName} fire tending behaviors unavailable, falling back to move command");
    ExecuteMoveCommand(companion, command);
}
```

### Testing
- [ ] Command a following companion to tend a fire (Shift+Middle Click on fire)
- [ ] Verify logs show "found FireTendingBehaviorV2" and "started FireTendingBehaviorV2 successfully"
- [ ] Verify companion walks to fire and adds fuel

---

## Fix #8: Fire Tending CanStart() Missing Diagnostics

### Problem
When `CanStart()` returned false, there was no logging to explain why, making debugging difficult.

### Root Cause
The `CanStart()` method had no verbose logging.

### Fix Applied
**File:** `FireTendingBehaviorV2.cs`

Added verbose logging to all paths in `CanStart()`:
```csharp
public override bool CanStart()
{
    if (Companion == null) return false;
    
    // If commanded to a fire, always allow - we'll move to it and figure out fuel later
    if (_commandedTarget != null) 
    {
        LogVerbose($"CanStart: TRUE - has commanded target");
        return true;
    }
    
    // Check for nearby fires that need tending
    var fireplace = FindNearbyFireplace();
    if (fireplace != null && NeedsFuel(fireplace))
    {
        // Check if we have fuel OR there's fuel in nearby chests
        if (HasFuelItem())
        {
            LogVerbose($"CanStart: TRUE - found fireplace needing fuel, have fuel in inventory");
            return true;
        }
        
        if (Resources.HasAnyItemInChests(ChestHelper.FuelItems))
        {
            LogVerbose($"CanStart: TRUE - found fireplace needing fuel, have fuel in chests");
            return true;
        }
        
        LogVerbose($"CanStart: FALSE - found fireplace but no fuel available");
    }
    
    var cookingStation = FindNearbyCookingStation();
    if (cookingStation != null && HasFoodToCook()) 
    {
        LogVerbose($"CanStart: TRUE - found cooking station and have food");
        return true;
    }
    
    LogVerbose($"CanStart: FALSE - no fire/cooking station found or no resources");
    return false;
}
```

### Testing
- [ ] Enable VerboseLogging
- [ ] Command companion to tend fire
- [ ] Check logs for "CanStart:" messages explaining decision

---

## Fix #9: Chest Search Radius Too Small (15m ? 50m)

### Problem
Both Fire Tending and Resource Gathering behaviors were failing because they couldn't find chests. The default `ChestSearchRadius` was only 15 meters, which is too small for companions who are following the player and get commanded to do tasks.

### Root Cause
In `CompanionSettings.cs`:
```csharp
private const float DEFAULT_CHEST_SEARCH_RADIUS = 15f;  // TOO SMALL!
private const float MAX_CHEST_RADIUS = 50f;
```

When a following companion is commanded to tend a fire or gather resources, they search for chests from their current position. If they're at the fire (which is often in the center of a base), 15m wasn't enough to reach chests that might be 20-30m away at the edges.

### Fix Applied
**File:** `CompanionSettings.cs`

Increased default and max chest search radius:
```csharp
private const float DEFAULT_CHEST_SEARCH_RADIUS = 50f;  // INCREASED: 50m default
private const float MAX_CHEST_RADIUS = 100f;  // INCREASED: Allow larger for commanded behaviors
```

### Impact
- All companion behaviors that search for chests now search 50m by default
- ResourceGathering will find axes in chests up to 50m away
- FireTending will find wood in chests up to 50m away
- SmelterOperator will find materials in chests up to 50m away

### Testing
- [ ] Command a following companion to tend fire (Shift+Middle Click on fire)
- [ ] Verify logs show "Found X chests within 50m"
- [ ] Verify companion finds wood in chests and adds it to fire
- [ ] Command a following companion to gather wood (no axe in inventory)
- [ ] Verify companion finds axe in nearby chest

---

## Fix #10: Fire Tending Refreshes Chests On Arrival

### Problem
Fire tending would search for chests at the START of the behavior (when companion is far from fire), find no chests, and then immediately complete without doing work.

### Root Cause
The `Resources.RefreshNearbyChests()` was only called at the start of the behavior. For following companions, this searched from their starting position (near the player), not near the fire.

### Fix Applied
**File:** `FireTendingBehaviorV2.cs`

Added chest refresh when arriving at the fire:
```csharp
private bool UpdateMovingToFire()
{
    if (ContinueMovement())
    {
        // Arrived at destination
        StopMovement();
        
        // CRITICAL: Refresh nearby chests now that we've arrived at the fire
        // This ensures we search with the 50m radius from our current position (near the fire)
        Resources.RefreshNearbyChests(true);
        LogVerbose($"Arrived at fire, refreshed chests. Found {Resources.ChestCount} chests within {CompanionSettings.ChestSearchRadius}m.");
        
        // ... rest of arrival logic ...
    }
}
```

Also added detailed logging when deciding next phase:
```csharp
bool hasFuelInInventory = HasFuelItem();
bool hasFuelInChests = Resources.HasAnyItemInChests(ChestHelper.FuelItems);

LogVerbose($"Fire needs fuel. HasFuelInInventory={hasFuelInInventory}, HasFuelInChests={hasFuelInChests}, ChestCount={Resources.ChestCount}");
```

And added player notification when no fuel is available:
```csharp
private void NotifyNoFuelAvailable()
{
    var owner = Companion?.GetOwner();
    if (owner != null && owner == Player.m_localPlayer)
    {
        MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, 
            $"{Companion.GetDisplayName()}: No wood available to tend fire");
    }
}
```

### Testing
- [ ] Command following companion to tend fire
- [ ] Verify logs show "Arrived at fire, refreshed chests. Found X chests"
- [ ] Verify companion either adds fuel OR shows "No wood available" message

---

## Files Modified Summary

| File | Changes |
|------|---------|
| `SmelterOperatorBehavior.StationPhases.cs` | Rate-limited logging, fixed shouldLog condition |
| `SmelterOperatorBehavior.Helpers.cs` | Flexible ore matching, diagnostic logging |
| `ResourceGatheringBehavior.Tools.cs` | Check chests for tools, reduced log spam |
| `CompanionCommandSystem.cs` | Move commands use allowInterrupt=true, fire tending uses V2 behavior |
| `CompanionCommandCoordinator.cs` | Better logging for selection failures |
| `FireTendingBehaviorV2.cs` | Use prefab name for fuel, diagnostic logging, refresh chests on arrival |
| `CompanionIdleBehavior.cs` | IsInSubBehavior checks IsActive flag |
| `CompanionSettings.cs` | **ChestSearchRadius: 15m ? 50m**, MaxChestRadius: 50m ? 100m |
| `ResourceAccessService.cs` | Added position-specific RefreshNearbyChests overload |

---

## Testing Checklist

### High Priority
- [ ] **Chest Search Radius** - Verify logs show "Found X chests within 50m"
- [ ] **Fire Tending Command** - Should find wood in chests and add to fire
- [ ] **Resource Gathering** - Should find axes in chests

### Medium Priority
- [ ] **Move Command** - Should ALWAYS work, even with busy companions
- [ ] **Log Spam** - Should be dramatically reduced

### Low Priority (Verification)
- [ ] **Behavior Completion** - Companions should be available after tasks complete

---

## How to Test

1. **Enable VerboseLogging** (optional, for detailed diagnostics):
   ```
   CompanionIdleBehavior.VerboseLogging = true;
   ```

2. **Test Fire Tending (CRITICAL)**:
   - Have a following companion (not stayed)
   - Make sure companion does NOT have wood in inventory
   - Place wood in a chest near a campfire
   - Let campfire burn down to < 70% fuel
   - Shift+Middle Click on campfire to command fire tending
   - **Expected logs:**
     - `[COMMAND] X found FireTendingBehaviorV2`
     - `[COMMAND] X started FireTendingBehaviorV2 successfully`
     - `[FireTending] Arrived at fire, refreshed chests. Found X chests within 50m`
     - `[FireTending] Fire needs fuel. HasFuelInInventory=False, HasFuelInChests=True, ChestCount=X`
   - Verify companion walks to fire, pulls wood from chest, adds to fire

3. **Test Resource Gathering with Chest Tools (CRITICAL)**:
   - Remove all axes from companion inventory
   - Put axe in chest near a tree
   - Command companion to gather tree (Shift+Middle Click)
   - **Expected logs:**
     - `[ResourceGathering] TryFindToolInNearbyChests: searching X chests for Axe`
     - `[ResourceGathering] TryFindToolInNearbyChests: FOUND X (tier Y) in chest`
   - Verify companion walks to chest, pulls axe, then gathers tree

4. **Test Move Commands**:
   - Have companion operating kiln or doing any task
   - Shift+Middle Click on ground
   - Companion should stop and move immediately

---

## Rollback Instructions

If issues occur, revert these files to their previous state:
- `SmelterOperatorBehavior.StationPhases.cs`
- `SmelterOperatorBehavior.Helpers.cs`
- `ResourceGatheringBehavior.Tools.cs`
- `CompanionCommandSystem.cs`
- `CompanionCommandCoordinator.cs`
- `FireTendingBehaviorV2.cs`
- `CompanionIdleBehavior.cs`
- `CompanionSettings.cs`
- `ResourceAccessService.cs`

---

## Notes

- All changes compile successfully
- **Key change: ChestSearchRadius increased from 15m to 50m**
- No new dependencies added
- Changes are backwards compatible
- Build verified: ?

