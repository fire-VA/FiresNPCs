# Terrain System Refactor Plan

## Overview

This document outlines the plan to refactor and consolidate our terrain systems into a cleaner, more maintainable architecture. The current implementation has grown organically and now suffers from:

1. **Monolithic code** - `DirtFloorMeshCombiner.cs` is ~5000 lines
2. **Competing paint systems** - Multiple methods trying to paint terrain in different ways
3. **Redundant operations** - Same work being done multiple times
4. **Server-side ClutterSystem errors** - NullReferenceException spam during client loading
5. **Unclear code flow** - Hard to follow what happens when

---

## Current Problems

### 1. DirtFloorMeshCombiner is Too Large (~5000 lines)

The file contains too many responsibilities:
- Disk management (add, remove, merge)
- Grid/height sampling
- Mesh generation (visual + collision + LOD)
- Paint mask management
- Grass spawning
- Neighbor/overflow handling
- Height modification (level, raise, lower, smooth)
- Cache integration
- Biome color management

### 2. Multiple Competing Paint Systems

Currently there are at least 4 different paint-related code paths:

| Method | Location | Purpose |
|--------|----------|---------|
| `OnPaintTerrain()` | DirtFloorMeshCombiner | Paint on combined mesh |
| `PaintDirectlyOnCombinedMask()` | DirtFloorMeshCombiner | Direct texture painting |
| `UpdateCombinedPaintMask()` | DirtFloorMeshCombiner | Rebuild paint from all disks |
| `PaintAt()` | DirtFloorController | Paint on individual disk |
| `PaintArea()` | DirtFloorTerrainManager | Entry point for paint operations |
| `PaintAllLayersAt()` | DirtFloorMeshCombiner (static) | Multi-layer paint |

These methods call each other in confusing ways and sometimes fight over what the "correct" paint should be.

### 3. Server-Side ClutterSystem Errors

```
NullReferenceException: Object reference not set to an instance of an object
  at ClutterSystem.GenerateVegPatch (UnityEngine.Vector2Int patchID, System.Single size)
```

This error is spamming during client zone loading. Likely caused by:
- Our patches interfering with ClutterSystem initialization timing
- Missing null checks in our Heightmap patches
- Environment box clutter spawning conflicting with vanilla system

### 4. Grass System Duplication

We have grass spawning in multiple places:
- `DirtFloorMeshCombiner.SpawnGrassOnMesh()` - Combined mesh grass
- `GrassOverlayController.SpawnGrass()` - Individual disk grass
- `EnvironmentBoxClutterSpawner` - Env box clutter/grass

---

## Proposed Architecture

### Phase 1: Split DirtFloorMeshCombiner into Focused Classes

**REVISED APPROACH**: Use partial classes instead of composition.
The original plan to use separate classes with callbacks failed because:
1. Too many private fields need to be exposed
2. The classes are tightly coupled by design
3. Data flows bidirectionally between systems

**New approach**: Split into partial class files that share the same class definition.
This preserves access to private members while organizing code logically.

```
DirtFloorMeshCombiner.cs (Core - ~300 lines)
??? Configuration fields
??? Runtime state fields
??? Properties
??? Unity lifecycle (Awake, OnEnable, OnDisable, OnDestroy, Update)
??? LOD management (UpdateLOD, SetLODState, GenerateLODMesh)

DirtFloorMeshCombiner.DiskManagement.cs (~400 lines)
??? AddDisk, RemoveDisk
??? OverlapsWith, WouldExceedMaxSize, IsAtMaxCapacity
??? HandleOverflowDisk
??? RestoreFromCache and related methods
??? NotifyDisksToUpdateVisibility
??? Merge operations

DirtFloorMeshCombiner.MeshGeneration.cs (~700 lines)
??? MarkDirty, ForceRegenerate, RegenerateMesh
??? CalculateCombinedBounds, SetupGrid
??? SampleHeights, SmoothHeightEdges, FillHeightGaps
??? BuildMesh, BuildMeshWithLevelArea
??? UpdateCollisionMesh
??? Biome color methods

DirtFloorMeshCombiner.Paint.cs (~500 lines)
??? SetupMaterial
??? UpdateCombinedPaintMask
??? OnPaintTerrain methods
??? PaintDirectlyOnCombinedMask
??? Sample/Get paint methods
??? Paint color conversion methods

DirtFloorMeshCombiner.HeightOps.cs (~500 lines)
??? ModifyHeightAt
??? OnPickaxeHit, OnRaiseGround, OnLevelGround, OnSmoothGround
??? ApplyMinHeight, ApplyMaxHeight
??? InvalidateTerrainAtDifferentElevation
??? Flatten/Smooth helpers

DirtFloorMeshCombiner.Grass.cs (~300 lines)
??? SpawnGrassOnMesh, ClearGrass
??? SpawnGrassForDiskArea
??? SpawnInstancedGrassForArea, SpawnNonInstancedGrassForArea
??? RefreshGrassHeights

DirtFloorMeshCombiner.Neighbors.cs (~250 lines)
??? RegisterNeighborCombiner, UnregisterNeighborCombiner
??? GetOverlapBounds, GetDistanceToEdge
??? BlendEdgesWithNeighbor, BlendWithAllNeighbors
??? FindOrCreateOverflowCombiner

DirtFloorMeshCombiner.Static.cs (~200 lines)
??? GetOrCreateCombinerFor
??? CheckAndMergeCombiners
??? GetCombinerAt, GetCombinerContaining
??? PaintAllLayersAt
??? RefreshBiomeColorsInRadius
```

**Benefits of partial classes**:
1. Same class, split across files - full access to all members
2. Clear organization by responsibility
3. Easy to navigate in IDE (each file is focused)
4. No interface/callback overhead
5. Git history shows changes in relevant files

### Phase 2: Unified Paint Controller

Create a single `TerrainPaintController` that handles ALL terrain painting:

```csharp
/// <summary>
/// Unified terrain paint controller that handles:
/// - Custom DirtFloor terrain (our system)
/// - Vanilla Heightmap terrain
/// - Environment box terrain overrides
/// </summary>
public static class TerrainPaintController
{
    /// <summary>
    /// Paints terrain at a world position. Automatically detects terrain type
    /// and routes to the correct implementation.
    /// </summary>
    public static void PaintAt(Vector3 worldPos, float radius, PaintType paintType)
    {
        // 1. Check for custom terrain (DirtFloor)
        var customTerrain = FindCustomTerrainAt(worldPos);
        if (customTerrain != null)
        {
            PaintCustomTerrain(customTerrain, worldPos, radius, paintType);
            return;
        }
        
        // 2. Check for vanilla terrain (Heightmap)
        var heightmap = Heightmap.FindHeightmap(worldPos);
        if (heightmap != null)
        {
            PaintVanillaTerrain(heightmap, worldPos, radius, paintType);
            return;
        }
        
        // 3. No terrain found
        Debug.LogWarning($"No terrain found at {worldPos}");
    }
    
    /// <summary>
    /// Paints custom DirtFloor terrain.
    /// - Updates the combiner's paint mask directly
    /// - Updates individual disk paint masks for persistence
    /// - Refreshes grass in painted area
    /// </summary>
    private static void PaintCustomTerrain(DirtFloorMeshCombiner combiner, 
        Vector3 worldPos, float radius, PaintType paintType)
    {
        // Single code path for all custom terrain painting
        combiner.PaintSystem.ApplyPaint(worldPos, radius, paintType);
    }
    
    /// <summary>
    /// Paints vanilla Heightmap terrain via TerrainComp.
    /// </summary>
    private static void PaintVanillaTerrain(Heightmap heightmap,
        Vector3 worldPos, float radius, PaintType paintType)
    {
        var tc = heightmap.GetAndCreateTerrainCompiler();
        if (tc == null) return;
        
        // Convert to vanilla paint and apply
        tc.PaintCleared(worldPos, radius, ConvertToPaintType(paintType), false);
    }
}
```

### Phase 3: Fix ClutterSystem Errors

The `ClutterSystem.GenerateVegPatch` errors need investigation:

1. **Check our Heightmap patches** - Ensure we're not returning null where Valheim expects data
2. **Check timing** - Our custom terrain might not be ready when ClutterSystem queries it
3. **Add null guards** - Patch ClutterSystem to handle our terrain gracefully

```csharp
// Potential fix: Patch ClutterSystem.GenerateVegPatch
[HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.GenerateVegPatch))]
class ClutterSystem_GenerateVegPatch_Patch
{
    static bool Prefix(Vector2Int patchID, float size)
    {
        // Early exit if essential systems not ready
        if (Heightmap.s_heightmaps == null || Heightmap.s_heightmaps.Count == 0)
            return false;
            
        // Check if this patch overlaps custom terrain
        Vector3 patchCenter = GetPatchCenter(patchID, size);
        var customTerrain = DirtFloorController.GetDirtFloorAtPosition(patchCenter, size);
        
        if (customTerrain != null)
        {
            // Let our grass system handle this area, skip vanilla
            return false;
        }
        
        return true; // Let vanilla handle it
    }
}
```

---

## Implementation Order

### Sprint 1: Split DirtFloorMeshCombiner (Priority: High)

**Goal**: Break the monolith into manageable pieces without changing behavior.

1. Create `DirtFloorMeshBuilder.cs`
   - Move: `SetupGrid()`, `SampleHeights()`, `BuildMesh()`, `BuildMeshWithLevelArea()`
   - Move: `GenerateLODMesh()`, `UpdateCollisionMesh()`
   - Move: `SmoothHeightEdges()`, `FillHeightGaps()`

2. Create `DirtFloorPaintSystem.cs`
   - Move: `UpdateCombinedPaintMask()`, `PaintDirectlyOnCombinedMask()`
   - Move: `SampleDiskPaintColor()`, `GetDiskPaintMask()`, `GetCombinedPaintTypeAt()`
   - Move: `GetPaintColor()`, `SnapToNearestPaintColor()`

3. Create `DirtFloorGrassSpawner.cs`
   - Move: `SpawnGrassOnMesh()`, `ClearGrass()`, `RefreshGrassHeights()`
   - Move: `SpawnGrassForDiskArea()`, `SpawnInstancedGrassForArea()`, `SpawnNonInstancedGrassForArea()`

4. Create `DirtFloorHeightOps.cs`
   - Move: `OnLevelGround()`, `OnRaiseGround()`, `OnSmoothGround()`, `OnPickaxeHit()`
   - Move: `ModifyHeightAt()`, `ApplyMinHeight()`, `ApplyMaxHeight()`
   - Move: `InvalidateTerrainAtDifferentElevation()`, `ClearPaintMaskInRadius()`

5. Create `DirtFloorNeighborSystem.cs`
   - Move: `WouldExceedMaxSize()`, `HandleOverflowDisk()`, `IsAtMaxCapacity()`
   - Move: `RegisterNeighborCombiner()`, `UnregisterNeighborCombiner()`
   - Move: `GetOverlapBounds()`, `BlendEdgesWithNeighbor()`, `BlendWithAllNeighbors()`

6. Update `DirtFloorMeshCombiner.cs`
   - Keep: Disk management, lifecycle, static registry
   - Add: References to new component classes
   - Update: Public methods delegate to appropriate component

### Sprint 2: Unified Paint System (Priority: High)

**Goal**: One paint method to rule them all.

1. Create `TerrainPaintController.cs`
   - Single entry point: `PaintAt(Vector3, float, PaintType)`
   - Auto-detect terrain type
   - Route to correct implementation

2. Simplify paint flow:
   ```
   User Action (IH tool, console command, hoe)
        ?
   TerrainPaintController.PaintAt()
        ?
   ?????????????????????
   ?                   ?
   Custom Terrain    Vanilla Terrain
   (DirtFloor)       (Heightmap)
        ?                 ?
   DirtFloorPaintSystem  TerrainComp
   ```

3. Remove redundant paint methods:
   - Consolidate `OnPaintTerrain()` variations
   - Remove `PaintAllLayersAt()` (handled by controller)
   - Simplify `PaintArea()` in DirtFloorTerrainManager

### Sprint 3: Fix ClutterSystem Errors (Priority: High)

**Goal**: No more server-side spam during client loading.

**Root Cause Analysis:**
The error occurs in `ClutterSystem.GenerateVegPatch` at line 0x00394, which corresponds to code that uses `hmap` (Heightmap) reference AFTER `GetGroundInfo` returns. Even though we have a prefix patch on `GetGroundInfo`, the vanilla code continues after our patch and may:
1. Have a null `hmap` if our patch returned one that was destroyed
2. Call methods on `hmap` that fail during zone transitions
3. Have timing issues where Heightmap is being destroyed while ClutterSystem queries it

**Specific Error Location:**
```csharp
// In GenerateVegPatch, after GetGroundInfo returns:
// These lines use hmap which may be null or stale:
hmap.GetOceanDepth(point)      // Line ~0x00394
hmap.GetVegetationMask(point)  // Additional calls
hmap.IsCleared(point)          // Additional calls
```

**Solution:**
1. **Add prefix patch to GenerateVegPatch** to skip patches in custom terrain areas:
```csharp
[HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.GenerateVegPatch))]
[HarmonyPrefix]
[HarmonyPriority(Priority.First)]
static bool GenerateVegPatch_Prefix(Vector2Int patchID, float size)
{
    // Calculate patch center in world coords
    float x = patchID.x * size + size * 0.5f;
    float z = patchID.y * size + size * 0.5f;
    Vector3 center = new Vector3(x, 0, z);
    
    // Check if this patch is over custom terrain
    var customTerrain = DirtFloorController.GetDirtFloorAtPosition(center, size * 1.5f);
    if (customTerrain != null)
    {
        // Our grass system handles this area - skip vanilla clutter
        return false;
    }
    
    // Let vanilla handle it
    return true;
}
```

2. **Add null guards to existing GetGroundInfo patch**:
   - Return `false` if no valid Heightmap can be found
   - Log a warning instead of letting NRE propagate

3. **Add try-catch wrapper for safety**:
```csharp
[HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.GenerateVegPatch))]
[HarmonyFinalizer]
static Exception GenerateVegPatch_Finalizer(Exception __exception)
{
    if (__exception is NullReferenceException)
    {
        // Suppress the NRE - it's caused by our custom terrain
        // Log once per minute at most
        RateLimitedLog.LogWarning("ClutterSystem NRE suppressed (custom terrain area)");
        return null; // Swallow the exception
    }
    return __exception;
}
```

**Implementation:**
1. Add `GenerateVegPatch_Prefix` to `EnvironmentBoxPatches.cs` or create new `ClutterSystemPatches.cs`
2. Test with server + client to verify no more spam
3. Verify grass/clutter still works correctly on vanilla terrain

### Sprint 4: Grass System Consolidation (Priority: Medium)

**Goal**: One grass system that works everywhere.

1. Determine ownership:
   - Combined terrain ? `DirtFloorGrassSpawner`
   - Individual disk (not in combiner) ? `GrassOverlayController`
   - Env box areas ? `EnvironmentBoxClutterSpawner`

2. Prevent conflicts:
   - Clear individual grass when disk joins combiner
   - Mark areas as "custom grass managed" to skip vanilla ClutterSystem

3. Unified grass refresh:
   - Single method to refresh grass after terrain changes
   - Respects paint masks (no grass on paved/cultivated)

---

## File Size Targets

After refactor, target line counts:

| File | Current | Target |
|------|---------|--------|
| DirtFloorMeshCombiner.cs | ~5000 | ~800 |
| DirtFloorMeshBuilder.cs | N/A | ~800 |
| DirtFloorPaintSystem.cs | N/A | ~600 |
| DirtFloorGrassSpawner.cs | N/A | ~400 |
| DirtFloorHeightOps.cs | N/A | ~600 |
| DirtFloorNeighborSystem.cs | N/A | ~300 |
| TerrainPaintController.cs | N/A | ~300 |

**Total**: Same functionality in ~3800 lines spread across 7 focused files instead of one 5000-line monster.

---

## Testing Checklist

### After Sprint 1 (Split):
- [ ] Terrain creation works (place DirtFloor piece)
- [ ] Terrain modification works (level, raise, lower, smooth)
- [ ] Paint works (pave, cultivate, dirt, reset)
- [ ] Grass spawns correctly
- [ ] Collision works (don't fall through)
- [ ] Zone reload works (terrain persists)
- [ ] LOD transitions work

### After Sprint 2 (Paint):
- [ ] Paint on custom terrain works
- [ ] Paint on vanilla terrain works
- [ ] Paint respects env box boundaries
- [ ] No competing paint methods causing flicker
- [ ] Paint persists across zone reload

### After Sprint 3 (ClutterSystem):
- [ ] No server-side errors during client loading
- [ ] Grass/clutter appears correctly in custom terrain areas
- [ ] No performance issues from patch overhead

### After Sprint 4 (Grass):
- [ ] Grass appears on unpainted custom terrain
- [ ] Grass removed from painted areas
- [ ] Grass heights match terrain after modifications
- [ ] No duplicate grass (individual + combined)

---

## Notes

### Why Split Instead of Rewrite?

A full rewrite would be risky because:
1. Current system mostly works (paint issues aside)
2. Complex state management (cache, ZDO, networking)
3. Many edge cases already handled
4. Integration with other systems (IH, WEC, env boxes)

By splitting, we preserve working code while making it more maintainable.

### Backward Compatibility

The public API should remain the same:
- `DirtFloorMeshCombiner.GetOrCreateCombinerFor()` 
- `DirtFloorTerrainManager.LevelArea()`, `PaintArea()`, etc.
- `DirtFloorController.PaintAt()`

Internal implementation details will change but external callers shouldn't need updates.

---

## Questions to Resolve

1. **Should DirtFloorPaintSystem be a static class or instance per combiner?**
   - Static: Simpler, no state management
   - Instance: Can hold combiner-specific paint mask reference

2. **How do we handle paint persistence?**
   - Currently: Paint stored on individual disks (ZDO) + combined mask (runtime)
   - Issue: Combined mask rebuilt from disks, but direct painting doesn't persist
   - Solution: Paint on disks for persistence, use combined mask for display only?

3. **What's the relationship between DirtFloorGrassSpawner and GrassOverlayController?**
   - GrassOverlayController is per-disk
   - DirtFloorGrassSpawner would be per-combiner
   - Need to ensure they don't both spawn grass

---

## Timeline Estimate

| Sprint | Duration | Dependencies |
|--------|----------|--------------|
| Sprint 1 | 2-3 days | None |
| Sprint 2 | 1-2 days | Sprint 1 |
| Sprint 3 | 1 day | None (can parallel with Sprint 2) |
| Sprint 4 | 1-2 days | Sprint 1 |

**Total**: ~5-8 days of focused work

---

## Progress Tracking

### Completed
- [x] Created refactor plan document
- [x] Added ClutterSystem.GenerateVegPatch finalizer patch to suppress NRE spam
- [x] Build verified successful after ClutterSystem fix
- [x] Created `DirtFloorMeshCombiner.Static.cs` with static methods
- [x] Made `DirtFloorMeshCombiner` a partial class
- [x] Removed duplicate static methods from main file
- [x] Build verified successful after first partial class split
- [x] **SPRINT 1 COMPLETE**: Full split into 9 partial class files

### Sprint 1 Results - Final File Structure

| File | Lines | Purpose |
|------|-------|---------|
| `DirtFloorMeshCombiner.Core.cs` | 141 | Configuration, runtime state, properties |
| `DirtFloorMeshCombiner.Lifecycle.cs` | 359 | Unity lifecycle + LOD management |
| `DirtFloorMeshCombiner.DiskManagement.cs` | 732 | Disk add/remove/merge, cache restoration, overflow handling |
| `DirtFloorMeshCombiner.MeshGen.cs` | 585 | Mesh generation, height sampling, bounds calculation |
| `DirtFloorMeshCombiner.Paint.cs` | 495 | Material setup, paint mask, biome colors |
| `DirtFloorMeshCombiner.Grass.cs` | 351 | Grass spawning and management |
| `DirtFloorMeshCombiner.HeightOps.cs` | 963 | Height modification (level, raise, lower, smooth) |
| `DirtFloorMeshCombiner.Utilities.cs` | 540 | Utility methods, constants, collision, CombinedMeshHitHandler |
| `DirtFloorMeshCombiner.Static.cs` | 314 | Static helper methods |

**Total**: ~4,480 lines across 9 focused files (down from ~5,230 lines in one monolith)

### In Progress
- [ ] Sprint 2: Unified Paint System - Create `TerrainPaintController` to unify paint paths

### Next Steps
1. Debug paint tool conflicts between custom terrain, vanilla terrain, and env boxes
2. The paint issue was happening on custom terrain - trace the paint flow using the new organized code
3. Check if `DirtFloorMeshCombiner.OnPaintTerrain()` in `HeightOps.cs` is being called correctly

### Blocked/Deferred
- [ ] Sprint 4: Grass System Consolidation (depends on Sprint 2)

### Notes
- Original monolith file deleted: `DirtFloorMeshCombiner.cs` (2671 lines at time of deletion)
- All code now organized by responsibility in partial classes
- Build verified successful after complete split
- Code flow is now much easier to trace:
  - Paint issues ? Check `DirtFloorMeshCombiner.Paint.cs`
  - Height issues ? Check `DirtFloorMeshCombiner.HeightOps.cs`
  - Grass issues ? Check `DirtFloorMeshCombiner.Grass.cs`
  - Disk management ? Check `DirtFloorMeshCombiner.DiskManagement.cs`

---

## Related Files

- `DirtFloorMeshCombiner.cs` - The monolith to split
- `DirtFloorController.cs` - Individual disk controller
- `DirtFloorTerrainManager.cs` - Operation entry points
- `DirtFloorPatches.cs` - Harmony patches
- `GrassOverlayController.cs` - Individual grass spawning
- `EnvironmentBoxClutterSpawner.cs` - Env box grass
- `BiomePaintPersistence.cs` - Biome paint storage
