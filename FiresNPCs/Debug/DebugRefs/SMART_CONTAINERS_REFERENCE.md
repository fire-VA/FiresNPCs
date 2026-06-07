# Smart Containers Reference

This document contains decompiled code from the SmartContainers mod for reference in improving our chest interaction systems.

## Key Concepts from SmartContainers

### 1. Container Tracking System

SmartContainers maintains a global list of all containers that gets updated via Harmony patches:

```csharp
// ContainersTracker.cs
public static ICollection<Container> containerList = new List<Container>();

// Patches Container.Awake to add containers to the list
// Patches Container.OnDestroyed to remove containers from the list
```

**Benefits:**
- No need to scan for containers every time
- Containers are tracked automatically as they're created/destroyed
- Much faster than Physics.OverlapSphere every frame

### 2. Item Grouping System

Items can be grouped by:
- **Exact match**: Same item name
- **User-defined groups**: Config-based lists (ore, ingots, wood, etc.)
- **Prefix groups**: Items starting with same prefix (trophy_, arrow_, etc.)
- **Postfix groups**: Items ending with same postfix (seeds, pelt, etc.)
- **Item type groups**: Trophies, Tools, Ammo, Armor, Weapons

### 3. Smart Routing Logic

When depositing an item, SmartContainers checks in order:
1. Containers with the **same item** (for stacking)
2. Containers with items from **same user group**
3. Containers with items from **same system group**
4. Containers with items matching **name patterns** (prefix/postfix)
5. Containers with **same item type**
6. Containers with **similar items** (fuzzy matching)

### 4. Concurrency Handling

SmartContainers has options for handling multiple players:
- `None`: Ignore concurrent access issues
- `ExcludeContainersOpenedByOthers`: Skip containers in use
- `BlockIfOtherContainersAreOpened`: Block all operations if any container is in use

This is checked via `container.m_nview.GetZDO().GetInt("InUse")`.

### 5. Item Name Normalization

```csharp
// ItemDataMethods.cs
public static string SCName(this string itemName)
{
    return itemName.ToLower().Replace("$item_", "").Replace("_", "");
}
```

This normalizes item names for consistent comparison.

---

## Key Methods We Can Adapt

### Container Finding

```csharp
public static List<Container> GetNearbyContainers(Vector3 center, bool all = false)
{
    List<Container> nearbyContainers = new List<Container>();
    foreach (Container container in ContainersTracker.containerList)
    {
        if (container != null && 
            container.transform != null && 
            container.GetInventory() != null &&
            container.GetComponentInParent<Piece>() != null)
        {
            float dist = Vector3.Distance(center, container.transform.position);
            if ((range <= 0 || dist < range) && 
                container.CheckAccess(Player.m_localPlayer.GetPlayerID()))
            {
                nearbyContainers.Add(container);
            }
        }
    }
    return nearbyContainers;
}
```

### Item Routing Logic

```csharp
public static bool TryAddToNearBy(ItemDrop.ItemData item, bool allowCurrent = false, bool forceGrouping = false)
{
    // 1. First try containers with same item (for stacking)
    foreach (Container targetContainer in list)
    {
        if (targetContainer.GetInventory().CanAddItem(item) && 
            targetContainer.GetInventory().HaveItem(item.m_shared.m_name))
        {
            return AddItemToContainer(item, targetContainer);
        }
    }
    
    // 2. Try user-defined groups
    foreach (Container container in list)
    {
        string matchingUserGroup = ItemGroups.FindMatchingUserGroup(container, item);
        if (matchingUserGroup != null && container.GetInventory().CanAddItem(item))
        {
            return AddItemToContainer(item, container);
        }
    }
    
    // 3. Try system groups
    // 4. Try name pattern matching
    // 5. Try item type matching
    // etc...
}
```

### Group Matching

```csharp
public static string FindMatchingUserGroup(Container container, ItemDrop.ItemData item)
{
    // Find all groups that contain this item AND have items in this container
    ICollection<string> matchingGroups = FindMatchingGroups(container, item.SCName(), item, groupKeys);
    
    if (matchingGroups.Count > 1)
        // Log warning about ambiguous groups
    
    return matchingGroups.Any() ? matchingGroups.Max() : null;
}

private static bool TestGroup(string shortItemName, Container container, ICollection<string> group)
{
    if (group.Contains(shortItemName))
    {
        foreach (string iname in group)
        {
            if (!iname.Equals(shortItemName) && 
                HaveMatchingItem(container, _ => _.SCName().Equals(iname)))
            {
                return true;
            }
        }
    }
    return false;
}
```

---

## Patterns to Implement in Our System

### 1. Global Container Registry

Instead of scanning with Physics.OverlapSphere every time, maintain a registry:

```csharp
public static class ContainerRegistry
{
    private static HashSet<Container> _allContainers = new HashSet<Container>();
    
    public static void Register(Container container);
    public static void Unregister(Container container);
    public static List<Container> GetNearby(Vector3 pos, float radius);
}
```

### 2. Item Category System

Define categories for intelligent routing:

```csharp
public enum ItemCategory
{
    Ore,
    Ingot,
    Wood,
    Food,
    Trophy,
    Weapon,
    Armor,
    Tool,
    Material,
    Consumable,
    Valuable,
    Misc
}

public static ItemCategory GetCategory(ItemDrop.ItemData item);
```

### 3. Smart Deposit Strategy

Priority order for depositing:
1. Stack with existing items (same type)
2. Put in chest with same category items
3. Put in chest with same item type
4. Put in any chest with space

### 4. Pull Strategy

Priority order for pulling:
1. Pull from closest chest with the item
2. Pull from chest with most of the item (consolidate)
3. Pull partial amounts if full amount not available

---

## Integration Points

### For SmelterOperatorBehavior

```csharp
// Before: Direct ChestHelper calls (no pathfinding)
ChestHelper.PullItemsByPrefabName(_nearbyChests, storageInv, "Coal", 10);

// After: Use ChestInteractionService (with pathfinding)
_chestService.StartPullOperation("Coal", 10, returnToOrigin: true);
// Then update every frame until complete
```

### For Item Categorization

We already have `SmartStorageOrganizer.GetItemCategory()` - ensure it's used consistently.

### For Container Access Checking

Add checks like SmartContainers does:
```csharp
// Check if container is in use by another player
bool isInUse = container.m_nview.GetZDO().GetInt("InUse") == 1;

// Check if player has access
bool hasAccess = container.CheckAccess(playerId);
```
