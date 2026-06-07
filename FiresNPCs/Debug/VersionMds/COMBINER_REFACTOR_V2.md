# DirtFloor Mesh Combiner Refactoring Plan v3

## Date: 2025-03-06

## Current Problems

### 1. Multiple Overlapping Combiners
- Logs show combiners being created at nearly identical positions
- Example: `(1677.64, 39.67, -130.43)` and `(1680.19, 38.09, -128.51)` - only ~3m apart
- "Overflow" combiners being created unnecessarily
- Duplicate detection thresholds (2m, 3m) are too small

### 2. Paint Applied to Wrong/Multiple Layers
- `PaintAllLayersAt` was painting to ALL overlapping combiners
- Each combiner has its own paint mask texture
- Multiple paint masks at same position = Z-fighting

### 3. Individual Disk Meshes Not Hidden
- `CustomHeightmap` component renders individual disk mesh
- `CultivatedOverlay` sometimes visible
- `GroundCollider` sometimes has MeshRenderer enabled
- These all Z-fight with the combiner mesh

### 4. Zone-Based Approach Flaws (v2)
- Arbitrary 64m zones don't match terrain connectivity
- Disks in same zone but 50m apart get wrongly combined
- Disks at zone boundaries get split into different combiners
- Zone-based approach cannot handle disconnected terrain correctly

## New Architecture: Connectivity-Based Grouping (v3)

### Core Principle
**Disks that TOUCH get combined. Disks that don't touch stay separate.**

### Key Changes

#### 1. Connectivity Detection Instead of Zone Grid
```csharp
// Check if disk edges are close enough to connect
float edgeToEdge = horizontalDist - diskRadius - existingDiskRadius;
bool shouldConnect = (edgeToEdge <= CONNECTION_DISTANCE) && (verticalDist <= MAX_VERTICAL_DISTANCE);
```

Constants:
- `CONNECTION_DISTANCE = 1.0f` - Edge-to-edge threshold
- `MAX_VERTICAL_DISTANCE = 6f` - Different floors stay separate

#### 2. Bridging Detection
When a new disk connects to MULTIPLE existing combiners, it BRIDGES them:
- All connected combiners get MERGED into one
- No more duplicate/overlapping combiners

```csharp
// Find ALL combiners this disk connects to
var connectedCombiners = FindAllConnectedCombiners(newDisk);

if (connectedCombiners.Count > 1)
{
    // This disk bridges multiple groups - merge them!
    MergeCombiners(connectedCombiners);
}
```

#### 3. Paint Routing
Paint operations search ALL combiners but only paint to the one with:
- Terrain at the paint XZ position
- Terrain closest to the paint Y position

### Implementation Status

#### Phase 1: Zone Registry Rewrite ? DONE
Rewrote `DirtFloorZoneRegistry.cs`:
- Removed zone grid concept
- Added connectivity-based grouping
- Added `FindConnectableCombiner()` - finds existing combiner to connect to
- Added `CheckAndMergeConnectedCombiners()` - detects and merges bridged groups
- Added `MergeCombiners()` - merges two combiners into one

#### Phase 2: Combiner Integration ? DONE
Updated `DirtFloorMeshCombiner.Core.cs`:
- Added `ZoneKey` property (now just a unique ID)
- Added `AddDiskInternal(disk)` - simple add without overflow logic
- Added `RemoveDiskInternal(disk)` - simple remove
- Added `RemoveAllDisksInternal()` - for merging
- Added `GetCombinedBounds()` - for connectivity checks

#### Phase 3: Paint Routing ? DONE
Updated `DirtFloorMeshCombiner.Static.cs`:
- `PaintAllLayersAt()` uses `DirtFloorZoneRegistry.GetBestCombinerForPaint()`
- Falls back to iteration if needed

### Build Status: ? SUCCESSFUL (All Phases Complete)

## Summary of Changes

### Architecture Simplification (v3)
The combiner system was significantly simplified by:

1. **Removing overflow handling** - Connectivity-based approach naturally handles size limits
2. **Removing neighbor blending** - Connected disks are in the same combiner, no seams
3. **Centralizing disk management in zone registry** - All disk add/remove goes through `DirtFloorZoneRegistry`
4. **Simplifying paint routing** - Zone registry provides O(1) combiner lookup for paint operations

### Files Modified
- `DirtFloorMeshCombiner.Core.cs` - Removed `_neighborCombiners` field
- `DirtFloorMeshCombiner.DiskManagement.cs` - Simplified to use zone registry, removed overflow/neighbor code
- `DirtFloorMeshCombiner.Static.cs` - Simplified to redirect to zone registry
- `DirtFloorMeshCombiner.Lifecycle.cs` - Removed neighbor cleanup code
- `DirtFloorTerrainManager.cs` - Updated to use zone registry for disk management
- `DirtFloorController.cs` - Updated to use zone registry, removed `CheckAndMergeCombiners` calls

### Code Reduction
- Removed ~300 lines of overflow handling code
- Removed ~200 lines of neighbor blending code
- Removed ~100 lines of redundant merge checking code

### Benefits
1. **Simpler code flow** - One path for disk management (zone registry)
2. **No duplicate combiners** - Zone registry prevents overlapping combiners
3. **No Z-fighting** - Single paint layer per position
4. **Better performance** - No redundant merge operations

## Completed Cleanup Tasks

### Phase 5: Remove Deprecated Code ? DONE
**DirtFloorMeshCombiner.DiskManagement.cs**:
- [x] Remove `HandleOverflowDisk()` - no longer needed
- [x] Remove `WouldExceedMaxSize()` checks - connectivity handles this
- [x] Simplify `AddDisk()` to just call zone registry
- [x] Remove old cache integration calls (cache now uses registry)

**DirtFloorMeshCombiner.Static.cs**:
- [x] Remove `GetOrCreateCombinerFor()` - now redirects to zone registry
- [x] Remove `CheckAndMergeCombiners()` - zone registry handles merging
- [x] Simplify `GetCombinerContaining()` - uses zone registry
- [x] Remove `FindOrCreateOverflowCombiner()` - no longer needed

**DirtFloorTerrainManager.cs**:
- [x] Remove `MergeWith()` calls - zone registry handles merging
- [x] Remove `RegisterNeighborCombiner()` calls - no neighbors in connectivity model
- [x] Remove `WouldExceedMaxSize()` checks - connectivity handles sizing

**DirtFloorController.cs**:
- [x] Remove `CheckAndMergeCombiners()` calls - zone registry handles merging
- [x] Update to use `DirtFloorZoneRegistry.AddDisk()` instead of `GetOrCreateCombinerFor()`

### Phase 6: Remove Neighbor Blending ? DONE
**DirtFloorMeshCombiner.Core.cs**:
- [x] Remove `_neighborCombiners` field

**DirtFloorMeshCombiner.DiskManagement.cs**:
- [x] Remove `RegisterNeighborCombiner()` / `UnregisterNeighborCombiner()`
- [x] Remove `BlendEdgesWithNeighbor()` - connectivity means no seams
- [x] Remove `BlendWithAllNeighbors()`
- [x] Remove `GetOverlapBounds()`
- [x] Remove `GetDistanceToEdge()`

**DirtFloorMeshCombiner.Lifecycle.cs**:
- [x] Remove neighbor cleanup in `OnDestroy()`

### Phase 7: Simplify Cache Integration ? TODO
- [ ] Cache stores: mesh data, paint mask, heights, bounds
- [ ] Cache restores via zone registry (not directly to combiner)
- [ ] Remove cache's own combiner tracking
- [ ] Use combiner's `ZoneKey` as cache key

## Test Scenarios

### Scenario 1: Adjacent Disks
- Place disk A at (0, 0, 0) with radius 5
- Place disk B at (9, 0, 0) with radius 5
- Edge-to-edge distance = 9 - 5 - 5 = -1 (overlapping)
- **Expected:** Both in SAME combiner ?

### Scenario 2: Distant Disks in Same Zone
- Place disk A at (0, 0, 0) with radius 5
- Place disk B at (50, 0, 0) with radius 5
- Edge-to-edge distance = 50 - 5 - 5 = 40m (far apart)
- **Expected:** DIFFERENT combiners ?

### Scenario 3: Bridging Disk
- Place disk A at (0, 0, 0)
- Place disk B at (20, 0, 0) (separate from A)
- Place disk C at (10, 0, 0) (touches both A and B)
- **Expected:** A, B, C all in SAME combiner after C is placed ?

### Scenario 4: Multi-Level
- Place disk A at (0, 0, 0)
- Place disk B at (5, 10, 0) (10m higher)
- **Expected:** DIFFERENT combiners (different floors) ?

### Data Structures

```csharp
public static class DirtFloorZoneRegistry
{
    // Zone key ? Combiner mapping
    private static Dictionary<string, DirtFloorMeshCombiner> s_zoneCombiners 
        = new Dictionary<string, DirtFloorMeshCombiner>();
    
    // Disk ? Zone key mapping (for removal)
    private static Dictionary<DirtFloorController, string> s_diskToZone 
        = new Dictionary<DirtFloorController, string>();
    
    // Constants
    private const float ZONE_SIZE = 64f;        // Horizontal zone size
    private const float ELEVATION_LAYER = 6f;   // Vertical layer threshold
    
    public static string GetZoneKey(Vector3 position)
    {
        int zoneX = Mathf.FloorToInt(position.x / ZONE_SIZE);
        int zoneZ = Mathf.FloorToInt(position.z / ZONE_SIZE);
        int elevationLayer = Mathf.FloorToInt(position.y / ELEVATION_LAYER);
        return $"{zoneX}_{zoneZ}_{elevationLayer}";
    }
    
    public static DirtFloorMeshCombiner GetOrCreateCombiner(Vector3 position)
    {
        string key = GetZoneKey(position);
        if (s_zoneCombiners.TryGetValue(key, out var existing))
            return existing;
        
        // Create new combiner for this zone
        var go = new GameObject($"DirtFloorCombiner_{key}");
        var combiner = go.AddComponent<DirtFloorMeshCombiner>();
        combiner.ZoneKey = key;
        s_zoneCombiners[key] = combiner;
        return combiner;
    }
    
    public static DirtFloorMeshCombiner GetCombinerAt(Vector3 position)
    {
        string key = GetZoneKey(position);
        s_zoneCombiners.TryGetValue(key, out var combiner);
        return combiner;
    }
    
    public static void AddDiskToCombiner(DirtFloorController disk)
    {
        if (disk == null) return;
        
        string key = GetZoneKey(disk.transform.position);
        var combiner = GetOrCreateCombiner(disk.transform.position);
        
        if (!combiner.ConnectedDisks.Contains(disk))
        {
            combiner.AddDiskSimple(disk); // New simple add method
        }
        
        s_diskToZone[disk] = key;
    }
    
    public static void RemoveDisk(DirtFloorController disk)
    {
        if (disk == null) return;
        
        if (s_diskToZone.TryGetValue(disk, out string key))
        {
            if (s_zoneCombiners.TryGetValue(key, out var combiner))
            {
                combiner.RemoveDiskSimple(disk);
                
                if (combiner.ConnectedDisks.Count == 0)
                {
                    s_zoneCombiners.Remove(key);
                    Object.Destroy(combiner.gameObject);
                }
            }
            s_diskToZone.Remove(disk);
        }
    }
    
    public static void Clear()
    {
        foreach (var combiner in s_zoneCombiners.Values)
        {
            if (combiner != null)
                Object.Destroy(combiner.gameObject);
        }
        s_zoneCombiners.Clear();
        s_diskToZone.Clear();
    }
}
```

### Migration Path

1. Create `DirtFloorZoneRegistry.cs` with new logic
2. Add `AddDiskSimple()` / `RemoveDiskSimple()` to combiner (no overflow, no neighbor logic)
3. Add `ZoneKey` property to combiner
4. Update `DirtFloorController.ApplyBaseVisibility()` to use registry
5. Update `PaintAllLayersAt` to use `GetCombinerAt`
6. Test thoroughly
7. Remove old complex logic from combiner files
8. Update cache system to use zone keys

### Expected Results

- ONE combiner per zone+elevation (no duplicates)
- Paint always goes to correct combiner
- No Z-fighting (only one mesh renders per area)
- Simpler code, easier to debug
- Consistent disk hiding

