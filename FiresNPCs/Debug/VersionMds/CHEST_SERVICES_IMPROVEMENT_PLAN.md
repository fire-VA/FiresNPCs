# Chest Services Improvement Plan

## Overview

This document outlines the plan to improve our chest interaction systems, inspired by SmartContainers patterns but adapted for companion AI needs.

---

## Current Architecture

```
???????????????????????????????????????????????????????????????????
?                    CURRENT STRUCTURE                             ?
?                                                                  ?
?   ChestHelper.cs (~800 lines)                                   ?
?   - FindNearbyChests()                                          ?
?   - PullItemsFromChests()                                       ?
?   - DepositToChests()                                           ?
?   - SmartDeposit()                                              ?
?   - Inventory status checks                                     ?
?   - Chest spawning                                              ?
?                                                                  ?
?   ResourceAccessService.cs (~300 lines)                         ?
?   - Wraps ChestHelper for behaviors                             ?
?   - Caches nearby chests                                        ?
?   - Provides unified API                                        ?
?                                                                  ?
?   ChestInteractionService.cs (~450 lines) [NEW]                 ?
?   - Physical interaction (pathfinding)                          ?
?   - State machine for chest trips                               ?
?                                                                  ?
?   SmartStorageOrganizer.cs (~200 lines)                         ?
?   - Item categorization                                         ?
?   - Station-aware chest finding                                 ?
???????????????????????????????????????????????????????????????????
```

---

## Proposed Architecture

Split into focused, maintainable classes:

```
???????????????????????????????????????????????????????????????????
?                    PROPOSED STRUCTURE                            ?
?                                                                  ?
? ??????????????????????????????????????????????????????????????? ?
? ? LAYER 1: Container Registry (Global Tracking)               ? ?
? ?                                                              ? ?
? ? ContainerRegistry.cs (~150 lines)                           ? ?
? ? - Global container tracking via Harmony patches             ? ?
? ? - Fast lookup by position                                   ? ?
? ? - Handles container creation/destruction                    ? ?
? ??????????????????????????????????????????????????????????????? ?
?                                                                  ?
? ??????????????????????????????????????????????????????????????? ?
? ? LAYER 2: Item Management (Data Operations)                  ? ?
? ?                                                              ? ?
? ? ItemCategoryService.cs (~200 lines)                         ? ?
? ? - Item categorization (ore, ingot, food, etc.)              ? ?
? ? - Name normalization                                        ? ?
? ? - Group matching logic                                      ? ?
? ?                                                              ? ?
? ? InventoryTransferService.cs (~300 lines)                    ? ?
? ? - Pull items from containers                                ? ?
? ? - Deposit items to containers                               ? ?
? ? - Smart stacking logic                                      ? ?
? ? - Amount validation                                         ? ?
? ??????????????????????????????????????????????????????????????? ?
?                                                                  ?
? ??????????????????????????????????????????????????????????????? ?
? ? LAYER 3: Chest Selection (Routing Logic)                    ? ?
? ?                                                              ? ?
? ? ChestSelectionService.cs (~250 lines)                       ? ?
? ? - Find best chest for deposit (smart routing)               ? ?
? ? - Find chest with specific item                             ? ?
? ? - Station-aware selection                                   ? ?
? ? - Access checking (in-use, permissions)                     ? ?
? ??????????????????????????????????????????????????????????????? ?
?                                                                  ?
? ??????????????????????????????????????????????????????????????? ?
? ? LAYER 4: Physical Interaction (Companion Movement)          ? ?
? ?                                                              ? ?
? ? ChestInteractionService.cs (~400 lines) [EXISTS]            ? ?
? ? - Pathfinding to chest                                      ? ?
? ? - Open/close animations                                     ? ?
? ? - State machine                                             ? ?
? ? - Return to origin                                          ? ?
? ??????????????????????????????????????????????????????????????? ?
?                                                                  ?
? ??????????????????????????????????????????????????????????????? ?
? ? LAYER 5: Behavior Integration (High-Level API)              ? ?
? ?                                                              ? ?
? ? ResourceAccessService.cs [EXISTS - Update]                  ? ?
? ? - Unified API for behaviors                                 ? ?
? ? - Coordinates all lower layers                              ? ?
? ? - Caching and throttling                                    ? ?
? ??????????????????????????????????????????????????????????????? ?
???????????????????????????????????????????????????????????????????
```

---

## Phase 1: Container Registry

### Goal
Fast container lookup without Physics.OverlapSphere every time.

### Implementation

```csharp
// ContainerRegistry.cs
namespace VerdantsAscent.Modules.Companions.Core
{
    public static class ContainerRegistry
    {
        private static HashSet<Container> _allContainers = new HashSet<Container>();
        private static bool _initialized = false;
        
        // Called from Harmony patches
        public static void Register(Container container);
        public static void Unregister(Container container);
        
        // Fast lookup
        public static List<Container> GetNearby(Vector3 position, float radius);
        public static List<Container> GetNearbyAccessible(Vector3 position, float radius, long playerId);
        
        // Utility
        public static bool IsInUse(Container container);
        public static bool HasAccess(Container container, long playerId);
    }
}
```

### Harmony Patches Needed

```csharp
[HarmonyPatch(typeof(Container), "Awake")]
// Add to registry

[HarmonyPatch(typeof(Container), "OnDestroyed")]
// Remove from registry
```

---

## Phase 2: Item Category Service

### Goal
Consistent item categorization for smart routing.

### Implementation

```csharp
// ItemCategoryService.cs
namespace VerdantsAscent.Modules.Companions.Core
{
    public enum ItemCategory
    {
        Ore,
        Ingot,
        Wood,
        Stone,
        Food,
        Consumable,
        Trophy,
        Weapon,
        Armor,
        Tool,
        Ammo,
        Valuable,
        Seed,
        Mushroom,
        Berry,
        Meat,
        Fish,
        Material,
        Misc
    }
    
    public static class ItemCategoryService
    {
        // Normalize item names for comparison
        public static string NormalizeName(string itemName);
        
        // Get category for an item
        public static ItemCategory GetCategory(ItemDrop.ItemData item);
        public static ItemCategory GetCategory(string prefabName);
        
        // Check if items are related
        public static bool AreRelated(ItemDrop.ItemData item1, ItemDrop.ItemData item2);
        public static bool SharesCategory(ItemDrop.ItemData item, ItemCategory category);
        
        // Predefined groups
        public static readonly HashSet<string> OreItems;
        public static readonly HashSet<string> IngotItems;
        public static readonly HashSet<string> WoodItems;
        // etc...
    }
}
```

---

## Phase 3: Inventory Transfer Service

### Goal
Reliable item transfer with proper amount handling.

### Key Issues to Address
1. **Partial transfers**: If chest doesn't have full amount, pull what's available
2. **Stack limits**: Respect max stack sizes
3. **Weight limits**: Check companion carry weight
4. **Save after transfer**: Ensure ZDO is saved

### Implementation

```csharp
// InventoryTransferService.cs
namespace VerdantsAscent.Modules.Companions.Core
{
    public class TransferResult
    {
        public bool Success;
        public int AmountTransferred;
        public int AmountRequested;
        public string ItemPrefab;
        public Container SourceContainer;  // For pulls
        public Container TargetContainer;  // For deposits
        public string Message;
    }
    
    public static class InventoryTransferService
    {
        /// <summary>
        /// Pulls items from a container to an inventory.
        /// Handles partial amounts if full amount not available.
        /// </summary>
        public static TransferResult PullItem(
            Container source,
            Inventory destination,
            string prefabName,
            int maxAmount);
        
        /// <summary>
        /// Pulls items from multiple containers until amount is met.
        /// </summary>
        public static TransferResult PullItemFromContainers(
            IEnumerable<Container> sources,
            Inventory destination,
            string prefabName,
            int maxAmount);
        
        /// <summary>
        /// Deposits item to a container.
        /// Prioritizes stacking with existing items.
        /// </summary>
        public static TransferResult DepositItem(
            Inventory source,
            Container destination,
            ItemDrop.ItemData item);
        
        /// <summary>
        /// Deposits item to best container from list.
        /// </summary>
        public static TransferResult DepositItemSmart(
            Inventory source,
            IEnumerable<Container> destinations,
            ItemDrop.ItemData item);
        
        /// <summary>
        /// Checks if transfer is possible.
        /// </summary>
        public static bool CanTransfer(
            Inventory source,
            Inventory destination,
            ItemDrop.ItemData item,
            int amount);
    }
}
```

---

## Phase 4: Chest Selection Service

### Goal
Intelligent chest selection based on context.

### Routing Priority (Deposit)
1. **Same item exists** ? Stack with existing
2. **Same category items** ? Keep similar items together
3. **Near relevant station** ? Ore near smelter, food near cooking station
4. **Has space** ? Any chest with room

### Routing Priority (Pull)
1. **Closest with item** ? Minimize travel time
2. **Most of item** ? Consolidate from one source
3. **Not in use** ? Avoid conflicts

### Implementation

```csharp
// ChestSelectionService.cs
namespace VerdantsAscent.Modules.Companions.Core
{
    public class ChestRecommendation
    {
        public Container Chest;
        public float Score;
        public string Reason;
        public float Distance;
    }
    
    public static class ChestSelectionService
    {
        /// <summary>
        /// Find best chest to deposit an item.
        /// </summary>
        public static ChestRecommendation FindBestForDeposit(
            ItemDrop.ItemData item,
            IEnumerable<Container> candidates,
            Vector3 fromPosition);
        
        /// <summary>
        /// Find chest with a specific item.
        /// </summary>
        public static ChestRecommendation FindWithItem(
            string prefabName,
            IEnumerable<Container> candidates,
            Vector3 fromPosition,
            int minAmount = 1);
        
        /// <summary>
        /// Find chest with any of the specified items.
        /// </summary>
        public static ChestRecommendation FindWithAnyItem(
            IEnumerable<string> prefabNames,
            IEnumerable<Container> candidates,
            Vector3 fromPosition);
        
        /// <summary>
        /// Find chest near a specific station type.
        /// </summary>
        public static ChestRecommendation FindNearStation(
            string stationType,
            IEnumerable<Container> candidates,
            Vector3 stationPosition,
            float maxDistance = 10f);
        
        /// <summary>
        /// Score a chest for a specific item deposit.
        /// </summary>
        private static float ScoreChestForItem(
            Container chest,
            ItemDrop.ItemData item,
            Vector3 fromPosition);
    }
}
```

---

## Phase 5: Update ChestInteractionService

### Current Issues
- Missing partial amount handling
- No retry logic for failed transfers
- Could use smarter chest selection

### Improvements Needed

```csharp
// Add to ChestInteractionService.cs

/// <summary>
/// Pulls items, handling partial amounts.
/// If chest A has 5 coal and we need 10, pulls 5 and optionally continues to next chest.
/// </summary>
public bool StartPullOperationWithFallback(
    string itemPrefab,
    int amount,
    bool returnToOrigin = true,
    bool allowPartial = true);

/// <summary>
/// Result now includes partial transfer info.
/// </summary>
public class InteractionResult
{
    public bool Success;
    public int AmountRequested;
    public int AmountTransferred;  // May be less than requested
    public bool IsPartial => AmountTransferred < AmountRequested && AmountTransferred > 0;
    // ... existing fields
}
```

---

## Phase 6: Integration into Behaviors

### SmelterOperatorBehavior

```csharp
// Add new phases
private enum OperatePhase
{
    // Existing...
    
    // NEW: Chest interaction phases
    FetchingMaterialsFromChest,
    DepositingOutputToChest,
}

// In Update:
case OperatePhase.FetchingMaterialsFromChest:
    if (_chestService.Update())
    {
        if (_chestService.LastResult.Success)
        {
            SetPhase(OperatePhase.FillingStation);
        }
        else if (_chestService.LastResult.IsPartial)
        {
            // Got some items, continue with what we have
            SetPhase(OperatePhase.FillingStation);
        }
        else
        {
            // No items found - wait or complete
            SetPhase(OperatePhase.WaitingForOutput);
        }
    }
    break;
```

### ResourceGatheringBehavior

```csharp
// When inventory is full, deposit to chest
if (_resources.IsInventoryFullOrNearlyFull())
{
    if (_chestService.StartDepositAllOperation(returnToOrigin: true))
    {
        SetPhase(GatheringPhase.DepositingToChest);
    }
}
```

---

## Implementation Order

### Sprint 1: Foundation (2-3 days) ? COMPLETE
1. ? Create `ChestInteractionService.cs` [DONE]
2. ? Create `ContainerRegistry.cs` with Harmony patches [DONE]
3. ? Create `ContainerRegistryPatches.cs` [DONE]
4. ? Update `ChestHelper.cs` to use `ContainerRegistry` [DONE]
5. ?? Skip `ItemCategoryService.cs` - `SmartStorageOrganizer` already handles this well

### Sprint 2: Transfer Logic (2 days) ? COMPLETE
6. ? Create `InventoryTransferService.cs` [DONE]
7. ? Update `ChestInteractionService` to use it [DONE]
8. ? Add partial transfer support [DONE]

### Sprint 3: Selection Logic (1-2 days) ?? SKIPPED
9. ?? Skip `ChestSelectionService.cs` - `SmartStorageOrganizer` and `ChestHelper` already handle this
10. ?? Skip `ResourceAccessService` update - already works well

### Sprint 4: Behavior Integration (2-3 days) ? COMPLETE
11. ? Integrate into `SmelterOperatorBehavior` [DONE]
    - Added `UsePhysicalChestInteraction` toggle
    - Added new phases: `MovingToChestForPull`, `InteractingWithChestForPull`, `MovingToChestForDeposit`, `InteractingWithChestForDeposit`
    - Companion now walks to chests to get materials and deposit items
    - Falls back to instant transfer if pathfinding times out or toggle is disabled
12. TODO: Integrate into `ResourceGatheringBehavior`
13. TODO: Integrate into other behaviors (FireTendingBehavior, ChestDepositBehavior)

### Sprint 5: Testing & Polish (1-2 days) - TODO
14. Test SmelterOperator with physical chest interaction
15. Test partial transfers
16. Test concurrent access handling
17. Test toggle between physical and instant modes

---

## Testing Checklist

### Container Registry
- [ ] Containers are tracked on Awake
- [ ] Containers are removed on destroy
- [ ] GetNearby returns correct results
- [ ] Performance is better than Physics.OverlapSphere

### Item Transfers
- [ ] Full amount transfer works
- [ ] Partial amount transfer works
- [ ] Weight limits are respected
- [ ] Stack limits are respected
- [ ] ZDO is saved after transfer

### Chest Selection
- [ ] Stacking priority works (same item first)
- [ ] Category routing works
- [ ] Station-aware routing works
- [ ] Distance is considered

### Physical Interaction
- [ ] Pathfinding works around obstacles
- [ ] Animations play correctly
- [ ] Return to origin works
- [ ] Can be cancelled by combat

### Behavior Integration
- [ ] SmelterOperator pathfinds to chests
- [ ] ResourceGathering deposits when full
- [ ] FireTending fetches fuel from chests
- [ ] No sliding or stuck issues

---

## Files to Create/Modify

| File | Action | Lines (est) | Status |
|------|--------|-------------|--------|
| `ContainerRegistry.cs` | CREATE | ~200 | ? DONE |
| `ContainerRegistryPatches.cs` | CREATE | ~50 | ? DONE |
| `ChestInteractionService.cs` | CREATE | ~450 | ? DONE |
| `InventoryTransferService.cs` | CREATE | ~350 | ? DONE |
| `ChestHelper.cs` | UPDATE | +30 | ? DONE (uses ContainerRegistry) |
| `SmelterOperatorBehavior.cs` | UPDATE | +20 | ? DONE (phases, service, fields) |
| `SmelterOperatorBehavior.StationPhases.cs` | UPDATE | +120 | ? DONE (physical pull phases) |
| `SmelterOperatorBehavior.OutputPhases.cs` | UPDATE | +100 | ? DONE (physical deposit phases) |
| `SmelterOperatorBehavior.Lifecycle.cs` | UPDATE | +20 | ? DONE (phase handlers) |
| `ResourceGatheringBehavior.*.cs` | UPDATE | +100 | TODO |
| `FireTendingBehavior.cs` | UPDATE | +50 | TODO |
| `ChestDepositBehavior.cs` | UPDATE | +50 | TODO |

---

## Dependencies

```
ContainerRegistry
    ?
ItemCategoryService
    ?
InventoryTransferService ? ChestSelectionService
    ?
ChestInteractionService
    ?
ResourceAccessService
    ?
Behaviors (SmelterOperator, ResourceGathering, etc.)
```
