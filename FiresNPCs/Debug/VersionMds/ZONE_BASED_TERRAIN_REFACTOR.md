# Zone-Based Custom Terrain System Refactor

## Current Status Summary

### ? What's Working Now

| Feature | Status | Notes |
|---------|--------|-------|
| **Collision Mesh** | ? Working | Uses same mesh as visual - no more floating/sinking |
| **Height Operations** | ? Working | Level, raise, lower work on custom terrain |
| **Paint Interception** | ? Working | IH paint commands intercepted correctly |
| **Paint Mask Updates** | ? Working | Paint pixels being written (3000+ pixels per operation) |
| **ZoneSystem.GetGroundHeight Patch** | ? Working | Returns custom terrain height |
| **Material Setup** | ? Working | Using vanilla Heightmap material |
| **Grass Spawning** | ? Working | Spawns at unified heights |
| **LOD System** | ? Working | Distance-based LOD with DirtFloorLODManager |
| **Cache System** | ? Working | 182 groups cached and restored |

### ? What's NOT Working

| Issue | Symptoms | Root Cause |
|-------|----------|------------|
| **Paint Not Rendering** | Grass removed but paved texture doesn't show | UV mapping mismatch between mesh and paint mask |
| **Overlapping Edges** | Visible seams between terrain chunks | Disk-based system creates arbitrary boundaries |
| **Multiple Combiners** | Disks split across combiners incorrectly | Overflow logic creates fragmentation |
| **Weird Terrain Shapes** | Non-uniform edges, bulging | Disk radius blending creates organic but unpredictable shapes |

### ?? Partially Working

| Feature | Status | Issue |
|---------|--------|-------|
| **Paint Visual** | Paint data saved but not visible | Shader not reading _ClearedMaskTex correctly |
| **Edge Blending** | Works but creates weird overlaps | Blending between arbitrary disk shapes |

---

## Implementation Progress

### Phase 1: Zone System Core ? COMPLETE

| File | Status | Description |
|------|--------|-------------|
| `CustomTerrainZone.cs` | ? Created | Zone terrain component with 64x64m grid |
| `CustomTerrainZoneManager.cs` | ? Created | Zone lookup, creation, and operations |

**Key Features Implemented:**
- Zone-aligned 64x64m grid (matches vanilla)
- 65x65 vertex grid (for edge stitching)
- Height data with `_hasTerrainAt` tracking
- Paint mask (128x128 texture)
- Mesh generation from height data
- Edge stitching to neighbors
- Zone registration and linking

### Phase 2: Route Operations to Zones ? COMPLETE

| File | Status | Description |
|------|--------|-------------|
| `DirtFloorTerrainManager.cs` | ? Updated | Routes to zone system when `UseZoneBasedTerrain = true` |
| `InfinityHammerCompat.cs` | ? Updated | `GetGroundHeight` patch checks zone terrain |

**Operations Now Routed:**
- `LevelArea()` ? `CustomTerrainZoneManager.LevelArea()`
- `RaiseArea()` ? `CustomTerrainZoneManager.RaiseArea()`
- `LowerArea()` ? `CustomTerrainZoneManager.RaiseArea()` (negative amount)
- `PaintArea()` ? `CustomTerrainZoneManager.PaintArea()`
- `IsPositionOverCustomTerrain()` ? checks both zone and disk systems
- `GetTerrainHeightAt()` ? NEW method checks both systems
- `ZoneSystem.GetGroundHeight` patch ? checks zone terrain first

**Toggle Flag:**
```csharp
// In DirtFloorTerrainManager.cs
public static bool UseZoneBasedTerrain = true;  // Set to false for legacy disk-based
```

### Phase 3: Test and Verify ? NEXT

**Testing Checklist:**
- [ ] Level tool creates flat terrain at correct height
- [ ] Paint tool applies paint visually
- [ ] Raise/lower tools modify existing terrain
- [ ] Edge stitching between adjacent zones
- [ ] No gaps or overlaps between zones
- [ ] Collision matches visual mesh

```csharp
public class CustomTerrainZone : MonoBehaviour
{
    // Zone identification
    public Vector2i ZoneId;  // Same as vanilla zone system
    public const float ZONE_SIZE = 64f;
    public const int GRID_SIZE = 65;  // Matches vanilla Heightmap
    
    // Height data (single source of truth)
    private float[] _heights = new float[GRID_SIZE * GRID_SIZE];
    private bool[] _modified = new bool[GRID_SIZE * GRID_SIZE];
    
    // Paint data
    private Texture2D _paintMask;  // 64x64 or 128x128
    
    // Mesh
    private Mesh _mesh;
    private MeshCollider _collider;
    private MeshRenderer _renderer;
    
    // Edge stitching
    private CustomTerrainZone _neighborNorth, _neighborSouth, _neighborEast, _neighborWest;
}
```

**Key Methods:**
- `GetOrCreateZone(Vector3 worldPos)` - Gets zone for position
- `ModifyHeight(Vector3 worldPos, float delta)` - Modifies single point
- `LevelArea(Vector3 center, float radius, float height)` - Levels circular area
- `PaintArea(Vector3 center, float radius, PaintType type)` - Paints area
- `StitchToNeighbors()` - Ensures edge vertices match adjacent zones
- `RegenerateMesh()` - Rebuilds mesh from height data

### Phase 2: Zone Manager (NEW)

**File:** `CustomTerrainZoneManager.cs`

```csharp
public static class CustomTerrainZoneManager
{
    private static Dictionary<Vector2i, CustomTerrainZone> _zones;
    
    // Get zone at world position
    public static CustomTerrainZone GetZoneAt(Vector3 worldPos);
    
    // Create zone if doesn't exist
    public static CustomTerrainZone GetOrCreateZone(Vector3 worldPos);
    
    // Get all zones in radius
    public static List<CustomTerrainZone> GetZonesInRadius(Vector3 center, float radius);
    
    // Stitch all adjacent zones
    public static void StitchAllZones();
}
```

### Phase 3: Refactor DirtFloorController

**Change From:** Terrain owner with own mesh
**Change To:** Terrain modifier that writes to CustomTerrainZone

```csharp
public class DirtFloorController : MonoBehaviour
{
    // No longer owns mesh - just stores placement data
    public float DirtRadius;
    public PaintType CurrentPaint;
    
    void Start()
    {
        // Find or create zone(s) for this disk
        var zones = CustomTerrainZoneManager.GetZonesInRadius(transform.position, DirtRadius);
        
        // Apply this disk's terrain to zone(s)
        foreach (var zone in zones)
        {
            zone.ApplyDisk(transform.position, DirtRadius, transform.position.y);
        }
    }
}
```

### Phase 4: Remove DirtFloorMeshCombiner

The combiner becomes unnecessary because:
- Zones are fixed size (no overflow)
- Zones tile perfectly (no blending issues)
- Zones stitch at edges (no gaps)

---

## Migration Path

### Step 1: Create Zone System (Additive)
- Add CustomTerrainZone class
- Add CustomTerrainZoneManager
- Don't remove existing code yet

### Step 2: Route Operations to Zones
- Modify DirtFloorTerrainManager to write to zones
- Keep DirtFloorMeshCombiner as fallback

### Step 3: Verify Zone System Works
- Test level, paint, raise, lower
- Test edge stitching
- Test persistence

### Step 4: Remove Old System
- Remove DirtFloorMeshCombiner
- Simplify DirtFloorController
- Update cache system

---

## Technical Details

### Zone Coordinate System
```csharp
// Convert world position to zone ID
public static Vector2i WorldToZone(Vector3 worldPos)
{
    return new Vector2i(
        Mathf.FloorToInt(worldPos.x / ZONE_SIZE),
        Mathf.FloorToInt(worldPos.z / ZONE_SIZE)
    );
}

// Get zone origin (world position of zone corner)
public static Vector3 ZoneToWorld(Vector2i zoneId)
{
    return new Vector3(
        zoneId.x * ZONE_SIZE,
        0,
        zoneId.y * ZONE_SIZE
    );
}
```

### Height Grid Layout
```
Zone (0,0):
  Grid[0,0] at world (0, 0)
  Grid[64,0] at world (64, 0)  ? SHARED with Zone (1,0) Grid[0,0]
  Grid[0,64] at world (0, 64)  ? SHARED with Zone (0,1) Grid[0,0]
  Grid[64,64] at world (64, 64) ? SHARED with zones (1,0), (0,1), (1,1)
```

### Edge Stitching
```csharp
void StitchToNeighbor(CustomTerrainZone neighbor, Direction dir)
{
    // North edge: my row 64 = their row 0
    if (dir == Direction.North && neighbor != null)
    {
        for (int x = 0; x < GRID_SIZE; x++)
        {
            int myIdx = 64 * GRID_SIZE + x;
            int theirIdx = x;
            
            // Average heights
            float avg = (_heights[myIdx] + neighbor._heights[theirIdx]) / 2f;
            _heights[myIdx] = avg;
            neighbor._heights[theirIdx] = avg;
        }
    }
}
```

---

## Paint System Fix

### Current Issue
Paint mask is 256x256 mapped to arbitrary combined bounds.
Mesh UVs are calculated from combined bounds.
But mesh vertices only exist where disks overlap - UVs don't match.

### Zone-Based Fix
- Paint mask is per-zone (64x64 or 128x128)
- UV = (localX / ZONE_SIZE, localZ / ZONE_SIZE)
- Every mesh vertex has exact UV mapping
- Paint always renders correctly

---

## Files to Create

1. `CustomTerrainZone.cs` - Zone terrain component
2. `CustomTerrainZoneManager.cs` - Zone lookup and creation
3. `CustomTerrainZonePersistence.cs` - Save/load zone data

## Files to Modify

1. `DirtFloorTerrainManager.cs` - Route to zone system
2. `DirtFloorController.cs` - Become modifier, not owner
3. `InfinityHammerCompat.cs` - Use zone system

## Files to Eventually Remove

1. `DirtFloorMeshCombiner.*.cs` - All 9 partial class files
2. `DirtFloorCombinerCache.cs` - Zone system handles caching

---

## Timeline

| Phase | Task | Complexity | Priority |
|-------|------|------------|----------|
| 1 | Create CustomTerrainZone | Medium | HIGH |
| 2 | Create ZoneManager | Low | HIGH |
| 3 | Route operations to zones | Medium | HIGH |
| 4 | Test and verify | Low | HIGH |
| 5 | Remove old combiner | Low | MEDIUM |
| 6 | Optimize and polish | Medium | LOW |

---

## Success Criteria

- [ ] No visible seams between terrain areas
- [ ] Paint renders correctly where applied
- [ ] Collision matches visual exactly
- [ ] Edge stitching works with vanilla terrain
- [ ] Performance equal or better than current
- [ ] All IH tools work correctly
