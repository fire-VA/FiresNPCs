# Farming Behavior Implementation Plan

## Overview
This document tracks the implementation of a new `FarmingBehavior` for companions that handles:
1. **Beehive Harvesting** - Check area for beehives with honey, harvest, store in chests
2. **Crop Harvesting** - Harvest mature Pickable crops (carrots, turnips, etc.)
3. **Crop Planting** - Plant seeds on cultivated ground
4. **Tool Finding Fix** - Ensure companions check chests within 50m for required tools

## Current Issues

### Tool Finding Bug
**Problem**: Companions commanded to gather resources (mining ore) are not properly checking chests within 50m radius for pickaxes/axes.

**Location**: `ResourceGatheringBehavior.Tools.cs`

**Root Cause Analysis Needed**:
- [ ] Verify `CHEST_SEARCH_RADIUS` is set to 50m
- [ ] Check if `FindToolInChests()` is being called
- [ ] Verify chest discovery is working (`ChestHelper.FindNearbyChests`)
- [ ] Check if tool prefab name matching is correct

---

## New Behavior: FarmingBehavior

### Feature Requirements

#### 1. Beehive Harvesting
- Detect `Beehive` components within work radius
- Check `GetHoneyLevel() > 0` to see if harvestable
- Call `Interact()` to harvest (spawns honey on ground)
- Pick up spawned honey items
- Deposit to nearby chests

**Valheim Beehive API**:
```csharp
// Key methods from Beehive.cs:
int GetHoneyLevel()           // Returns 0-4 honey available
bool Interact(Humanoid, bool, bool)  // Harvests honey, spawns ItemDrops
string m_name                 // "Beehive"
ItemDrop m_honeyItem          // Reference to honey prefab
Transform m_spawnPoint        // Where honey spawns
```

#### 2. Crop Harvesting (Pickables)
- Detect `Pickable` components within work radius
- Check if pickable and not already picked
- Call `Interact()` to harvest
- Pick up dropped items
- Deposit to nearby chests

**Key Pickable Prefabs**:
- `Pickable_Carrot` ? drops `Carrot`
- `Pickable_Turnip` ? drops `Turnip`
- `Pickable_Onion` ? drops `Onion`
- `Pickable_Barley` ? drops `Barley`
- `Pickable_Flax` ? drops `Flax`
- `Pickable_SeedCarrot` ? drops `CarrotSeeds`
- `Pickable_SeedTurnip` ? drops `TurnipSeeds`
- `Pickable_SeedOnion` ? drops `OnionSeeds`

#### 3. Crop Planting
- Find cultivated ground (`Heightmap.IsCultivated()`)
- Check companion inventory for seeds
- Use `Plant` prefab placement similar to player cultivator
- Respect plant spacing (`Plant.m_growRadius`)

**Seed ? Plant Prefab Mapping**:
```
CarrotSeeds    ? sapling_carrot
TurnipSeeds    ? sapling_turnip
OnionSeeds     ? sapling_onion
BarleySeeds    ? sapling_barley (needs Plains biome)
FlaxSeeds      ? sapling_flax (needs Plains biome)
```

### Behavior Phases

```
FarmingPhase:
??? Idle                    // Looking for work
??? MovingToBeehive         // Walking to beehive
??? HarvestingBeehive       // Interacting with beehive
??? MovingToPickable        // Walking to mature crop
??? HarvestingPickable      // Interacting with pickable
??? MovingToPlantSpot       // Walking to cultivated ground
??? Planting                // Placing seed/sapling
??? CollectingDrops         // Picking up harvested items
??? MovingToChest           // Walking to deposit chest
??? Depositing              // Storing items in chest
??? Complete                // Done with current cycle
```

### Priority Logic

```csharp
// Priority order:
// 1. Harvest beehives with honey (high value, time-sensitive)
// 2. Harvest mature crops (prevent despawn/wilt)
// 3. Plant seeds if have seeds and cultivated ground available
// 4. Deposit collected items when inventory getting full
```

### Detection Ranges

| Target | Detection Range | Interaction Range |
|--------|----------------|-------------------|
| Beehive | 30m | 2.5m |
| Pickable | 30m | 2.5m |
| Cultivated Ground | 20m | 1.5m |
| Chests | 50m (CompanionSettings) | 2.5m |

---

## Files to Create/Modify

### New Files
- [ ] `FiresNPCs/Modules/Companions/IdleBehaviors/FarmingBehavior.cs` - Main behavior class
- [ ] `FiresNPCs/Modules/Companions/IdleBehaviors/FarmingBehavior.Beehives.cs` - Beehive harvesting
- [ ] `FiresNPCs/Modules/Companions/IdleBehaviors/FarmingBehavior.Crops.cs` - Crop harvesting/planting
- [ ] `FiresNPCs/Modules/Companions/IdleBehaviors/FarmingDataHelper.cs` - Seed/plant mappings

### Files to Modify
- [ ] `FiresNPCs/Modules/Companions/IdleBehaviors/ResourceGathering/ResourceGatheringBehavior.Tools.cs` - Fix 50m tool search
- [ ] `FiresNPCs/Modules/Companions/CompanionIdleBehavior/CompanionIdleBehavior.SubBehaviors.cs` - Register FarmingBehavior
- [ ] `FiresNPCs/Modules/Companions/Core/BehaviorCoordinator.cs` - Add farming behavior to coordinator

---

## Implementation Steps

### Phase 1: Fix Tool Finding (Priority)
1. [ ] Review `ResourceGatheringBehavior.Tools.cs` - `FindToolInChests()` method
2. [ ] Verify chest search radius is 50m
3. [ ] Add logging to trace tool search
4. [ ] Fix any prefab name matching issues
5. [ ] Test commanded mining with pickaxe in chest

### Phase 2: Create FarmingDataHelper
1. [ ] Create seed-to-plant prefab mappings
2. [ ] Create pickable detection helpers
3. [ ] Create beehive detection helpers
4. [ ] Add biome compatibility checks

### Phase 3: Create FarmingBehavior Base
1. [ ] Create main partial class with phases
2. [ ] Implement `CanStart()` - check for beehives/crops/seeds
3. [ ] Implement `Start()` - initialize targets
4. [ ] Implement `Update()` - phase state machine
5. [ ] Implement `Cancel()` - cleanup

### Phase 4: Implement Beehive Harvesting
1. [ ] `FindNearbyBeehives()` - detect Beehive components
2. [ ] `ScoreBeehive()` - prioritize by honey level
3. [ ] `HarvestBeehive()` - interact and collect drops
4. [ ] Handle multiple beehives in sequence

### Phase 5: Implement Crop Harvesting
1. [ ] `FindNearbyPickables()` - detect mature Pickable components
2. [ ] `IsPickableReady()` - check if harvestable
3. [ ] `HarvestPickable()` - interact and collect drops
4. [ ] Handle seed pickables (for replanting)

### Phase 6: Implement Planting
1. [ ] `FindPlantableSpots()` - find cultivated ground
2. [ ] `HasSeedsToPlant()` - check inventory for seeds
3. [ ] `PlantSeed()` - instantiate plant prefab
4. [ ] Respect plant spacing requirements

### Phase 7: Integration & Testing
1. [ ] Register behavior in `CompanionIdleBehavior`
2. [ ] Add to `BehaviorCoordinator`
3. [ ] Test autonomous operation in Stay mode
4. [ ] Test commanded operation via ping
5. [ ] Test with multiple companions

---

## Technical Notes

### Beehive Interaction
```csharp
// To harvest a beehive:
Beehive hive = target.GetComponent<Beehive>();
if (hive.GetHoneyLevel() > 0)
{
    // Check private area access
    if (PrivateArea.CheckAccess(hive.transform.position))
    {
        hive.Interact(humanoid, false, false);
        // Honey ItemDrops spawn at hive.m_spawnPoint
    }
}
```

### Pickable Interaction
```csharp
// To harvest a pickable:
Pickable pickable = target.GetComponent<Pickable>();
ZNetView nview = pickable.GetComponent<ZNetView>();
if (nview.IsValid() && !nview.GetZDO().GetBool(ZDOVars.s_picked))
{
    pickable.Interact(humanoid, false, false);
    // Items spawn at pickable position
}
```

### Plant Placement
```csharp
// To plant a seed:
// 1. Find cultivated ground point
// 2. Check spacing with Physics.OverlapSphere for other plants
// 3. Instantiate plant prefab at position
// 4. Remove seed from companion inventory
```

### Cultivated Ground Detection
```csharp
// Check if ground is cultivated:
Heightmap heightmap = Heightmap.FindHeightmap(position);
if (heightmap != null && heightmap.IsCultivated(position))
{
    // Can plant here
}
```

---

## Testing Checklist

### Tool Finding Fix
- [ ] Place pickaxe in chest 40m from companion
- [ ] Command companion to mine nearby ore
- [ ] Verify companion retrieves pickaxe from chest
- [ ] Test with axe for wood gathering

### Beehive Harvesting
- [ ] Place beehive near stayed companion
- [ ] Wait for honey to accumulate
- [ ] Verify companion harvests honey
- [ ] Verify honey deposited to chest

### Crop Harvesting
- [ ] Plant carrots near stayed companion
- [ ] Wait for crops to mature
- [ ] Verify companion harvests crops
- [ ] Verify crops deposited to chest

### Planting
- [ ] Give companion carrot seeds
- [ ] Ensure cultivated ground nearby
- [ ] Verify companion plants seeds
- [ ] Verify proper spacing

---

## Status

| Task | Status | Notes |
|------|--------|-------|
| Tool Finding Bug Analysis | ? Complete | Was searching from single position |
| Tool Finding Fix | ? Complete | Now searches from companion, target, AND home positions |
| FarmingDataHelper | ? Complete | Seed/plant mappings, beehive/crop detection, planting utilities |
| FarmingBehavior Base | ? Complete | Main behavior class with phases and task finding |
| Beehive Harvesting | ? Complete | MovingToBeehive, HarvestingBeehive phases |
| Crop Harvesting | ? Complete | MovingToCrop, HarvestingCrop phases |
| Planting | ? Complete | MovingToPlantSpot, Planting phases |
| Integration | ? Complete | Registered in CompanionIdleBehavior.SubBehaviors.cs |
| Smelter Loop Fix | ? Complete | Added post-pull grace period to prevent pull?fill?pull loop |
| Testing | ?? Not Started | |

---

## Changelog

### 2025-03-03
- Created implementation plan document
- Documented Valheim Beehive and Plant APIs
- Outlined behavior phases and priority logic
- **FIXED**: Tool finding now searches from multiple positions (companion, target resource, home)
- Updated `TryFindToolInNearbyChests()` and `HasToolForResource()` to use multi-position search
- **CREATED**: `FarmingDataHelper.cs` with seed/plant mappings, biome checks, detection utilities
- **CREATED**: `FarmingBehavior.cs` - Main behavior class with all phases
- **CREATED**: `FarmingBehavior.Phases.cs` - Phase update methods for beehives, crops, planting
- **INTEGRATED**: Added FarmingBehavior to CompanionIdleBehavior.SubBehaviors.cs
- **FIXED**: Smelter loop bug - companion was stuck in pull?fill?pull loop due to stale inventory cache
  - Added `_justPulledItems` flag and `POST_PULL_GRACE_PERIOD` (0.5s)
  - UpdateFillingStation now waits for inventory sync before checking chest contents again
