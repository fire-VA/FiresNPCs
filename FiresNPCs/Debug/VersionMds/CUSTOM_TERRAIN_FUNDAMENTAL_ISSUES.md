# Custom Terrain System - Fundamental Issues Analysis

## Date: 2025-03-03

## Visual Evidence
The screenshot shows clear evidence of the core problems:
1. Paint appears confined to individual disk boundaries (yellowish paved area vs dark grass areas)
2. Grass clutter spawns inconsistently across disk boundaries
3. Biome overlay paint doesn't propagate across the combined terrain

---

## Root Cause Analysis

### Problem 1: Paint Mask Per-Combiner, Not Per-World-Position

**Current Architecture:**
```
DirtFloorController (Disk)
    ??? Has its own paint mask texture (256x256)
    ??? Paint is stored in local UV coordinates

DirtFloorMeshCombiner
    ??? Combines multiple disks into one mesh
    ??? Has its own _combinedPaintMask (256x256)
    ??? UV coordinates map world position to texture
```

**The Flaw:**
When disks are combined, each disk's paint data is sampled and composited into the combiner's paint mask. BUT:
- The UV mapping changes when bounds change
- Paint from overlapping regions is sampled from whichever disk "owns" that UV coordinate
- There's no proper blending at disk boundaries

### Problem 2: Paint UV Mapping Breaks on Bounds Change

When a new disk is added:
1. `_combinedBounds` expands
2. UV coordinates for existing pixels change (same world position = different UV)
3. `RemapPaintMaskToNewBounds()` tries to fix this, BUT:
   - It samples from old UV space
   - Any paint at the edges gets stretched/compressed
   - New areas have no paint data

### Problem 3: Grass Spawns Per-Disk, Not Per-Combiner

```csharp
// Current grass spawning
SpawnGrassOnMesh() {
    // Samples paint mask at each vertex
    // BUT the paint mask may not match the actual biome overlay
}
```

**The Flaw:**
- Grass spawning checks the paint mask color
- If paint mask says "no paint" (default), it spawns grass
- Biome overlay paint is stored in `BiomePaintPersistence`, not the disk paint mask
- These two systems are disconnected!

### Problem 4: Two Separate Paint Systems That Don't Talk

**System A: Terrain Paint (paved/cultivated/dirt)**
- Stored in `DirtFloorController._paintMask` 
- Combined into `DirtFloorMeshCombiner._combinedPaintMask`
- Used by terrain shader for texture blending
- Used by grass system to decide where to spawn

**System B: Biome Overlay Paint (plains/lava/snow)**
- Stored in `BiomePaintPersistence` as circles with positions
- Applied to heightmap vertex colors
- Applied to combiner vertex colors via `RefreshBiomeColors()`
- NOT connected to grass spawning!

---

## The Correct Architecture

### What Should Happen When Disks Overlap (< 2m vertical):

1. **Merge into single combiner** ? (Already happens)
2. **Regenerate height grid** ? (Already happens)
3. **CLEAR ALL PAINT in overlapping region** ? (NOT happening)
4. **CLEAR ALL GRASS in overlapping region** ? (NOT happening)
5. **Recalculate vertex colors for combined area** ? (Partial)
6. **Respawn grass for combined area** ? (NOT properly)

### What Should Happen When Painting:

1. **Paint writes to combiner's paint mask** ? (Happens)
2. **Paint clears grass in affected radius** ? (Inconsistent)
3. **Paint updates vertex colors** ? (Only for biome overlay)
4. **Grass respawns based on new paint** ? (Disconnected)

---

## Proposed Fix: Unified Paint System

### Step 1: Single Source of Truth for Paint

Create a world-space paint query system:
```csharp
public static class TerrainPaintQuery
{
    // Returns paint type at world position, checking:
    // 1. BiomePaintPersistence (biome overlays)
    // 2. DirtFloorMeshCombiner paint mask
    // 3. Vanilla terrain paint mask
    public static PaintInfo GetPaintAt(Vector3 worldPos);
}
```

### Step 2: Clear Paint on Disk Overlap

In `DirtFloorMeshCombiner.AddDisk()`:
```csharp
// When adding overlapping disk:
// 1. Find overlap region
// 2. Clear paint mask in overlap region (set to default)
// 3. Clear grass in overlap region
// 4. Mark area for repaint/regrass
```

### Step 3: Unified Grass Spawning

```csharp
public void SpawnGrassOnMesh()
{
    foreach (vertex in mesh)
    {
        Vector3 worldPos = vertex.position;
        
        // Check ALL paint sources
        var biomePaint = BiomePaintPersistence.GetPaintAtPosition(worldPos.x, worldPos.z);
        var terrainPaint = GetPaintMaskAt(worldPos);
        
        // Determine if grass should spawn
        bool shouldHaveGrass = ShouldSpawnGrass(biomePaint, terrainPaint);
        
        if (shouldHaveGrass)
            SpawnGrassAt(worldPos);
    }
}
```

### Step 4: Paint Propagation on Overlap

When disks within 2m merge:
1. Get all paint data from both disks
2. Clear the combined paint mask
3. Re-apply all paints to the new combined UV space
4. Regenerate grass

---

## Implementation Priority

### Phase 1: Fix the Immediate Pain (Quick Fixes)
1. Clear paint mask when disks merge
2. Clear grass when disks merge
3. Force repaint/regrass after merge

### Phase 2: Unify Paint Systems (Medium)
1. Create `TerrainPaintQuery` 
2. Connect biome paint to grass spawning
3. Ensure paint propagates across disk boundaries

### Phase 3: Proper Architecture (Long-term)
1. Single world-space paint storage
2. Paint mask streaming/LOD
3. Proper UV-independent paint storage

---

## Quick Test to Verify Issue

1. Place disk A, paint it paved
2. Place disk B overlapping disk A
3. Expected: Paved area should extend across both disks
4. Actual: Paved area only on disk A's original region

If this test fails, the issue is confirmed.

---

## Files to Modify

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.DiskManagement.cs` | Clear paint/grass on merge |
| `DirtFloorMeshCombiner.Paint.cs` | Better UV remapping, paint propagation |
| `DirtFloorMeshCombiner.Grass.cs` | Check biome paint before spawning |
| `BiomePaintPersistence.cs` | Expose paint data for grass system |
| `TerrainPaintQuery.cs` | NEW - unified paint query |
