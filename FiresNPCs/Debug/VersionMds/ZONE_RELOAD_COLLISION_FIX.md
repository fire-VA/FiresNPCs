# Zone Reload Collision Fix

## ? STATUS: VERIFIED WORKING

**Tested:** Zone reload collision now works correctly. Players no longer fall through terrain when returning to a zone.

---

## Issue Description

**Problem:** Players would fall through custom terrain (dirt floor disks) when returning to a zone after leaving it.

**Symptoms:**
- First visit: Terrain collision works correctly ?
- Leave zone and return: Player falls through terrain ?
- Waiting indefinitely did not fix the issue

## Root Cause Analysis

When you leave a zone in Valheim, all GameObjects in that zone are destroyed, including `DirtFloorMeshCombiner` components. However, the caching system had several issues:

### Issue 1: Stale Dictionary Entries

The `_groupToCombiners` dictionary kept entries pointing to destroyed (null) combiners:

```
Before Fix:
Zone Load ? Combiner created ? Added to _groupToCombiners[groupId] = combiner
Zone Unload ? Combiner destroyed ? _groupToCombiners[groupId] = null (stale reference)
Zone Reload ? GetCachedCombinerForDisk finds groupId ? Gets null combiner ? No recreation!
```

### Issue 2: Assigned Disk IDs Not Cleared

The `_assignedDiskIds` HashSet was never cleared when a combiner was destroyed:

```
Before Fix:
Zone Load ? Disk processed ? Added to _assignedDiskIds
Zone Unload ? Combiner destroyed ? _assignedDiskIds still contains disk ID
Zone Reload ? Disk tries to load ? Found in _assignedDiskIds ? Skipped! No combiner!
```

### Issue 3: GetCachedCombinerForDisk Early Exit

The method checked `_assignedDiskIds` first and returned early without attempting recreation:

```csharp
// OLD CODE - Problematic early exit
if (_assignedDiskIds.Contains(zdoId))
{
    // Find existing combiner - but it's null/destroyed!
    if (_groupToCombiners.TryGetValue(groupId, out var combiner) && combiner != null)
        return combiner;
    return null; // ? BUG: Returns null instead of recreating!
}
```

## The Fix

### Change 1: `OnCombinerDestroyed` - Clear Runtime State

**File:** `DirtFloorCombinerCache.cs`

When a combiner is destroyed (zone unload), we now properly clear all runtime tracking for that group:

```csharp
public static void OnCombinerDestroyed(DirtFloorMeshCombiner combiner)
{
    if (combiner == null) return;
    
    // Find group ID
    int groupIdToRemove = -1;
    foreach (var kvp in _groupToCombiners)
    {
        if (kvp.Value == combiner)
        {
            groupIdToRemove = kvp.Key;
            break;
        }
    }
    
    if (groupIdToRemove >= 0)
    {
        // Remove combiner reference (cache data preserved for recreation)
        _groupToCombiners.Remove(groupIdToRemove);
        
        // Clear restored flag so it can be marked restored again on recreation
        _restoredGroups.Remove(groupIdToRemove);
        
        // CRITICAL: Clear assigned disk IDs for this group's disks
        // This allows them to be re-processed when the zone reloads
        var groupData = _currentCache?.Groups?.FirstOrDefault(g => g.GroupId == groupIdToRemove);
        if (groupData != null)
        {
            foreach (var zdoId in groupData.DiskZdoIds)
            {
                _assignedDiskIds.Remove(zdoId);
            }
        }
    }
}
```

**Effect:** When zone unloads, disk IDs are cleared so they can trigger combiner recreation on reload.

### Change 2: `GetCachedCombinerForDisk` - Handle Destroyed Combiners

**File:** `DirtFloorCombinerCache.cs`

The method now properly detects and handles destroyed combiners:

```csharp
public static DirtFloorMeshCombiner GetCachedCombinerForDisk(DirtFloorController disk)
{
    // ... validation code ...
    
    // Check if this disk is in a cached group
    if (!_currentCache.DiskToGroupMap.TryGetValue(zdoId, out int cachedGroupId))
    {
        return null; // Not in cache - will create new
    }
    
    // CRITICAL FIX: Check if combiner exists AND is not null/destroyed
    if (_groupToCombiners.TryGetValue(cachedGroupId, out var existingCombiner) && existingCombiner != null)
    {
        // Combiner exists - use it
        if (!existingCombiner.ConnectedDisks.Contains(disk))
        {
            existingCombiner.AddDiskWithoutRegeneration(disk);
        }
        _assignedDiskIds.Add(zdoId);
        return existingCombiner;
    }
    
    // Combiner was destroyed (zone unload) or doesn't exist yet
    // Remove stale entries
    if (_groupToCombiners.ContainsKey(cachedGroupId))
    {
        _groupToCombiners.Remove(cachedGroupId);
    }
    _restoredGroups.Remove(cachedGroupId);
    _assignedDiskIds.Add(zdoId);
    
    // Add to pending list and instantiate from cache
    // ... pending disk handling ...
    
    var groupData = _currentCache.Groups.FirstOrDefault(g => g.GroupId == cachedGroupId);
    if (groupData != null)
    {
        Debug.Log($"[DirtFloorCombinerCache] Recreating combiner {cachedGroupId} from cache (zone reload)");
        return InstantiateCombinerFromCache(cachedGroupId, groupData);
    }
    
    return null;
}
```

**Effect:** When a disk loads and its combiner was destroyed, a new combiner is instantiated from cache.

## Flow Comparison

### Before Fix

```
FIRST VISIT:
  Disk loads ? GetCachedCombinerForDisk ? No cache ? Create new combiner
  Combiner creates collision mesh ? Player can walk ?

LEAVE ZONE:
  Combiner.OnDestroy() ? OnCombinerDestroyed()
  ? _groupToCombiners[id] removed
  ? _assignedDiskIds NOT cleared ? BUG
  
RETURN TO ZONE:
  Disk loads ? GetCachedCombinerForDisk
  ? zdoId in _assignedDiskIds ? Early return with null ? BUG
  ? No combiner created ? No collision ? Player falls through ?
```

### After Fix

```
FIRST VISIT:
  Disk loads ? GetCachedCombinerForDisk ? No cache ? Create new combiner
  Combiner creates collision mesh ? Player can walk ?

LEAVE ZONE:
  Combiner.OnDestroy() ? OnCombinerDestroyed()
  ? _groupToCombiners[id] removed
  ? _restoredGroups[id] removed
  ? _assignedDiskIds cleared for all disks in group ? FIX
  
RETURN TO ZONE:
  Disk loads ? GetCachedCombinerForDisk
  ? zdoId NOT in _assignedDiskIds (was cleared)
  ? Check _groupToCombiners ? null (combiner destroyed)
  ? InstantiateCombinerFromCache() ? FIX
  ? RestoreFromCache() ? UpdateCollisionMesh()
  ? Collision mesh active ? Player can walk ?
```

## Additional Improvement: Immediate Collision for Nearby Terrain

**File:** `DirtFloorMeshCombiner.cs` - `RestoreFromCache()`

When restoring from cache, we now check distance to player:

```csharp
// CRITICAL: Check distance to player to determine if we need immediate collision
bool needsImmediateCollision = false;
Player player = Player.m_localPlayer;
if (player != null)
{
    float distToPlayer = Vector3.Distance(transform.position, player.transform.position);
    // If player is within 100m, set up collision immediately
    needsImmediateCollision = distToPlayer < 100f;
}

if (needsImmediateCollision)
{
    // Immediate setup - prevents falling through
    SetupMaterial();
    UpdateCollisionMesh();
    // Queue grass spawn (less critical)
    deferredInit.QueueGrassSpawn(this, () => SpawnGrassOnMeshDeferred());
}
else
{
    // Distant terrain - use deferred init to spread load
    deferredInit.QueueMaterialSetup(...);
    deferredInit.QueuePhysicsBake(...);
    deferredInit.QueueGrassSpawn(...);
}
```

**Effect:** Nearby terrain (< 100m) gets collision immediately on load, preventing players from falling through even during the brief moment between spawn and deferred initialization.

## Testing Checklist

- [ ] Create terrain disks in an area
- [ ] Walk on terrain - collision should work
- [ ] Walk away until zone unloads (watch for "[DirtFloorCombinerCache] Combiner X destroyed" log)
- [ ] Return to area
- [ ] Look for "[DirtFloorCombinerCache] Recreating combiner X from cache (zone reload)" log
- [ ] Walk on terrain - collision should still work
- [ ] Repeat several times to ensure consistency

## Expected Log Output

### First Visit
```
[DirtFloorMeshCombiner] Merged combiners, total disks: 5
[DirtFloorMeshCombiner] Collision mesh setup SUCCESS at (100, 50, 200): 1024 verts, 1800 tris
[DirtFloorCombinerCache] Saved cache: 1 groups
```

### Leave Zone
```
[DirtFloorCombinerCache] Combiner 1 destroyed - cleared 5 disk assignments for zone reload
```

### Return to Zone
```
[DirtFloorCombinerCache] Recreating combiner 1 from cache (zone reload)
[DirtFloorMeshCombiner] Collision mesh setup SUCCESS at (100, 50, 200): 1024 verts, 1800 tris
```

## Files Modified

| File | Changes |
|------|---------|
| `DirtFloorCombinerCache.cs` | `OnCombinerDestroyed()` - Clear disk assignments; `GetCachedCombinerForDisk()` - Handle destroyed combiners |
| `DirtFloorMeshCombiner.cs` | `RestoreFromCache()` - Immediate collision for nearby terrain; `UpdateCollisionMesh()` - Added success logging |

## Related Documentation

- `DIRT_FLOOR_OPTIMIZATION.md` - Overall optimization history
- `DIRT_FLOOR_PROGRESS.md` - Feature development progress

---

# Next Implementation: Max Grid Size Overflow Handling

## ? STATUS: IMPLEMENTED & FIXED

**Implemented:** Overflow handling now automatically creates new combiners when grid size would be exceeded, with seamless edge blending.

**Fixed (v2):** The terrain manager was force-merging overflow combiners back together, causing corruption. Now both `LevelArea` and `MergeWith` respect overflow limits.

---

## Problem Description

**Original Issue:** When a combiner reaches its maximum grid size (128x128 vertices), adding new terrain circles causes problems:

1. The combiner attempts to resize to fit the new circle
2. This repositions the existing terrain
3. Parts of the original terrain get deleted/corrupted
4. The new circle may not have proper collision under all its area

**Root Cause Found (v2 Fix):** Even though overflow detection was working, `DirtFloorTerrainManager.LevelArea()` was calling `combiner.MergeWith()` on nearby disks which **forced** the merge regardless of overflow, causing the grid to clip and corrupt the mesh.

```
OLD FLOW (BUGGY):
1. Place disk ? WouldExceedMaxSize() ? Create overflow combiner ?
2. LevelArea calls MergeWith() ? Forces merge anyway! ?
3. Grid clips at 128x128 ? Mesh corruption ?

NEW FLOW (FIXED):
1. Place disk ? WouldExceedMaxSize() ? Create overflow combiner ?
2. LevelArea checks WouldExceedMaxSize() before merging ?
3. MergeWith() checks overflow before merging ?
4. If would overflow ? Register as neighbors + blend edges instead ?
```

**Expected Behavior:** When adding terrain that would exceed max size:
1. Recognize the combiner is at max capacity
2. Create a NEW combiner for the overflow area
3. Properly blend edges between the two combiners
4. Both combiners have complete collision coverage

## Current Grid Limits

```csharp
// In DirtFloorMeshCombiner.SetupGrid()
int maxSize = 128;
if (_gridWidth > maxSize) _gridWidth = maxSize;
if (_gridHeight > maxSize) _gridHeight = maxSize;
```

This means:
- Max grid: 128 × 128 = 16,384 vertices
- At 4 verts/meter (GridResolution = 4f): ~32m × 32m max area
- Circles beyond this get clipped or cause corruption

## Proposed Solution

### Phase 1: Detect Overflow Condition

Add method to check if adding a disk would exceed max size:

```csharp
/// <summary>
/// Checks if adding a disk would cause the combiner to exceed max grid size.
/// </summary>
public bool WouldExceedMaxSize(DirtFloorController disk)
{
    if (disk == null) return false;
    
    // Calculate what bounds would be with new disk
    Bounds testBounds = _combinedBounds;
    Vector3 diskPos = disk.transform.position;
    float radius = disk.DirtRadius;
    
    testBounds.Encapsulate(new Vector3(diskPos.x - radius, diskPos.y, diskPos.z - radius));
    testBounds.Encapsulate(new Vector3(diskPos.x + radius, diskPos.y, diskPos.z + radius));
    
    // Calculate grid dimensions
    float cellSize = 1f / GridResolution;
    int testWidth = Mathf.CeilToInt(testBounds.size.x / cellSize) + 1;
    int testHeight = Mathf.CeilToInt(testBounds.size.z / cellSize) + 1;
    
    const int MAX_GRID_SIZE = 128;
    return testWidth > MAX_GRID_SIZE || testHeight > MAX_GRID_SIZE;
}
```

### Phase 2: Modify AddDisk to Handle Overflow

```csharp
public void AddDisk(DirtFloorController disk)
{
    if (disk == null || _connectedDisks.Contains(disk))
        return;
    
    // Check if already assigned
    if (DirtFloorCombinerCache.IsDiskAssigned(disk))
        return;
    
    // NEW: Check if this would exceed max size
    if (WouldExceedMaxSize(disk))
    {
        // Don't add to this combiner - create overflow combiner instead
        HandleOverflowDisk(disk);
        return;
    }
    
    // ... existing add logic ...
}
```

### Phase 3: Create Overflow Combiner with Edge Blending

```csharp
/// <summary>
/// Handles a disk that would overflow this combiner's max size.
/// Creates a new combiner and ensures proper edge blending.
/// </summary>
private void HandleOverflowDisk(DirtFloorController disk)
{
    // Create new combiner for this disk
    var overflowCombiner = new GameObject("DirtFloorMeshCombiner_Overflow");
    var newCombiner = overflowCombiner.AddComponent<DirtFloorMeshCombiner>();
    
    // Add the disk to the new combiner
    newCombiner.AddDisk(disk);
    
    // Register as neighbor for edge blending
    RegisterNeighborCombiner(newCombiner);
    newCombiner.RegisterNeighborCombiner(this);
    
    // Blend edges where combiners meet
    BlendEdgesWithNeighbor(newCombiner);
    
    Debug.Log($"[DirtFloorMeshCombiner] Created overflow combiner for disk at {disk.transform.position}");
}
```

### Phase 4: Edge Blending Between Combiners

```csharp
// Track neighbor combiners for edge blending
private List<DirtFloorMeshCombiner> _neighborCombiners = new List<DirtFloorMeshCombiner>();

public void RegisterNeighborCombiner(DirtFloorMeshCombiner neighbor)
{
    if (neighbor != null && neighbor != this && !_neighborCombiners.Contains(neighbor))
    {
        _neighborCombiners.Add(neighbor);
    }
}

/// <summary>
/// Blends edge heights with a neighboring combiner to prevent seams.
/// </summary>
public void BlendEdgesWithNeighbor(DirtFloorMeshCombiner neighbor)
{
    if (neighbor == null || _heights == null || neighbor._heights == null)
        return;
    
    // Find overlapping edge region
    Bounds overlap = GetOverlapBounds(neighbor._combinedBounds);
    if (overlap.size.x <= 0 || overlap.size.z <= 0)
        return;
    
    // For each grid point in the overlap region, blend heights
    float blendDistance = BlendDistance;
    
    for (int z = 0; z < _gridHeight; z++)
    {
        for (int x = 0; x < _gridWidth; x++)
        {
            Vector3 worldPos = GridToWorld(x, z);
            
            // Check if in overlap region
            if (!overlap.Contains(new Vector3(worldPos.x, overlap.center.y, worldPos.z)))
                continue;
            
            // Get height from neighbor at this position
            float neighborHeight = neighbor.SampleHeightAt(worldPos);
            if (neighborHeight < -50f) continue; // No valid height
            
            // Calculate blend weight based on distance to edge
            float distToMyEdge = GetDistanceToEdge(worldPos);
            float distToTheirEdge = neighbor.GetDistanceToEdge(worldPos);
            
            float totalDist = distToMyEdge + distToTheirEdge;
            float neighborWeight = totalDist > 0.001f ? distToMyEdge / totalDist : 0.5f;
            
            // Blend heights
            int idx = z * _gridWidth + x;
            float myHeight = _heights[idx];
            _heights[idx] = Mathf.Lerp(myHeight, neighborHeight, neighborWeight * 0.5f);
        }
    }
    
    // Rebuild mesh with blended heights
    BuildMesh();
}
```

### Phase 5: Modify GetOrCreateCombinerFor

```csharp
public static DirtFloorMeshCombiner GetOrCreateCombinerFor(DirtFloorController disk)
{
    // ... existing cache checks ...
    
    // Check if disk overlaps with any existing combiner
    foreach (var combiner in s_allCombiners)
    {
        if (combiner != null && combiner.OverlapsWith(disk))
        {
            // NEW: Check if adding would overflow
            if (combiner.WouldExceedMaxSize(disk))
            {
                // Find or create overflow combiner
                var overflowCombiner = FindOrCreateOverflowCombiner(combiner, disk);
                return overflowCombiner;
            }
            
            // Normal case - add to existing
            if (!DirtFloorCombinerCache.IsGroupRestored(combiner))
            {
                combiner.AddDisk(disk);
                return combiner;
            }
        }
    }
    
    // ... create new combiner ...
}

private static DirtFloorMeshCombiner FindOrCreateOverflowCombiner(
    DirtFloorMeshCombiner fullCombiner, 
    DirtFloorController disk)
{
    // Check if there's already an overflow combiner that can accept this disk
    foreach (var combiner in s_allCombiners)
    {
        if (combiner == null || combiner == fullCombiner) continue;
        
        // Check if this combiner is a neighbor that can accept the disk
        if (combiner.OverlapsWith(disk) && !combiner.WouldExceedMaxSize(disk))
        {
            combiner.AddDisk(disk);
            return combiner;
        }
    }
    
    // Create new overflow combiner
    var newCombinerObj = new GameObject("DirtFloorMeshCombiner_Overflow");
    var newCombiner = newCombinerObj.AddComponent<DirtFloorMeshCombiner>();
    newCombiner.AddDisk(disk);
    
    // Register as neighbors for edge blending
    fullCombiner.RegisterNeighborCombiner(newCombiner);
    newCombiner.RegisterNeighborCombiner(fullCombiner);
    
    // Blend edges
    fullCombiner.BlendEdgesWithNeighbor(newCombiner);
    newCombiner.BlendEdgesWithNeighbor(fullCombiner);
    
    // Register with cache
    DirtFloorCombinerCache.RegisterCombinerGroup(newCombiner);
    
    Debug.Log($"[DirtFloorMeshCombiner] Created overflow combiner, blended with neighbor");
    
    return newCombiner;
}
```

## Implementation Flow

```
NEW DISK PLACEMENT:
  ?
  ?
GetOrCreateCombinerFor(disk)
  ?
  ??? Check cache ? Found? ? Return cached combiner
  ?
  ??? Find overlapping combiner
  ?     ?
  ?     ??? WouldExceedMaxSize(disk)? 
  ?     ?     ?
  ?     ?     ??? YES: FindOrCreateOverflowCombiner()
  ?     ?     ?         ?
  ?     ?     ?         ??? Find existing overflow that fits
  ?     ?     ?         ?   OR create new combiner
  ?     ?     ?         ?
  ?     ?     ?         ??? Register as neighbors
  ?     ?     ?         ?
  ?     ?     ?         ??? BlendEdgesWithNeighbor() ??? KEY: Seamless transition
  ?     ?     ?
  ?     ?     ??? NO: AddDisk() normally
  ?     ?
  ?     ??? No overlap ? Create new combiner
  ?
  ?
RESULT: All terrain has collision, edges blend seamlessly
```

## Edge Cases to Handle

1. **Disk spans multiple combiners:** Split disk into regions, each assigned to appropriate combiner
2. **Three-way corner:** Multiple combiners meet at a point - need multi-way blending
3. **Cache persistence:** Overflow combiners need to be cached and restored properly
4. **Neighbor tracking across zone reload:** Re-establish neighbor relationships on load

## Testing Checklist

- [ ] Place terrain until combiner reaches max size (~32m × 32m area)
- [ ] Place more terrain overlapping the edge - should see "Creating overflow combiner" log
- [ ] Verify new combiner is created (not corruption) - look for "DirtFloorMeshCombiner_Overflow" in hierarchy
- [ ] Walk across seam between combiners - should be seamless with blended heights
- [ ] Leave zone and return - both combiners should restore correctly
- [ ] Heights at edges should match between combiners
- [ ] Check collision works on both combiners

## Expected Log Output

### When Overflow Occurs
```
[DirtFloorMeshCombiner] WouldExceedMaxSize: disk at (X, Y, Z) would create grid 140x130 (max=128)
[DirtFloorMeshCombiner] GetOrCreateCombinerFor: Combiner at (X, Y, Z) would overflow with disk at (X, Y, Z)
[DirtFloorMeshCombiner] Created new overflow combiner at (X, Y, Z)
[DirtFloorMeshCombiner] Registered neighbor combiner at (X, Y, Z)
[DirtFloorMeshCombiner] Blended 45 edge vertices with neighbor
```

## Files to Modify

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.cs` | ? Added `WouldExceedMaxSize()`, `HandleOverflowDisk()`, `BlendEdgesWithNeighbor()`, neighbor tracking, `FindOrCreateOverflowCombiner()`. ? **v2:** Added overflow check to `MergeWith()` |
| `DirtFloorTerrainManager.cs` | ? **v2:** Fixed `LevelArea()` to check overflow before merging, register as neighbors instead if would overflow |
| `DirtFloorCombinerCache.cs` | Future: Cache neighbor relationships for zone reload |

## Implementation Details

### v2 Fixes

**`DirtFloorMeshCombiner.MergeWith()` - Now checks overflow:**
```csharp
public void MergeWith(DirtFloorMeshCombiner other)
{
    // ... existing restored check ...
    
    // NEW: OVERFLOW CHECK - Don't merge if any disk from other would overflow this combiner
    foreach (var disk in other._connectedDisks)
    {
        if (disk != null && WouldExceedMaxSize(disk))
        {
            Debug.Log($"[DirtFloorMeshCombiner] MergeWith blocked - would exceed max grid size");
            
            // Register as neighbors for edge blending instead of merging
            RegisterNeighborCombiner(other);
            other.RegisterNeighborCombiner(this);
            
            // Blend edges
            BlendEdgesWithNeighbor(other);
            other.BlendEdgesWithNeighbor(this);
            
            return; // Don't merge
        }
    }
    
    // ... normal merge logic ...
}
```

**`DirtFloorTerrainManager.LevelArea()` - No longer force-merges:**
```csharp
// OLD (buggy):
foreach (var disk in disksToMerge)
{
    combiner.MergeWith(existingCombiner); // Force merged regardless of size!
}

// NEW (fixed):
foreach (var disk in disksToMerge)
{
    // Check if merging would exceed max size
    bool wouldOverflow = false;
    foreach (var otherDisk in existingCombiner.ConnectedDisks)
    {
        if (combiner.WouldExceedMaxSize(otherDisk))
        {
            wouldOverflow = true;
            break;
        }
    }
    
    if (!wouldOverflow)
    {
        combiner.MergeWith(existingCombiner);
    }
    else
    {
        // Register as neighbors for edge blending instead
        combiner.RegisterNeighborCombiner(existingCombiner);
        existingCombiner.RegisterNeighborCombiner(combiner);
    }
}
```

### New Methods Added to `DirtFloorMeshCombiner.cs`

1. **`WouldExceedMaxSize(DirtFloorController disk)`** - Checks if adding a disk would exceed the 128x128 grid limit
2. **`IsAtMaxCapacity()`** - Quick check if combiner is near max size
3. **`RegisterNeighborCombiner()` / `UnregisterNeighborCombiner()`** - Tracks adjacent combiners for edge blending
4. **`GetOverlapBounds(Bounds otherBounds)`** - Calculates overlap region between two combiners
5. **`SampleHeightAt(Vector3 worldPos)`** - Samples height from grid at world position
6. **`GetDistanceToEdge(Vector3 worldPos)`** - Gets distance to combiner boundary
7. **`BlendEdgesWithNeighbor(DirtFloorMeshCombiner neighbor)`** - Blends heights at combiner boundaries
8. **`BlendWithAllNeighbors()`** - Blends with all registered neighbors after regeneration
9. **`HandleOverflowDisk(DirtFloorController disk)`** - Creates overflow combiner when disk won't fit
10. **`FindOrCreateOverflowCombiner()`** - Static helper to find or create overflow combiner

### Modified Methods

1. **`AddDisk()`** - Now checks `WouldExceedMaxSize()` before adding, redirects to `HandleOverflowDisk()` if needed
2. **`GetOrCreateCombinerFor()`** - Now checks overflow condition when finding combiners, uses `FindOrCreateOverflowCombiner()`
3. **`OnDestroy()`** - Cleans up neighbor relationships
4. **`SetupGrid()`** - Uses `MAX_GRID_SIZE` constant instead of local variable

### New Fields

```csharp
private const int MAX_GRID_SIZE = 128;  // Grid size limit constant
private List<DirtFloorMeshCombiner> _neighborCombiners = new List<DirtFloorMeshCombiner>();  // Neighbor tracking
```

## Success Criteria

1. ? No terrain corruption when exceeding max grid size
2. ? Automatic overflow combiner creation
3. ? Seamless visual and collision transition at edges (via BlendEdgesWithNeighbor)
4. ? **v2:** MergeWith respects overflow limits
5. ? **v2:** LevelArea respects overflow limits
6. ? Proper persistence across zone reload (neighbor relationships not yet cached)
7. ? No performance regression

## Expected Log Output (After v2 Fix)

### Normal Merge (Under Max Size)
```
[DirtFloorMeshCombiner] Merged combiners, total disks: 5
[DirtFloorMeshCombiner] Collision mesh setup SUCCESS at (X, Y, Z): 9000 verts
```

### Overflow Blocked - Neighbor Registration Instead
```
[DirtFloorMeshCombiner] WouldExceedMaxSize: disk at (X, Y, Z) would create grid 140x130 (max=128)
[DirtFloorMeshCombiner] MergeWith blocked - would exceed max grid size. Registering as neighbors instead.
[DirtFloorMeshCombiner] Registered neighbor combiner at (X, Y, Z)
[DirtFloorMeshCombiner] Blended 45 edge vertices with neighbor
[DirtFloorMeshCombiner] Collision mesh setup SUCCESS at (X, Y, Z): 9000 verts
[DirtFloorMeshCombiner] Collision mesh setup SUCCESS at (X, Y, Z): 9000 verts
```

### Key Difference From Before
**Before v2:** You would see `Merged combiners, total disks: 17` growing unbounded, then mesh corruption.
**After v2:** Disks stay in separate combiners once max size is reached, with edge blending between them.

---

# Next Implementation: Grass & Multi-Layer Terrain Fixes

## ? STATUS: IMPLEMENTED

**Issues Fixed:**
1. ? Floating grass after terrain height changes
2. ? Patchy/ugly grass on large combined disks  
3. ? Paint operations now affect all overlapping layers within 1m height
4. ? Cave support (requires testing)

---

**Issues Identified:**

### Issue 1: Floating Grass After Height Changes
When terrain at one height overlaps with terrain at a lower height (causing the first terrain to drop partway across), the grass layer doesn't recognize the height change and continues floating at the old height.

**Root Cause:** Grass spawn positions are calculated once when spawned, but terrain mesh heights can change when disks merge/blend. The grass Y positions become stale.

**Solution:**
1. After any mesh regeneration that changes heights, re-spawn grass with updated Y positions
2. Sample actual mesh height at each grass spawn point rather than using cached values
3. Add `RefreshGrassHeights()` method that updates grass Y positions without full respawn

### Issue 2: Patchy/Ugly Grass on Large Combined Disks
When multiple large disks are connected, grass becomes patchy and doesn't cover the entire combined area properly.

**Root Cause:** 
- Grass spawning uses individual disk radii rather than the combined mesh bounds
- Edge blending between disks may create gaps in grass coverage
- `SpawnGrassOnMesh()` may not be sampling the full grid coverage

**Solution:**
1. Ensure grass spawning covers the entire `_combinedBounds` area, not just individual disk areas
2. Fill in gaps by spawning grass at grid points that have valid heights but weren't assigned to a disk
3. Improve density at disk boundaries

### Issue 3: Paint Operations on Overlapping Layers
When terrain paint is applied to an overlapping section, it should remove grass from ALL overlapping layers within 1m height difference, not just one.

**Root Cause:** `OnPaintTerrain()` only updates the current combiner's paint mask and grass, not other combiners at similar heights.

**Solution:**
1. When painting, find all combiners whose bounds overlap the paint area
2. Filter to combiners within ±1m height of the paint position
3. Apply paint and refresh grass on all matching combiners

### Issue 4: Cave Support (Multi-Layer Collision)
When terrain is placed at heights too different to merge (e.g., floor and ceiling of a cave), both layers need independent collision meshes.

**Current Behavior:** The `MaxHeightDifference` (8m) prevents merging, but the separate combiners may not both have working collision.

**Expected Behavior:**
- Floor combiner has collision ?
- Ceiling combiner (8+ meters above) has its own collision ?
- Player can walk on floor and jump to hit ceiling
- Both layers can have independent grass, paint, etc.

**Solution:**
1. Verify that separate combiners (created due to height difference) each get their own collision mesh
2. Ensure `GetOrCreateCombinerFor()` creates truly independent combiners for height-separated disks
3. Add logging to confirm multi-layer collision setup

---

## Implementation Plan

### Phase 1: Fix Floating Grass (Height-Aware Grass)

```csharp
// In DirtFloorMeshCombiner.cs

/// <summary>
/// Refreshes grass Y positions to match current mesh heights.
/// Called after terrain height modifications.
/// </summary>
public void RefreshGrassHeights()
{
    if (_spawnedGrass == null || _spawnedGrass.Count == 0) return;
    
    foreach (var grassObj in _spawnedGrass)
    {
        if (grassObj == null) continue;
        
        Vector3 pos = grassObj.transform.position;
        
        // Sample actual mesh height at this XZ position
        float meshHeight = SampleHeightAt(pos);
        if (meshHeight > -50f)
        {
            // Update Y position to match mesh
            grassObj.transform.position = new Vector3(pos.x, meshHeight + 0.02f, pos.z);
        }
    }
}

// Call RefreshGrassHeights() after:
// - RegenerateMesh()
// - ForceRegenerate()
// - BlendEdgesWithNeighbor()
// - Any height modification operation
```

### Phase 2: Fix Patchy Grass Coverage

```csharp
// In SpawnGrassOnMesh() - ensure full coverage

// Instead of just iterating disk areas, iterate the entire valid grid
for (int z = 0; z < _gridHeight; z += spacingSteps)
{
    for (int x = 0; x < _gridWidth; x += spacingSteps)
    {
        int idx = z * _gridWidth + x;
        if (idx >= _heights.Length || _heights[idx] < -50f) continue;
        
        Vector3 worldPos = GridToWorld(x, z);
        
        // Find the disk this point belongs to (for grass type)
        // But spawn grass even if at edge/between disks
        DirtFloorController closestDisk = FindClosestDiskForGrass(worldPos);
        if (closestDisk == null) continue;
        
        // Check paint mask - skip painted areas
        var paintType = GetPaintTypeAtGridPoint(x, z);
        if (paintType != DirtFloorController.PaintType.Reset) continue;
        
        // Spawn grass at this position using mesh height
        SpawnGrassAt(worldPos, _heights[idx], closestDisk);
    }
}
```

### Phase 3: Multi-Layer Paint Propagation

```csharp
// In DirtFloorMeshCombiner.cs

/// <summary>
/// Applies paint to all overlapping combiners within height threshold.
/// </summary>
public static void PaintAllLayersAt(Vector3 worldPos, float radius, 
    DirtFloorController.PaintType paintType, float heightThreshold = 1f)
{
    float paintHeight = worldPos.y;
    
    foreach (var combiner in s_allCombiners)
    {
        if (combiner == null) continue;
        
        // Check if combiner's bounds overlap with paint area
        if (!combiner._combinedBounds.Contains(new Vector3(worldPos.x, 
            combiner._combinedBounds.center.y, worldPos.z)))
        {
            continue;
        }
        
        // Check height difference
        float combinerHeight = combiner.transform.position.y;
        if (Mathf.Abs(combinerHeight - paintHeight) > heightThreshold)
        {
            continue; // Too far vertically - different layer (cave floor/ceiling)
        }
        
        // Apply paint to this layer
        combiner.OnPaintTerrain(worldPos, radius, paintType);
    }
}
```

### Phase 4: Verify Cave/Multi-Layer Collision

```csharp
// In GetOrCreateCombinerFor() - ensure height-separated disks get independent combiners

// When checking OverlapsWith(), the MaxHeightDifference check should prevent
// disks 8+ meters apart from joining the same combiner.

// Add verification logging:
foreach (var combiner in s_allCombiners)
{
    if (combiner != null && combiner.OverlapsWith(disk))
    {
        // This means height difference <= MaxHeightDifference
        // Disks will merge
    }
    else if (combiner != null && HorizontalOverlaps(combiner, disk))
    {
        // Horizontal overlap but too much height difference
        // This creates a CAVE - both need collision
        Debug.Log($"[Cave Detection] Disk at Y={disk.transform.position.y} " +
            $"overlaps combiner at Y={combiner.transform.position.y} " +
            $"but height diff > {MaxHeightDifference} - creating separate layer");
    }
}
```

---

## Testing Checklist

### Floating Grass Fix
- [ ] Place terrain disk A at height 100
- [ ] Place overlapping disk B at height 95 (causes A to blend down)
- [ ] Verify grass on disk A adjusts Y position to match new terrain height
- [ ] No floating grass visible

### Patchy Grass Fix
- [ ] Connect 3+ large disks together
- [ ] Verify grass covers entire combined area without gaps
- [ ] Edges between disks have proper grass density
- [ ] No bald patches at disk boundaries

### Multi-Layer Paint
- [ ] Create two overlapping disks at similar height (within 1m)
- [ ] Paint one area with cultivate
- [ ] Verify both layers lose grass in painted area
- [ ] Create cave (disks 10m apart vertically)
- [ ] Paint floor - verify ceiling grass is NOT affected

### Cave Collision
- [ ] Place disk at Y=100 (floor)
- [ ] Place overlapping disk at Y=110 (ceiling) 
- [ ] Verify player can walk on floor (collision works)
- [ ] Verify player hits ceiling when jumping (ceiling has collision)
- [ ] Both layers render grass independently

---

## Files to Modify

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.cs` | Add `RefreshGrassHeights()`, fix `SpawnGrassOnMesh()` coverage, add `PaintAllLayersAt()` |
| `DirtFloorTerrainManager.cs` | Use `PaintAllLayersAt()` for paint operations |
| `GrassOverlayController.cs` | May need updates for height-aware grass on individual disks |

---

## Success Criteria

1. ? No floating grass after terrain height changes - `RefreshGrassHeights()` implemented
2. ? Full grass coverage on large combined terrain - Improved `SpawnGrassOnMesh()` with weighted distances
3. ? Paint operations affect all overlapping layers within 1m height - `PaintAllLayersAt()` implemented
4. ? Cave support with independent collision on floor and ceiling - Needs testing
5. ? Performance not degraded

## Implementation Complete

### New Methods Added

**`DirtFloorMeshCombiner.RefreshGrassHeights()`**
- Updates grass Y positions to match current mesh heights
- Handles both instanced and non-instanced grass
- Called automatically after `ForceRegenerate()` and `BlendEdgesWithNeighbor()`

**`DirtFloorMeshCombiner.PaintAllLayersAt()`** (static)
- Paints all overlapping combiners within height threshold
- Default threshold of 1m ensures close layers get painted together
- Cave floors/ceilings (8m+ apart) are painted independently

### Improved Methods

**`SpawnGrassOnMesh()`**
- Now uses weighted distance to find the best disk for each spawn point
- Includes points up to `BlendDistance` beyond disk radius
- Eliminates bald patches at disk boundaries

**`ForceRegenerate()`**
- Now calls `RefreshGrassHeights()` after mesh regeneration

**`BlendEdgesWithNeighbor()`**
- Now calls `RefreshGrassHeights()` after blending heights

**`DirtFloorTerrainManager.PaintArea()`**
- Now uses `PaintAllLayersAt()` for multi-layer support

---

# Multi-Layer Height Isolation Fix

## ? STATUS: IMPLEMENTED

**Issue:** When terrain exists at two different heights (e.g., cave floor at Y=100 and ceiling at Y=110), editing one layer would incorrectly affect the other layer. Specifically:
- Pickaxe hits on the floor would pull down the ceiling mesh
- Level operations on the floor would modify ceiling terrain
- Smooth operations would blend heights across layers that should be independent

**Root Cause:** Height modification operations (`ModifyHeightAt`, `OnLevelGround`, `OnRaiseGround`, `OnSmoothGround`, etc.) iterated through ALL connected disks without checking if they were at the relevant height level.

## The Fix

### New Constant: `MAX_HEIGHT_OPERATION_DISTANCE`

```csharp
/// <summary>
/// Maximum vertical distance for height operations to affect a disk.
/// Operations targeting a specific height will only affect disks within this range.
/// This prevents cave floor edits from affecting the ceiling and vice versa.
/// </summary>
private const float MAX_HEIGHT_OPERATION_DISTANCE = 2f;
```

### Modified Methods

All height-modifying methods now check vertical distance before applying changes:

1. **`ModifyHeightAt()`** - Now filters disks by `MAX_HEIGHT_OPERATION_DISTANCE` from target Y
2. **`OnPickaxeHit()`** - Validates combiner has disks at hit height before processing
3. **`OnRaiseGround()`** - Only raises disks within vertical range
4. **`OnLevelGround()`** - Only levels disks within vertical range of target height
5. **`ApplyMinHeight()`** - Only applies to disks within vertical range
6. **`ApplyMaxHeight()`** - Only applies to disks within vertical range  
7. **`OnSmoothGround()`** - Only smooths disks within vertical range

### Example: Height-Isolated Pickaxe Hit

```csharp
public void OnPickaxeHit(Vector3 hitPoint, float damage)
{
    // First, verify this combiner should handle this hit based on height
    float closestDiskVertDist = float.MaxValue;
    foreach (var disk in _connectedDisks)
    {
        if (disk == null) continue;
        float vertDist = Mathf.Abs(disk.transform.position.y - hitPoint.y);
        if (vertDist < closestDiskVertDist)
        {
            closestDiskVertDist = vertDist;
        }
    }
    
    // If no disk is close enough vertically, don't process this hit
    if (closestDiskVertDist > MAX_HEIGHT_OPERATION_DISTANCE * 2f)
    {
        Debug.Log($"[DirtFloorMeshCombiner] Pickaxe hit ignored - no disks within height range");
        return;
    }
    
    // ... proceed with height modification
}
```

### Example: Height-Isolated ModifyHeightAt

```csharp
public bool ModifyHeightAt(Vector3 worldPos, float delta, float radius)
{
    bool modified = false;
    float targetY = worldPos.y;
    
    foreach (var disk in _connectedDisks)
    {
        if (disk == null) continue;
        
        // CRITICAL: Check vertical distance to target position
        // This prevents modifying terrain at very different heights
        float verticalDist = Mathf.Abs(disk.transform.position.y - targetY);
        if (verticalDist > MAX_HEIGHT_OPERATION_DISTANCE)
        {
            continue; // Skip - this disk is at a different height layer
        }
        
        // ... proceed with horizontal distance check and modification
    }
    return modified;
}
```

## Expected Behavior

### Before Fix
```
CAVE SCENARIO (floor Y=100, ceiling Y=110):
  Player hits floor with pickaxe ? ModifyHeightAt called
  ? ALL disks modified including ceiling
  ? Ceiling mesh pulled down toward floor ?
```

### After Fix
```
CAVE SCENARIO (floor Y=100, ceiling Y=110):
  Player hits floor at Y=100 with pickaxe ? ModifyHeightAt called
  ? Ceiling disk at Y=110 skipped (verticalDist=10 > MAX_HEIGHT_OPERATION_DISTANCE=2)
  ? Only floor disk modified
  ? Ceiling stays at original height ?
```

## Testing Checklist

- [ ] Create floor terrain at Y=100
- [ ] Create ceiling terrain at Y=110 (same XZ position)
- [ ] Hit floor with pickaxe ? Only floor should lower
- [ ] Hit ceiling with pickaxe ? Only ceiling should lower
- [ ] Level floor ? Ceiling should NOT be affected
- [ ] Level ceiling ? Floor should NOT be affected
- [ ] Smooth floor ? Ceiling should NOT blend with floor
- [ ] Raise ground on floor ? Ceiling unchanged

## Files Modified

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.cs` | Added `MAX_HEIGHT_OPERATION_DISTANCE` constant; Added height filtering to `ModifyHeightAt()`, `OnPickaxeHit()`, `OnRaiseGround()`, `OnLevelGround()`, `ApplyMinHeight()`, `ApplyMaxHeight()`, `OnSmoothGround()` |

---

# Custom Terrain as Valid Ground Fix

## ? STATUS: IMPLEMENTED

**Issue:** Trees would fall through custom terrain instead of landing on it. Plants couldn't be placed properly. Objects using `StaticPhysics` would clip through the terrain.

**Root Cause:** Valheim uses `ZoneSystem.GetGroundHeight()` and `ZoneSystem.GetSolidHeight()` to determine ground level for physics checks. Our custom terrain was not recognized by these methods because:
1. Custom terrain uses the "piece" layer instead of "terrain" layer
2. The vanilla methods only check vanilla Heightmap components

## The Fix

### New Patches in `DirtFloorPatches.cs`

**`GetGroundHeight_Patch` (nested class with TargetMethod):**
- Targets `bool GetGroundHeight(Vector3 p, out float height)` specifically
- Uses `TargetMethod()` to select the correct overload (needed for `out` parameters)
- Checks if position is over custom terrain (DirtFloor or Combiner)
- Uses raycast to find actual terrain collision surface height
- If custom terrain is higher than vanilla terrain, uses custom height
- This makes trees, fallen objects, and `StaticPhysics` land ON our terrain

**`GetSolidHeight_Patch` (nested class with TargetMethod):**
- Targets `bool GetSolidHeight(Vector3 p, float radius, out float height, Transform ignore)` specifically
- Same logic but for solid height checks
- Used when `StaticPhysics.m_checkSolids` is true

### Helper Method: `TryGetCustomTerrainHeight()`

```csharp
private static bool TryGetCustomTerrainHeight(Vector3 worldPos, out float height)
{
    height = 0f;
    
    // Fast check: is there any custom terrain nearby?
    var dirtFloor = DirtFloorController.GetDirtFloorAtPosition(worldPos, 2f);
    if (dirtFloor == null)
    {
        var combiner = DirtFloorMeshCombiner.GetCombinerAt(worldPos);
        if (combiner == null)
            return false;
    }
    
    // Raycast down from above to find terrain surface
    Vector3 rayStart = new Vector3(worldPos.x, worldPos.y + 100f, worldPos.z);
    int layerMask = LayerMask.GetMask("piece", "piece_nonsolid");
    
    RaycastHit hit;
    if (Physics.Raycast(rayStart, Vector3.down, out hit, 200f, layerMask))
    {
        // Verify this is our custom terrain
        var hitDirtFloor = hit.collider.GetComponentInParent<DirtFloorController>();
        var hitCombiner = hit.collider.GetComponent<DirtFloorMeshCombiner>();
        
        if (hitDirtFloor != null || hitCombiner != null)
        {
            height = hit.point.y;
            return true;
        }
    }
    
    // Fallback: sample from combiner's height grid
    var nearestCombiner = DirtFloorMeshCombiner.GetCombinerAt(worldPos);
    if (nearestCombiner != null)
    {
        float sampledHeight = nearestCombiner.SampleHeightAt(worldPos);
        if (sampledHeight > -50f)
        {
            height = sampledHeight;
            return true;
        }
    }
    
    return false;
}
```

## Expected Behavior

### Before Fix
```
TREE FALLS ON CUSTOM TERRAIN:
  TreeBase.OnDestroy() ? Creates falling tree log
  StaticPhysics.Fall() ? ZoneSystem.GetGroundHeight()
  ? Returns vanilla terrain height (below our terrain)
  ? Log falls THROUGH custom terrain ?
```

### After Fix
```
TREE FALLS ON CUSTOM TERRAIN:
  TreeBase.OnDestroy() ? Creates falling tree log
  StaticPhysics.Fall() ? ZoneSystem.GetGroundHeight()
  ? Vanilla height returned first
  ? Postfix: TryGetCustomTerrainHeight() finds custom terrain
  ? Custom height is higher ? Use custom height
  ? Log lands ON custom terrain ?
```

## What This Enables

1. **Trees land on terrain** - Falling trees/logs stop on our terrain surface
2. **Objects settle properly** - Any object using `StaticPhysics` will rest on our terrain
3. **Plants grow correctly** - Combined with existing `Heightmap.IsCultivated` patch
4. **Better physics integration** - Our terrain is recognized as valid solid ground

## Testing Checklist

- [ ] Place custom terrain, chop a tree near it
- [ ] Tree log should land ON the terrain, not fall through
- [ ] Place items on custom terrain - they should stay put
- [ ] Build structures - they should have proper ground support
- [ ] Plant seeds on cultivated terrain - they should grow

## Files Modified

| File | Changes |
|------|---------|
| `DirtFloorPatches.cs` | Added `GetCustomTerrainMask()`, `TryGetCustomTerrainHeight()`, `ZoneSystem_GetGroundHeight_Postfix()`, `ZoneSystem_GetSolidHeight_Postfix()` |

---

# Paint Layer Resolution Fix

## ? STATUS: IMPLEMENTED

**Issue:** Paint layer was sometimes clipping through terrain because the paint mask texture resolution was lower than the mesh grid resolution, causing UV alignment issues.

**Root Cause:** 
- Combined paint mask was 64x64 pixels
- Mesh grid could be up to 128x128 vertices  
- UV coordinates mapped positions to 0-1 range, but the lower paint mask resolution couldn't precisely match every mesh vertex
- This caused visual artifacts where paint appeared offset from the terrain surface

## The Fix

### Change: Increase Paint Mask Resolution

**File:** `DirtFloorMeshCombiner.cs`

```csharp
// OLD:
private const int COMBINED_PAINT_MASK_SIZE = 64;

// NEW:
// Paint mask resolution must match or exceed grid resolution to prevent clipping
// When mesh has 128x128 vertices, paint mask must have at least 128x128 pixels
// for proper UV alignment. Using same size as MAX_GRID_SIZE ensures 1:1 mapping.
private const int COMBINED_PAINT_MASK_SIZE = 128;
```

### How Paint and Terrain Work Together

The terrain system uses three layers, all on the same mesh:

1. **Collision Mesh** - At `vertices[i].y - 0.02f` (2cm below visual)
2. **Visual Mesh** - At `_heights[idx]` (the terrain surface)
3. **Grass** - At `_heights[idx] + 0.02f` (2cm above surface)

The paint is NOT a separate layer - it's a texture (`_ClearedMaskTex`) applied to the terrain material via UV coordinates (UV0). The shader samples this texture to determine terrain appearance (grass/cultivated/paved/dirt).

### UV Mapping

Both the mesh and paint mask use the same UV calculation:

```csharp
// In BuildMesh() - UV0 for paint mask:
float paintU = (worldPos.x - _combinedBounds.min.x) / boundsWidth;
float paintV = (worldPos.z - _combinedBounds.min.z) / boundsDepth;

// In UpdateCombinedPaintMask() - same mapping:
float u = (float)x / (COMBINED_PAINT_MASK_SIZE - 1);
float v = (float)y / (COMBINED_PAINT_MASK_SIZE - 1);
Vector3 worldPos = new Vector3(
    _combinedBounds.min.x + u * boundsWidth,
    transform.position.y,
    _combinedBounds.min.z + v * boundsDepth
);
```

By using the same resolution (128x128) for both mesh and paint mask, every mesh vertex has a corresponding paint mask pixel, eliminating UV alignment issues.

## Files Modified

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.cs` | Changed `COMBINED_PAINT_MASK_SIZE` from 64 to 128 |

## Testing Checklist

- [ ] Place terrain and paint (cultivate/pave)
- [ ] Paint should align perfectly with terrain surface
- [ ] No visual clipping or offset between paint and terrain
- [ ] Grass respects painted areas (no grass on cultivated/paved)
- [ ] Paint persists across zone reload

---

# Terrain Leveling Bumps Fix

## ? STATUS: IMPLEMENTED

**Issue:** When using Infinity Hammer's level tool, the resulting terrain had bumpy/uneven surfaces instead of being perfectly flat. This was most visible when painting the terrain, as the bumps caused visual artifacts.

**Root Cause:** 
The `CustomHeightmap.LevelTerrain()` method had two problems that prevented truly flat terrain:

1. **Falloff Blending** - Heights were blended toward the target based on distance:
   ```csharp
   float blendedTarget = Mathf.Lerp(targetLocalHeight, currentH, t);
   ```
   This meant heights at the edge of the leveled area didn't reach the target.

2. **Height Clamping** - Heights were clamped to `baseH ± c_LevelMaxDelta`:
   ```csharp
   float clampedH = Mathf.Clamp(blendedTarget, baseH - c_LevelMaxDelta, baseH + c_LevelMaxDelta);
   ```
   If the original terrain was bumpy, the base heights stored those bumps, and clamping prevented the heights from being changed far enough to flatten them.

3. **Partial Disk Updates** - Only the intersection between the level radius and disk was modified. Areas of disks outside the level radius kept their original (potentially bumpy) heights, which then got sampled by `SampleHeights()` during mesh regeneration.

## The Fix

### New Methods in `CustomHeightmap.cs`

**`FlattenEntireHeightmap(float targetWorldY)`**
- Sets ALL heights in the heightmap to the exact target height
- NO falloff, NO clamping
- Also updates `m_baseHeights` so future operations don't clamp back
- **NOTE:** This is available but NOT used for level operations to respect selection area

**`FlattenAreaToHeight(Vector3 worldPos, float radius, float targetWorldY)`**
- Sets heights within a radius to the exact target height
- NO falloff, NO clamping  
- Also updates `m_baseHeights` within the area
- **This is the method used by level operations - it respects the exact selection radius**

```csharp
public void FlattenAreaToHeight(Vector3 worldPos, float radius, float targetWorldY)
{
    WorldToVertex(worldPos, out int cx, out int cz);
    float targetLocalHeight = targetWorldY - transform.position.y;
    
    float gridRadius = radius / m_scale;
    int iRadius = Mathf.CeilToInt(gridRadius);
    int numVerts = VertexCount;
    
    for (int z = cz - iRadius; z <= cz + iRadius; z++)
    {
        for (int x = cx - iRadius; x <= cx + iRadius; x++)
        {
            if (x < 0 || z < 0 || x >= numVerts || z >= numVerts)
                continue;
            
            float dist = Vector2.Distance(new Vector2(x, z), new Vector2(cx, cz));
            if (dist > gridRadius)
                continue;
            
            // NO falloff, NO clamping - exact target height
            int index = z * numVerts + x;
            m_heights[index] = targetLocalHeight;
            
            // Also update base heights so future operations don't clamp back
            if (m_baseHeights != null && index < m_baseHeights.Length)
            {
                m_baseHeights[index] = targetLocalHeight;
            }
        }
    }
    
    MarkDirty();
    Regenerate();
}
```

### Modified `OnLevelGround()` in `DirtFloorMeshCombiner.cs`

Now uses `FlattenAreaToHeight()` instead of `LevelTerrain()`, and **only flattens within the exact selection radius**:

```csharp
// CRITICAL FIX: Use FlattenAreaToHeight instead of LevelTerrain
// LevelTerrain has falloff and clamping that causes bumps
// FlattenAreaToHeight forces exact heights for truly flat terrain
// IMPORTANT: Only flatten within the exact selection radius - never the entire disk!
var heightmap = disk.GetComponent<CustomHeightmap>();
if (heightmap != null)
{
    // Flatten ONLY the intersection with the selection radius
    // This respects the user's selection area exactly
    heightmap.FlattenAreaToHeight(worldPos, radius, targetHeight);
}
```

## Why This Works

### Before Fix
```
Level Operation Flow:
  User levels area at height 100 with radius 5m ?
  LevelTerrain() called on each disk ?
  Heights blended: 100 at center, 95-100 at edges (falloff) ?
  Heights clamped by baseHeights (original bumps) ?
  ForceRegenerate() ? SampleHeights() reads from disk heightmaps ?
  Bumpy heights get into combined mesh ?
```

### After Fix
```
Level Operation Flow:
  User levels area at height 100 with radius 5m ?
  FlattenAreaToHeight(pos, radius=5m, height=100) called on each disk ?
  ONLY heights within the 5m radius set to EXACTLY 100 (no falloff) ?
  baseHeights also updated within radius (prevents future clamping) ?
  Heights OUTSIDE 5m radius are UNCHANGED (respects selection) ?
  ForceRegenerate() ? SampleHeights() reads from disk heightmaps ?
  Heights within selection are 100, outside unchanged ? Correct ?
```

**Key Point:** The fix respects the user's selection area exactly. Only the area within the specified radius gets flattened - areas outside remain unchanged.

## Files Modified

| File | Changes |
|------|---------|
| `CustomHeightmap.cs` | Added `FlattenEntireHeightmap()` and `FlattenAreaToHeight()` methods |
| `DirtFloorMeshCombiner.cs` | Changed `OnLevelGround()` to use `FlattenAreaToHeight()` with exact radius |

## Testing Checklist

- [ ] Use Infinity Hammer level tool on terrain with specific radius
- [ ] Only area within selection radius should be flat
- [ ] Area outside selection radius should remain unchanged
- [ ] Paint the leveled area with paved texture
- [ ] Paint should be smooth within the leveled area
- [ ] Multiple overlapping level operations should all be flat

---
