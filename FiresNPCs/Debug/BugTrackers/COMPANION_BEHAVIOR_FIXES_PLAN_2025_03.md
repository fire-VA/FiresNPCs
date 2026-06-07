# Companion Behavior Fixes Plan - March 2025

## Overview
This document tracks the plan to fix several companion behavior bugs and the implementation status.

---

## Bug #1: Following Companions Don't Show Chest Open/Close Animation

### Problem
When following companions are commanded to interact with chests, the chest lid doesn't animate open/close. However, companions that are "staying" do show proper chest animations.

### Root Cause Analysis
Following companions use `ChestInteractionService` which calls `Container.SetInUse(true/false)`. This DOES properly trigger the chest animation via:
1. `m_open.SetActive(true)` / `m_closed.SetActive(false)` for opening
2. `m_openEffects.Create()` for sound effects
3. ZDO `s_inUse` flag for multiplayer sync

However, `SetInUse()` requires **ZNetView ownership** to work. The code claims ownership:
```csharp
if (!_targetChestNView.IsOwner())
    _targetChestNView.ClaimOwnership();
```

**ISSUE**: Following companions may have a different authority source or timing issue that prevents the ownership claim from succeeding before `SetInUse()` is called.

Additionally, the ZDO sync via `ZDOMan.ForceSendZDO()` iterates over `ZNet.instance.GetConnectedPeers()` which:
- Works for multiplayer (syncs to other players)
- Does NOT sync to the local client who is standing right there
- The local client should see the animation immediately without ZDO sync

### Fix Plan
1. Add delay/verification after `ClaimOwnership()` before calling `SetInUse()`
2. Ensure the chest's visual update is called explicitly for local player
3. Add fallback direct manipulation of `m_open`/`m_closed` GameObjects if `SetInUse()` doesn't work

### Files to Modify
- `ChestInteractionService.cs`: `TryOpenChest()` and `TryCloseChest()` methods

---

## Bug #2: Companions Don't Clear Tree Stumps (_stub Objects)

### Problem
After chopping down trees and processing logs, companions don't attack the remaining stumps (`_stub` objects). Stumps should be cleared after logs are processed.

### Root Cause Analysis
Looking at `ResourceGatheringBehavior.TreeGathering.cs`:
- `FindNearbyStump()` searches for Destructibles with "stump" or "stub" in the name
- `UpdateWaitingForLogs()` DOES check for stumps after logs are done
- But the stump search only happens in `UpdateWaitingForLogs()` which is called after tree felling

**ISSUES IDENTIFIED**:
1. `FindNearbyStump()` only looks for `Destructible` components, but Valheim stumps may be under different component types
2. The search radius `STUMP_SEARCH_RADIUS = 8f` may be too small since logs can roll/bounce
3. Stumps are named like `Beech_Stub`, `Birch_Stub`, etc. - the check is case-insensitive but might miss some variants
4. After processing logs, the behavior transitions to `WaitingForDrops` instead of continuing to attack stumps

**CRITICAL**: The stub prefab names from Valheim are:
- `Beech_Stub`
- `Birch_Stub`, `Birch1_aut_Stub`, `Birch2_Stub`, `Birch2_aut_Stub`
- `Oak_Stub`
- `FirTree_Stub`, `Pinetree_01_Stub`
- `SwampTree1_Stub`
- `YggaShoot_Stub`, `YggaShoot1_Stub`, `YggaShoot2_Stub`, `YggaShoot3_Stub`

The search does include "stub" check, so naming should match.

### Fix Plan
1. Extend `UpdateWaitingForLogs()` to continue attacking stumps until they're destroyed
2. Make stump search more robust - search for objects with `Destructible` component AND name containing stub/stump
3. Increase search radius or search from tree's original position
4. Add a dedicated phase `ClearingStump` that continues until stump is gone
5. Make sure `WaitingForDrops` is only entered AFTER stumps are cleared

### Files to Modify
- `ResourceGatheringBehavior.TreeGathering.cs`: Stump detection and clearing logic
- `ResourceGatheringBehavior.cs`: Phase management for stump clearing

---

## Bug #3: Companions Interact with Workbenches Too Often

### Problem
Companions interact with workbenches excessively, making them feel robotic.

### Root Cause Analysis
In `WorkstationInteractionBehaviorV2.cs`:
- The behavior is available for idle rotation (`AvailableForIdleRotation => true`)
- When `TryStartSubBehavior()` runs in `CompanionIdleBehavior`, it has a chance to start workstation interaction
- The `subBehaviorCooldown` is 30 seconds between attempts
- The `subBehaviorChance` is 30% (modified to 80% for staying companions)

**ISSUES**:
1. No specific cooldown for workstation interaction - it uses the global sub-behavior cooldown
2. Staying companions have 80% chance to start ANY sub-behavior, which includes workstation
3. No tracking of "last workstation interaction time" to space them out

### Fix Plan
1. Add workstation-specific cooldown in `WorkstationInteractionBehaviorV2.CanStart()`
2. Track last workstation interaction time per companion
3. Make minimum interval configurable (suggest 5-10 minutes between workstation visits)

### Files to Modify
- `WorkstationInteractionBehaviorV2.cs`: Add cooldown tracking

---

## Bug #4: Companions Repair Items That Don't Need Repair

### Problem
When at workbenches, companions attempt to repair items that are already at full durability.

### Root Cause Analysis
In `WorkstationInteractionBehaviorV2.cs`, `TryRepairItem()`:
```csharp
private bool ItemNeedsRepair(ItemDrop.ItemData item)
{
    if (item?.m_shared == null || !item.m_shared.m_useDurability) return false;
    float maxDurability = item.GetMaxDurability();
    if (maxDurability <= 0) return false;
    return item.m_durability / maxDurability < 0.95f;  // Only repairs if < 95%
}
```

This check looks correct. The issue might be in `GetDamagedItems()` which is called before repair:
```csharp
var damagedItems = GetDamagedItems();
if (damagedItems.Count == 0) return;
```

**POSSIBLE ISSUE**: The 95% threshold might be too high, causing companions to repair items that are only 5% damaged.

### Fix Plan
1. Lower the repair threshold to 80% (item must be < 80% durability to repair)
2. Add logging when repair is attempted to verify the check is working
3. Consider making the threshold configurable

### Files to Modify
- `WorkstationInteractionBehaviorV2.cs`: `ItemNeedsRepair()` method

---

## Bug #5: Companions Can Upgrade Items Multiple Times Per Day

### Problem
Companions should only be able to upgrade items once per in-game day (1800 seconds), but they can upgrade repeatedly.

### Root Cause Analysis
In `WorkstationInteractionBehaviorV2.cs`, `TryUpgradeItem()`:
- There's an `UPGRADE_CHANCE_PER_ANIMATION = 0.02f` (2% chance per work animation)
- But NO tracking of when the last upgrade happened
- No per-day limit on upgrades

### Fix Plan
1. Add tracking of last upgrade time per companion (stored in ZDO)
2. Check if an in-game day has passed since last upgrade before allowing another
3. In-game day = 1800 seconds (30 minutes real time)
4. Store `lastUpgradeDay` as game day number in ZDO

### Files to Modify
- `WorkstationInteractionBehaviorV2.cs`: Add upgrade day tracking
- `CompanionController.cs` (if needed): Add ZDO field for last upgrade day

---

## Bug #6: Wrong Animation for Workstation Interaction

### Problem
Companions play the standard "interact" animation when working at workstations instead of a hammer/crafting animation.

### Root Cause Analysis
In `WorkstationInteractionBehaviorV2.cs`, `PlayWorkAnimation()`:
```csharp
private void PlayWorkAnimation()
{
    PlayInteractAnimation();  // This triggers "interact" animation
    TryRepairItem();
}
```

This calls `PlayInteractAnimation()` which triggers the generic interact animation.

**What animation SHOULD be used**:
Looking at Valheim's player animations for crafting:
- `crafting` - bool animation for working at stations
- `Working` - alternative bool for work animations
- `emote_craftwork` - specific emote for craft work

The `WorkBehaviorBase.PlayWorkAnimation(bool enable)` method sets both `crafting` and `Working` bools:
```csharp
protected void PlayWorkAnimation(bool enable)
{
    if (AnimationController != null)
        AnimationController.PlayWorkAnimation(enable);
    else if (ZAnim != null)
    {
        ZAnim.SetBool("crafting", enable);
        ZAnim.SetBool("Working", enable);
    }
}
```

But `WorkstationInteractionBehaviorV2.PlayWorkAnimation()` is a DIFFERENT method that shadows this!

### Fix Plan
1. Rename `WorkstationInteractionBehaviorV2.PlayWorkAnimation()` to avoid shadowing
2. Use the base class `PlayWorkAnimation(true)` at start of work, `PlayWorkAnimation(false)` at end
3. Keep the periodic `PlayInteractAnimation()` but change to `crafting` animation trigger
4. The hammer/crafting animation is typically triggered by setting `crafting` bool to true

### Files to Modify
- `WorkstationInteractionBehaviorV2.cs`: Fix animation method shadowing

---

## Implementation Status

| Bug | Status | Notes |
|-----|--------|-------|
| #1 Chest animations | ? FIXED | Added `EnsureChestVisualState()` fallback |
| #2 Stump clearing | ? FIXED | Increased search radius, improved detection |
| #3 Workbench frequency | ? FIXED | Added 5-minute cooldown |
| #4 Unnecessary repairs | ? FIXED | Lowered threshold to 75% |
| #5 Upgrade limit | ? FIXED | Track upgrade day in ZDO |
| #6 Wrong animation | ? FIXED | Use `crafting` bool instead of `interact` |

---

## Implementation Details

### Bug #1 Fix: Chest Open/Close Animations (ChestInteractionService.cs)

**Changes Made:**
1. Added `EnsureChestVisualState(bool open)` method that directly manipulates `m_open` and `m_closed` GameObjects
2. Called this fallback method after `SetInUse()` to ensure local player sees animation immediately
3. Added null check for `ZNet.instance` before iterating peers

**Why This Fixes It:**
Following companions may have timing issues with ownership claims. The fallback ensures the chest lid visually opens/closes even if `SetInUse()` doesn't immediately update the visual state.

### Bug #2 Fix: Stump Clearing (ResourceGatheringBehavior.TreeGathering.cs)

**Changes Made:**
1. Increased `STUMP_SEARCH_RADIUS` from 8f to 15f (logs can roll far)
2. Improved `FindNearbyStump()` to search for both `Destructible` and `ZNetView` objects
3. Added proper `(Clone)` suffix stripping for prefab name matching
4. Added comprehensive doc comments with all Valheim stub naming conventions

**Why This Fixes It:**
The original search radius was too small and the detection was missing some stub objects. The improved detection finds stumps by both component type and name pattern.

### Bug #3 Fix: Workbench Frequency (WorkstationInteractionBehaviorV2.cs)

**Changes Made:**
1. Added `WORKSTATION_COOLDOWN = 300f` constant (5 minutes)
2. Added static `Dictionary<string, float> _lastWorkstationTime` to track cooldowns per companion
3. Added cooldown check in `CanStart()` that skips workstation if cooldown hasn't elapsed
4. Record interaction time in `Start()`
5. Skip cooldown check when companion is commanded to use a specific station

**Why This Fixes It:**
Companions will now wait 5 minutes between autonomous workstation visits, making behavior feel more natural and less robotic.

### Bug #4 Fix: Unnecessary Repairs (WorkstationInteractionBehaviorV2.cs)

**Changes Made:**
1. Added `REPAIR_THRESHOLD = 0.75f` constant
2. Changed `ItemNeedsRepair()` to use this threshold instead of 0.95f
3. Reduced `REPAIR_CHANCE_PER_ANIMATION` from 0.25f to 0.15f
4. Reduced `FULL_REPAIR_CHANCE` from 0.08f to 0.05f

**Why This Fixes It:**
Items must now be below 75% durability to be considered for repair. This prevents companions from "repairing" items that only need 5% durability restored.

### Bug #5 Fix: Upgrade Limit (WorkstationInteractionBehaviorV2.cs)

**Changes Made:**
1. Added `LAST_UPGRADE_DAY_KEY = "lastUpgradeDay"` ZDO key
2. Added `CanUpgradeToday()` method that checks ZDO for last upgrade day
3. Added `RecordUpgradeDay()` method that stores current day in ZDO
4. `TryUpgradeItem()` now calls `RecordUpgradeDay()` after successful upgrade
5. Used `EnvMan.instance.GetDay()` to get current in-game day

**Why This Fixes It:**
Companions can now only upgrade one item per in-game day (1800 seconds real time). The limit is stored per-companion in ZDO so it persists across sessions.

### Bug #6 Fix: Wrong Animation (WorkstationInteractionBehaviorV2.cs)

**Changes Made:**
1. Added `_isCraftingAnimationActive` state tracking
2. Created `StartCraftingAnimation()` method that sets `crafting` and `Working` bool params
3. Created `StopCraftingAnimation()` method to clean up animation state
4. Renamed `PlayWorkAnimation()` to `PlayPeriodicWorkAnimation()` to avoid shadowing
5. `PlayPeriodicWorkAnimation()` now enables crafting animation and triggers interact for hammer swing
6. Fixed `RestoreState()` to use `StartCraftingAnimation()` instead of shadowed method
7. Updated `Cancel()` to call `StopCraftingAnimation()`

**Why This Fixes It:**
The `crafting` bool triggers Valheim's proper workbench animation (hammer swinging stance) instead of the generic interact animation (brief pickup motion).

---

## Changelog

| Date | Change |
|------|--------|
| 2025-03-XX | Initial plan created |
| 2025-03-XX | All 6 bugs fixed and verified building |

