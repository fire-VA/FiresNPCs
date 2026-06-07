# Combiner Architecture Refactor

## STATUS: IMPLEMENTED - Phase 1 Complete

## Summary of Changes

The architecture has been updated so that **disks are absorbed into combiners and destroyed permanently**.

### Key Changes Made:

1. **`DirtFloorMeshCombiner.AbsorbAndDestroyDisk()`** - New method that:
   - Extracts all data from a disk (position, radius, height, paint mask)
   - Stores as `TerrainDisk` struct in the combiner
   - Merges disk paint into combiner paint mask
   - Destroys the disk GameObject via `ZNetScene.Destroy()` (removes ZDO)

2. **`DirtFloorZoneRegistry.AddDisk()`** - Updated to:
   - Call `AbsorbAndDestroyDisk()` instead of just adding disk reference
   - Destroy duplicate disks if combiner was already restored from cache
   - No longer maintains `_connectedDisks` list (disks don't exist)

3. **`RestorePaintMaskFromCache()`** - Fixed to:
   - Return `false` if paint data is empty/black
   - Allow fallthrough to TerrainComp loading

4. **`CheckAndMergeWithExistingCombiners()`** - Fixed to:
   - Skip entirely for combiners restored from cache (authoritative)
   - Prevent destruction of valid cached combiners

5. **`RemoveOverlappingCombiners()`** - Fixed to:
   - Skip combiners that are restored from cache
   - Use smaller threshold to prevent aggressive cleanup

### Data Flow

```
PLACEMENT:
Player places disk ? DirtFloorController created ? Joins combiner
? AbsorbAndDestroyDisk() called ? Disk data extracted ? TerrainDisk stored
? Paint mask merged ? Disk GameObject DESTROYED ? ZDO removed from world

ZONE RELOAD:
Zone loads ? Cache checked ? Combiner restored from cache data
? Mesh/paint/grass created from cache ? NO DISKS LOADED (they don't exist)

PAINT OPERATION:
Player paints ? Combiner's paint mask updated ? Saved to cache
? No disk ZDOs to update (they don't exist)
```

### Migration for Existing Worlds

Existing disk ZDOs will still load initially, but:
1. When they call `AddDisk()`, they get absorbed and destroyed
2. After one zone load cycle, all disks become combiner data
3. Future zone loads only restore combiners from cache

## Quick Fix (Immediate)

1. **Race conditions on zone reload** - Multiple disks load simultaneously, each trying to create/find combiners
2. **Duplicate combiner creation** - Before the first combiner is registered, other disks create more
3. **TerrainComp spam** - Vanilla detects multiple terrain objects and destroys them repeatedly  
4. **Collision loss** - Aggressive cleanup destroys valid combiners thinking they're duplicates
5. **Paint not persisting** - Paint saved to disk ZDO but combiner restores from cache without paint

## Current (Wrong) Architecture

```
Zone Load:
  Disk1.Awake() ? GetCachedCombinerForDisk() ? Creates Combiner1
  Disk2.Awake() ? GetCachedCombinerForDisk() ? Cache miss, Creates Combiner2 (race!)
  Disk3.Awake() ? GetCachedCombinerForDisk() ? Cache miss, Creates Combiner3 (race!)
  
  Later: CheckAndMergeWithExistingCombiners() destroys "duplicates"
  Later: RemoveOverlappingCombiners() destroys more
  Result: Only 1 combiner survives but may have lost disks/data
```

## Target Architecture

**Combiners are the ONLY persistent objects. Disks are absorbed and destroyed.**

```
Terrain Placement:
  Player places disk ? Disk joins/creates Combiner ? Disk data absorbed into Combiner
  ? Disk GameObject DESTROYED (ZNetScene.Destroy)
  ? Combiner saves to its own ZDO

Zone Load:
  Combiner ZDO loads ? Combiner recreated from its own data
  ? NO DISKS INVOLVED
  ? Mesh, paint, grass all restored from Combiner ZDO
```

## Implementation Plan

### Phase 1: Give Combiners Their Own ZDO Persistence

1. **Register a combiner prefab** with ZNetScene
2. **Add ZNetView** to combiners for network sync
3. **Save to combiner ZDO**:
   - Mesh vertices/triangles (compressed)
   - Paint mask data
   - Grid dimensions and heights
   - List of absorbed disk positions/radii (for reference only)
4. **Load from combiner ZDO** on zone reload

### Phase 2: Absorb Disks Into Combiners

When `AddDisk()` is called:
1. Extract all relevant data from disk (position, radius, paint mask, grass settings)
2. Store as `AbsorbedDiskData` struct in combiner
3. **Destroy the disk GameObject** via `ZNetScene.instance.Destroy()`
4. Regenerate combiner mesh with new disk data
5. Save combiner to ZDO

### Phase 3: Remove Disk-Based Loading

1. Remove `DirtFloorController.Start()` combiner joining logic
2. Remove `DelayedGrassInit()` combiner registration
3. Remove `DirtFloorCombinerCache` disk-to-group mapping
4. Combiner restoration uses ONLY combiner ZDO data

### Phase 4: Cleanup

1. Remove `_connectedDisks` list (only `_absorbedDisks` data remains)
2. Remove disk visibility management (no disks to hide)
3. Simplify zone registry (no disk lookup needed)
4. Remove race condition mitigations (no more races!)

## Data Structures

### AbsorbedDiskData (replaces DirtFloorController reference)
```csharp
[Serializable]
public struct AbsorbedDiskData
{
    public Vector3 Position;
    public float Radius;
    public float HeightOffset;
    public bool ShowGrass;
    public string GrassType;
    // Paint mask is merged into combiner's paint mask
}
```

### Combiner ZDO Keys
```csharp
private static readonly int ZDO_Version = "combiner_version".GetStableHashCode();
private static readonly int ZDO_MeshData = "combiner_mesh".GetStableHashCode();
private static readonly int ZDO_PaintMask = "combiner_paint".GetStableHashCode();
private static readonly int ZDO_Heights = "combiner_heights".GetStableHashCode();
private static readonly int ZDO_GridInfo = "combiner_grid".GetStableHashCode();
private static readonly int ZDO_DiskCount = "combiner_diskcount".GetStableHashCode();
private static readonly int ZDO_DiskData = "combiner_disks".GetStableHashCode();
```

## Migration Path

For existing worlds with disk ZDOs:
1. On first load after update, detect existing disk ZDOs
2. Create combiners from disk data
3. Mark disk ZDOs for deletion (they'll be cleaned up naturally)
4. Combiners save to their own ZDOs
5. Future loads use combiner ZDOs only

## Benefits

1. **No race conditions** - Only one object type loads per terrain area
2. **No duplicate combiners** - Combiner is authoritative
3. **No TerrainComp spam** - Fewer objects, cleaner lifecycle
4. **Paint persists correctly** - Saved directly to combiner ZDO
5. **Simpler code** - No disk management, visibility toggling, etc.
6. **Better performance** - Fewer GameObjects, no disk-to-combiner lookups

## Files to Modify

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.Core.cs` | Add ZNetView, ZDO save/load, AbsorbedDiskData |
| `DirtFloorMeshCombiner.Lifecycle.cs` | Remove duplicate detection, add ZDO restoration |
| `DirtFloorMeshCombiner.DiskManagement.cs` | Change AddDisk to absorb and destroy |
| `DirtFloorController.cs` | Remove combiner registration, simplify to placement-only |
| `DirtFloorZoneRegistry.cs` | Simplify to combiner-only management |
| `DirtFloorCombinerCache.cs` | Remove or repurpose for local caching only |
| `DirtFloorPatches.cs` | Update for new architecture |

## Prefab Registration

The combiner needs to be registered as a spawnable prefab for ZDO persistence:

```csharp
// In plugin initialization or ZNetScene.Awake patch
public static void RegisterCombinerPrefab()
{
    // Create the prefab
    var prefab = new GameObject("DirtFloorCombiner_Prefab");
    prefab.AddComponent<ZNetView>();
    prefab.AddComponent<DirtFloorMeshCombiner>();
    
    // Required components for the prefab to work
    var nview = prefab.GetComponent<ZNetView>();
    nview.m_persistent = true;
    nview.m_type = ZDO.ObjectType.Default;
    
    // Register with ZNetScene
    // This requires a custom hash since we're not using asset bundle
    ZNetScene.instance.m_namedPrefabs[COMBINER_PREFAB_HASH] = prefab;
}
```

## Testing Checklist

- [ ] Place terrain disk - should be absorbed into combiner immediately
- [ ] Paint terrain - paint persists on combiner
- [ ] Leave zone and return - combiner restores from its own ZDO
- [ ] Multiple players - combiner syncs via ZNetView
- [ ] Delete terrain - combiner handles removal
- [ ] No "Found another terrain compiler" spam
- [ ] No collision loss on zone reload
- [ ] Existing worlds migrate correctly
