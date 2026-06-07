# Custom Terrain System Refactoring Plan

## Executive Summary

The custom terrain system has grown organically across multiple modules, resulting in:
- **Duplicated functionality** across different files
- **Scattered responsibilities** (clutter in EnvironmentBoxes, paint in multiple places)
- **Unclear ownership** of features
- **Difficult maintenance** and debugging

This document outlines a plan to consolidate everything into a clean, modular architecture.

---

## Current State Analysis

### Problem 1: Scattered Clutter/Grass Handling

| File | Clutter Responsibility |
|------|----------------------|
| `DirtFloorMeshCombiner.Grass.cs` | Spawns grass on combined mesh |
| `ClutterPaintTool.cs` | Paints clutter via console commands |
| `BiomePaintPersistence.cs` | Persists clutter paints, restores on load |
| `GrassOverlayController.cs` | Per-disk grass spawning |
| `EnvironmentBoxPatches.cs` | ClutterSystem patches for biome override |

**Issues:**
- Grass is spawned by both combiner AND per-disk controller
- Clutter clearing happens in multiple places
- Persistence is handled separately from spawning

### Problem 2: Scattered Paint Handling

| File | Paint Responsibility |
|------|---------------------|
| `DirtFloorMeshCombiner.Paint.cs` | Direct painting on combiner masks |
| `DirtFloorTerrainManager.cs` | `PaintArea()` entry point |
| `ZonePaintMaskManager.cs` | Zone-level paint storage (partially used) |
| `BiomePaintPersistence.cs` | Biome paint persistence |
| `TerrainPaintRPC.cs` | Network sync for paint |
| `InfinityHammerCompat.cs` | Paint command parsing |

**Issues:**
- Multiple entry points for painting
- Zone-level vs combiner-level paint confusion
- Biome paint vs terrain paint (paved/cultivated) mixed together

### Problem 3: Scattered Biome Handling

| File | Biome Responsibility |
|------|---------------------|
| `BiomeOverlayPainter.cs` | Paints biome overlays on vanilla heightmaps |
| `BiomePaintPersistence.cs` | Persists biome paints |
| `EnvironmentBoxPatches.cs` | Biome override via environment boxes |
| `DirtFloorMeshCombiner.Paint.cs` | `GetBiomeColorForPosition()` |

**Issues:**
- Biome affects both vertex colors AND can affect paint mask
- Environment boxes vs painted biomes are different systems doing similar things
- No central "what biome is at this position?" query

### Problem 4: Module Location Issues

| File | Current Location | Should Be |
|------|-----------------|-----------|
| `BiomePaintPersistence.cs` | `EnvironmentBoxes/` | `CustomTerrain/` |
| `BiomeOverlayPainter.cs` | `EnvironmentBoxes/` | `CustomTerrain/` |
| `ClutterPaintTool.cs` | `EnvironmentBoxes/` | `CustomTerrain/` |
| `GrassOverlayController.cs` | `CustomTerrain/` | ? Correct |

---

## Proposed Architecture

### New Namespace Structure

```
VerdantsAscent.Terrain/
??? Core/                       # Core terrain mesh management
?   ??? DirtFloorController.cs
?   ??? DirtFloorMeshCombiner.cs (partial files)
?   ??? DirtFloorCombinerCache.cs
?   ??? DirtFloorSaveManager.cs
?   ??? CustomHeightmap.cs
?
??? Paint/                      # All terrain painting
?   ??? TerrainPaintManager.cs     # Central paint API
?   ??? TerrainPaintMask.cs        # Paint mask texture handling
?   ??? TerrainPaintPersistence.cs # Save/load paint data
?   ??? TerrainPaintRPC.cs         # Network sync
?
??? Biome/                      # All biome handling
?   ??? BiomeManager.cs            # Central biome API
?   ??? BiomeOverlayPainter.cs     # Biome overlay painting
?   ??? BiomePersistence.cs        # Save/load biome data
?   ??? BiomeColorProvider.cs      # Vertex color calculation
?
??? Clutter/                    # All clutter/grass handling
?   ??? ClutterManager.cs          # Central clutter API
?   ??? ClutterSpawner.cs          # Spawning logic
?   ??? ClutterPersistence.cs      # Save/load clutter data
?   ??? ClutterPrefabCache.cs      # Prefab caching
?
??? Tools/                      # User-facing tools
?   ??? TerrainPlacementTool.cs
?   ??? InfinityHammerCompat.cs
?   ??? TerrainCommandParser.cs
?   ??? TerrainDebugCommands.cs
?
??? Integration/                # External system integration
?   ??? EnvironmentBoxBiomeProvider.cs  # Env box biome override
?   ??? VanillaTerrainPatches.cs        # Harmony patches
?   ??? ClutterSystemPatches.cs         # ClutterSystem integration
?
??? Utilities/                  # Shared utilities
    ??? TerrainMaterialCache.cs
    ??? TerrainLODManager.cs
    ??? TerrainDeferredInit.cs
```

---

## New Central Manager Classes

### 1. TerrainPaintManager (Single Entry Point for All Paint)

```csharp
namespace VerdantsAscent.Terrain.Paint
{
    /// <summary>
    /// Central manager for ALL terrain painting operations.
    /// This is the ONLY class that should be called for painting.
    /// </summary>
    public static class TerrainPaintManager
    {
        public enum PaintType
        {
            Grass,      // Default unpainted
            Dirt,       // Brown dirt
            Cultivated, // Tilled soil
            Paved       // Stone/cobblestone
        }
        
        /// <summary>
        /// Paints terrain at a world position.
        /// Handles: custom terrain, vanilla terrain, persistence, clutter clearing.
        /// </summary>
        public static void Paint(Vector3 center, float radius, PaintType type)
        {
            // 1. Clear clutter in area (via ClutterManager)
            ClutterManager.ClearInRadius(center, radius);
            
            // 2. Paint custom terrain (DirtFloorMeshCombiner)
            PaintCustomTerrain(center, radius, type);
            
            // 3. Paint vanilla terrain (if applicable)
            PaintVanillaTerrain(center, radius, type);
            
            // 4. Save to persistence
            TerrainPaintPersistence.RecordPaint(center, radius, type);
            
            // 5. Sync over network
            TerrainPaintRPC.SendPaint(center, radius, type);
        }
        
        /// <summary>
        /// Queries the paint type at a position.
        /// </summary>
        public static PaintType GetPaintAt(Vector3 worldPos);
        
        /// <summary>
        /// Returns true if the position is "cleared" (non-grass paint).
        /// Used by clutter spawning to avoid grass on paved areas.
        /// </summary>
        public static bool IsCleared(Vector3 worldPos);
    }
}
```

### 2. BiomeManager (Single Entry Point for All Biome Queries)

```csharp
namespace VerdantsAscent.Terrain.Biome
{
    /// <summary>
    /// Central manager for ALL biome-related queries and modifications.
    /// </summary>
    public static class BiomeManager
    {
        /// <summary>
        /// Gets the effective biome at a world position.
        /// Priority: 1) Painted biome overlay, 2) Environment box, 3) World generator
        /// </summary>
        public static Heightmap.Biome GetBiomeAt(Vector3 worldPos)
        {
            // Check painted biome overlays first
            if (BiomePersistence.TryGetPaintedBiome(worldPos, out var painted))
                return painted;
            
            // Check environment box override
            if (EnvironmentBoxBiomeProvider.TryGetBiome(worldPos, out var envBox))
                return envBox;
            
            // Fall back to world generator
            return WorldGenerator.instance.GetBiome(worldPos);
        }
        
        /// <summary>
        /// Paints a biome overlay at a position.
        /// Updates vertex colors on custom terrain, triggers clutter refresh.
        /// </summary>
        public static void PaintBiome(Vector3 center, float radius, Heightmap.Biome biome)
        {
            // 1. Record the paint
            BiomePersistence.RecordBiomePaint(center, radius, biome);
            
            // 2. Update custom terrain vertex colors
            RefreshCustomTerrainBiomeColors(center, radius);
            
            // 3. Refresh clutter for new biome
            ClutterManager.RefreshInRadius(center, radius);
            
            // 4. Update vanilla heightmaps if needed
            RefreshVanillaHeightmaps(center, radius);
        }
        
        /// <summary>
        /// Gets the biome color for terrain vertex coloring.
        /// </summary>
        public static Color32 GetBiomeColor(Heightmap.Biome biome);
    }
}
```

### 3. ClutterManager (Single Entry Point for All Clutter)

```csharp
namespace VerdantsAscent.Terrain.Clutter
{
    /// <summary>
    /// Central manager for ALL clutter/grass operations.
    /// </summary>
    public static class ClutterManager
    {
        /// <summary>
        /// Spawns clutter on custom terrain at a position.
        /// Uses biome from BiomeManager, respects paint from TerrainPaintManager.
        /// </summary>
        public static void SpawnOnTerrain(DirtFloorMeshCombiner combiner);
        
        /// <summary>
        /// Paints specific clutter type at a position.
        /// </summary>
        public static void PaintClutter(Vector3 center, float radius, string clutterType, float density);
        
        /// <summary>
        /// Clears ALL clutter in radius:
        /// - Combiner-spawned grass
        /// - Painted clutter
        /// - Vanilla ClutterSystem
        /// </summary>
        public static void ClearInRadius(Vector3 center, float radius)
        {
            // 1. Clear from all nearby combiners
            foreach (var combiner in DirtFloorMeshCombiner.GetCombinersInRadius(center, radius))
            {
                combiner.ClearGrassInRadius(center, radius);
            }
            
            // 2. Clear painted clutter (persistent)
            ClutterPersistence.RecordClear(center, radius);
            ClutterSpawner.DestroyInRadius(center, radius);
            
            // 3. Clear vanilla ClutterSystem
            ClutterSystem.instance?.ResetGrass(center, radius);
        }
        
        /// <summary>
        /// Refreshes clutter in an area (after biome change, terrain edit, etc.)
        /// </summary>
        public static void RefreshInRadius(Vector3 center, float radius);
        
        /// <summary>
        /// Gets all available clutter type names.
        /// </summary>
        public static IReadOnlyList<string> GetClutterTypes();
    }
}
```

---

## Migration Plan

### Phase 1: Create New Structure (No Breaking Changes)

1. **Create new folders:**
   - `CustomTerrain/Paint/`
   - `CustomTerrain/Biome/`
   - `CustomTerrain/Clutter/`
   - `CustomTerrain/Tools/`
   - `CustomTerrain/Integration/`

2. **Create manager stubs:**
   - `TerrainPaintManager.cs` - delegates to existing code
   - `BiomeManager.cs` - delegates to existing code
   - `ClutterManager.cs` - delegates to existing code

3. **Move files to correct locations:**
   - Move `BiomePaintPersistence.cs` to `CustomTerrain/Biome/`
   - Move `BiomeOverlayPainter.cs` to `CustomTerrain/Biome/`
   - Move `ClutterPaintTool.cs` to `CustomTerrain/Clutter/`

### Phase 2: Consolidate Logic

1. **Consolidate paint logic:**
   - Move all paint code from `DirtFloorTerrainManager` to `TerrainPaintManager`
   - Remove paint methods from `BiomePaintPersistence` (biome only)
   - Update `InfinityHammerCompat` to use `TerrainPaintManager`

2. **Consolidate biome logic:**
   - Create `BiomeManager.GetBiomeAt()` as single query point
   - Move `GetBiomeColorForPosition()` to `BiomeManager`
   - Update `DirtFloorMeshCombiner.BuildMesh()` to use `BiomeManager`

3. **Consolidate clutter logic:**
   - Merge `ClutterPaintTool` spawning into `ClutterSpawner`
   - Move grass spawning from `GrassOverlayController` into `ClutterManager`
   - Remove duplicate clutter code from `DirtFloorMeshCombiner.Grass.cs`

### Phase 3: Update All Callers

1. **Update height modification methods:**
   - Replace direct `ClutterPaintTool.ClearClutter()` calls with `ClutterManager.ClearInRadius()`
   - Replace direct `BiomePaintPersistence` calls with `BiomeManager`

2. **Update Infinity Hammer integration:**
   - All paint commands go through `TerrainPaintManager.Paint()`
   - All biome commands go through `BiomeManager.PaintBiome()`
   - All clutter commands go through `ClutterManager`

3. **Update persistence:**
   - Single TerrainComp data format for all terrain data
   - Clear separation: paint data, biome data, clutter data

### Phase 4: Cleanup

1. **Remove deprecated code:**
   - Remove duplicate methods
   - Remove unused fields
   - Remove legacy compatibility code

2. **Update documentation:**
   - Update XML comments
   - Create architecture diagram
   - Update debug commands

---

## File Mapping: Old ? New

| Old File | New Location | Notes |
|----------|--------------|-------|
| `BiomePaintPersistence.cs` | `Biome/BiomePersistence.cs` | Split biome and clutter |
| `BiomeOverlayPainter.cs` | `Biome/BiomeOverlayPainter.cs` | Keep, update namespace |
| `ClutterPaintTool.cs` | `Clutter/ClutterPaintTool.cs` | Rename to ClutterCommands? |
| `GrassOverlayController.cs` | **DELETE** | Merge into ClutterManager |
| `DirtFloorMeshCombiner.Grass.cs` | `Core/DirtFloorMeshCombiner.Grass.cs` | Simplify, delegate to ClutterManager |
| `DirtFloorMeshCombiner.Paint.cs` | `Core/DirtFloorMeshCombiner.Paint.cs` | Remove biome logic |
| `DirtFloorTerrainManager.cs` | `Core/DirtFloorTerrainManager.cs` | Remove paint logic |
| `ZonePaintMaskManager.cs` | **DELETE** | Not used, replaced by combiner paint |
| `TerrainPaintRPC.cs` | `Paint/TerrainPaintRPC.cs` | Keep, update namespace |
| `EnvironmentBoxPatches.cs` | `Integration/VanillaTerrainPatches.cs` | Rename, keep relevant patches |

---

## Immediate Quick Wins (Before Full Refactor)

These can be done immediately to reduce confusion:

### 1. Create Central Entry Points (Facade Pattern)

Create simple facade classes that route to existing code:

```csharp
// Quick facade - doesn't move files, just provides clean API
public static class TerrainAPI
{
    public static void Paint(Vector3 pos, float radius, PaintType type)
        => DirtFloorMeshCombiner.PaintAllLayersAt(pos, radius, type);
    
    public static void PaintBiome(Vector3 pos, float radius, Heightmap.Biome biome)
        => BiomePaintPersistence.PaintBiomeOverlay(pos, radius, biome);
    
    public static void ClearClutter(Vector3 pos, float radius)
    {
        ClutterPaintTool.ClearClutter(pos, radius);
        foreach (var c in DirtFloorMeshCombiner.GetCombinersInRadius(pos, radius))
            c.ClearGrassInRadius(pos, radius);
    }
    
    public static Heightmap.Biome GetBiome(Vector3 pos)
    {
        var paint = BiomePaintPersistence.GetPaintAtPosition(pos.x, pos.z);
        if (paint != null) return paint.Biome;
        return WorldGenerator.instance.GetBiome(pos);
    }
}
```

### 2. Fix Double Clutter Clearing

The `ClearClutterInTerrainArea()` method calls both:
- `ClearGrassInRadius()` (combiner grass)
- `ClutterPaintTool.ClearClutter()` (painted clutter)

This is correct but could be simplified with `ClutterManager.ClearInRadius()`.

### 3. Remove Unused Code

- `ZonePaintMaskManager.cs` - appears unused
- `UpdatePaintMaskFromBiomePaints()` - already removed call site

---

## Success Metrics

After refactoring:

1. **Single entry point** for each concern:
   - Paint: `TerrainPaintManager.Paint()`
   - Biome: `BiomeManager.GetBiomeAt()`, `BiomeManager.PaintBiome()`
   - Clutter: `ClutterManager.ClearInRadius()`, `ClutterManager.SpawnOnTerrain()`

2. **No duplicate logic:**
   - Only ONE place spawns grass
   - Only ONE place clears clutter
   - Only ONE place queries biome

3. **Clear file organization:**
   - All terrain files in `CustomTerrain/`
   - Sub-folders for Paint, Biome, Clutter, Tools

4. **Testable:**
   - Each manager can be tested independently
   - Clear interfaces between systems

---

## Next Steps

1. [x] Review this plan and approve approach
2. [x] Create facade class (`TerrainAPI.cs`) - **DONE**
3. [x] Create folder structure (Core/, Paint/, Biome/, Clutter/, Tools/, Utilities/) - **DONE**
4. [x] Move files to correct locations - **DONE**
5. [x] Remove deprecated zone-based terrain code - **DONE**
6. [x] Update namespaces for moved files - **DONE**
7. [x] Fix all namespace references in external files - **DONE**
8. [x] Begin consolidation (Phase 2) - **DONE**
9. [x] Update callers to use `TerrainAPI` (Phase 3) - **DONE**
10. [x] Cleanup and delete deprecated code (Phase 4) - **DONE**
11. [x] Fix terrain removal bugs (Phase 5) - **DONE**

### 2025-03-06: Phase 5 - Testing and Bug Fixes

**Issue 1: Terrain mesh stays visible after removal**
- Root cause: When all disks were removed from a combiner, the mesh/collider weren't cleared before destruction
- Fix: Added mesh clearing code in `RemoveDisk()` method before destroying the combiner's GameObject

**Issue 2: Deleted terrain reappears on zone reload**
- Root cause: Cache kept stale data for deleted disks, causing recreation on zone reload
- Fix: Added `OnDiskRemoved()` method to `DirtFloorCombinerCache` that:
  - Removes disk from `_assignedDiskIds`
  - Removes disk from `DiskToGroupMap`
  - Removes disk from its group's `DiskZdoIds` list
  - Removes empty groups from cache entirely
  - Marks cache as dirty for saving

**Issue 3: "Found another terrain compiler" warnings**
- Root cause: Our code was calling `Heightmap.GetAndCreateTerrainCompiler()` which creates new TerrainComps
  that conflict with Valheim's zone system creating its own TerrainComps simultaneously
- Fix: Replaced ALL 6 calls to `GetAndCreateTerrainCompiler()` with safe `GetComponent<TerrainComp>()` pattern:
  - `DirtFloorMeshCombiner.Paint.cs:SavePaintMaskToTerrainComp()` - 1 call fixed
  - `BiomePaintPersistence.cs` - 5 calls fixed in:
    - `PaintBiomeOverlay()`
    - `ResetBiomeOverlay()`
    - `RecordClutterPaint()`
    - `RecordClutterPaintDirect()`
    - `RecordBiomePaintDirect()`
- Now we NEVER create TerrainComps ourselves - we only use existing ones found via `FindTerrainCompiler()` or `GetComponent<TerrainComp>()`

**Issue 4: Z-fighting / checkerboard terrain paint pattern**
- Root cause: Multiple issues combining to create overlapping terrain layers:
  1. `PaintAllLayersAt()` was painting to ALL combiners that overlapped a position (2+ layers)
  2. Multiple combiners were being created at nearly the same position (3m apart, same elevation)
  3. Individual disk meshes (`CustomHeightmap`) not being hidden consistently
- Fixes applied:
  1. **PaintAllLayersAt v11**: Now only paints to ONE combiner per position (closest center)
     - Finds the BEST combiner for each paint operation
     - Uses scoring based on horizontal distance to combiner center
     - Prevents Z-fighting from overlapping paint masks
  2. **RemoveOverlappingCombiners v12**: More aggressive duplicate detection
     - Increased horizontal threshold from 2m to 5m
     - Increased vertical threshold from 3m to 4m
     - Also checks if bounds actually intersect
     - Merges combiners that overlap OR have centers within 3m
  3. **ForceHideAllDiskMeshes**: Already comprehensive - ensures individual disks don't render

**Build: ? SUCCESSFUL**

---

## Progress Log

### 2025-03-06: File Organization Complete

**Folder Structure Created:**
```
CustomTerrain/
??? Core/                       # Core terrain mesh management
?   ??? CustomHeightmap.cs
?   ??? DirtFloorAsyncMeshBuilder.cs
?   ??? DirtFloorCombinerCache.cs
?   ??? DirtFloorController.cs
?   ??? DirtFloorHeightmap.cs
?   ??? DirtFloorMeshCombiner.Core.cs
?   ??? DirtFloorMeshCombiner.DiskManagement.cs
?   ??? DirtFloorMeshCombiner.Grass.cs
?   ??? DirtFloorMeshCombiner.HeightOps.cs
?   ??? DirtFloorMeshCombiner.Lifecycle.cs
?   ??? DirtFloorMeshCombiner.MeshGen.cs
?   ??? DirtFloorMeshCombiner.Static.cs
?   ??? DirtFloorMeshCombiner.Utilities.cs
?   ??? DirtFloorSaveManager.cs
?   ??? DirtFloorTerrainCompStorage.cs
?
??? Paint/                      # All terrain painting
?   ??? DirtFloorMeshCombiner.Paint.cs
?   ??? DirtFloorPaintMaskStreaming.cs
?   ??? TerrainPaintRPC.cs
?   ??? ZonePaintMaskManager.cs
?
??? Biome/                      # All biome handling
?   ??? BiomeOverlayPainter.cs
?   ??? BiomePaintPersistence.cs
?
??? Clutter/                    # All clutter/grass handling
?   ??? ClutterPaintInputHandler.cs
?   ??? ClutterPaintTool.cs
?   ??? GrassOverlaySystem.cs
?
??? Tools/                      # User-facing tools
?   ??? DirtFloorPlacementTool.cs
?   ??? InfinityHammerCompat.cs
?   ??? TerrainCommandParser.cs
?   ??? TerrainDebugCommands.cs
?   ??? TerrainOperationExecutor.cs
?   ??? TerrainOperationParams.cs
?
??? Integration/                # External system integration
?   ??? (empty - patches stay in DirtFloorPatches.cs for now)
?
??? Utilities/                  # Shared utilities
?   ??? DirtFloorDeferredInit.cs
?   ??? DirtFloorLODManager.cs
?   ??? MaterialVariantSystem.cs
?   ??? RateLimitedLogHandler.cs
?   ??? TerrainMaterialCache.cs
?   ??? TerritorySnowManager.cs
?
??? TerrainAPI.cs               # Central facade (root level)
??? DirtFloorTerrainManager.cs  # Main manager (root level)
??? DirtFloorPatches.cs         # Harmony patches (root level)
```

**Deprecated Code Removed:**
- `CustomTerrainZone.cs` - deleted
- `CustomTerrainZoneManager.cs` - deleted
- `CustomTerrainOp.cs` - deleted
- All references to `UseZoneBasedTerrain` flag removed
- All `CustomTerrainZoneManager.*` method calls replaced with `DirtFloorMeshCombiner` equivalents

**Files Fixed:**
- `DirtFloorTerrainManager.cs` - removed zone-based routing
- `DirtFloorPatches.cs` - removed zone-based terrain checks
- `InfinityHammerCompat.cs` - removed zone-based height queries
- `TerrainOperationExecutor.cs` - removed zone-based delete operation

### 2025-03-06: Namespace Refactoring Complete

**All namespaces updated to new structure:**
- `VerdantsAscent.Terrain` - Root-level classes (TerrainAPI, DirtFloorTerrainManager, DirtFloorPatches)
- `VerdantsAscent.Terrain.Core` - Core classes (DirtFloorController, DirtFloorMeshCombiner.*, CustomHeightmap, etc.)
- `VerdantsAscent.Terrain.Paint` - Paint classes (TerrainPaintRPC, ZonePaintMaskManager, DirtFloorPaintMaskStreaming)
- `VerdantsAscent.Terrain.Biome` - Biome classes (BiomeOverlayPainter, BiomePaintPersistence)
- `VerdantsAscent.Terrain.Clutter` - Clutter classes (ClutterPaintTool, GrassOverlayController, ClutterPaintInputHandler)
- `VerdantsAscent.Terrain.Tools` - Tool classes (InfinityHammerCompat, TerrainCommandParser, TerrainDebugCommands, etc.)
- `VerdantsAscent.Terrain.Utilities` - Utility classes (TerrainMaterialCache, DirtFloorDeferredInit, DirtFloorLODManager, etc.)

**External files updated with new using statements:**
- `DebugTools.cs` - Added VerdantsAscent.Terrain.Core, Biome, Clutter, Utilities
- `VAPieceManager.cs` - Added VerdantsAscent.Terrain.Core, Clutter
- `EnvironmentBoxController.cs` - Added VerdantsAscent.Terrain.Utilities
- `EnvironmentBoxPatches.cs` - Added VerdantsAscent.Terrain.Core, Biome
- `TerritoryRuntime.Environment.cs` - Added VerdantsAscent.Terrain.Utilities
- `DebugFXEnumerator.cs` - Added VerdantsAscent.Terrain.Clutter
- `Ascend.cs` - Added VerdantsAscent.Terrain.Tools, Utilities

**Fully-qualified references fixed:**
- `VerdantsAscent.Utilities.DirtFloorController` ? `DirtFloorController`
- `VerdantsAscent.Utilities.CustomHeightmap` ? `CustomHeightmap`
- `VerdantsAscent.Utilities.DirtFloorMeshCombiner` ? `DirtFloorMeshCombiner`
- `VerdantsAscent.Utilities.GrassOverlayController` ? `GrassOverlayController`
- `Utilities.TerritorySnowManager` ? `TerritorySnowManager`
- `Utilities.MaterialVariantSystem` ? `MaterialVariantSystem`

**Build: ? SUCCESSFUL**

### 2025-03-06: Phase 4 - Cleanup (In Progress)

**Removed deprecated files:**

1. **ZonePaintMaskManager.cs** - DELETED
   - Was an alternative zone-based paint storage system
   - Now replaced by combiner-based paint (DirtFloorMeshCombiner.Paint.cs)
   - Updated callers in `DirtFloorPlacementTool.cs` and `TerrainOperationExecutor.cs`
   - Removed `using VerdantsAscent.Terrain.Paint;` imports where no longer needed

**Updated files:**
- `TerrainPaintManager.cs` - Removed reference to ZonePaintMaskManager in comments
- `DirtFloorPlacementTool.cs` - Removed ZonePaintMaskManager.ClearPaintInRadius call
- `TerrainOperationExecutor.cs` - Removed ZonePaintMaskManager.ClearPaintInRadius call

**Build: ? SUCCESSFUL**

### 2025-03-06: Phase 3 - Update Callers (Complete)

**Updated files to use ClutterManager instead of direct ClutterPaintTool calls:**

1. **DirtFloorMeshCombiner.HeightOps.cs**
   - `PunchHole()` - now uses `ClutterManager.ClearInRadius()`
   - `ClearClutterInTerrainArea()` - simplified to single `ClutterManager.ClearInRadius()` call
   - Removed redundant try/catch blocks and multiple clearing calls

2. **DirtFloorTerrainManager.cs**
   - `PaintArea()` - now uses `ClutterManager.ClearInRadius()` instead of 3 separate clearing calls

3. **BiomeOverlayPainter.cs**
   - `PaintOverlay()` - now uses `ClutterManager.ClearInRadius()`

4. **BiomePaintPersistence.cs**
   - `PaintBiomeOverlay()` - now uses `ClutterManager.ClearInRadius()`
   - Note: `ClutterRestoreHelper` still uses direct `ClutterPaintTool.PaintClutter()` to avoid recursion during restoration

**Benefits of using ClutterManager:**
- Single call instead of multiple separate clearing operations
- Consistent behavior across all clutter types (combiner grass, painted clutter, vanilla ClutterSystem)
- Centralized logic for future changes
- Cleaner, more maintainable code

**Build: ? SUCCESSFUL**

### 2025-03-06: Phase 2 - Consolidation Complete

**Created Central Manager Classes:**

1. **ClutterManager.cs** (`VerdantsAscent.Terrain.Clutter`)
   - Single entry point for ALL clutter/grass operations
   - `SpawnOnCombiner()` - Spawns grass on terrain
   - `PaintClutter()` - Paints specific clutter types
   - `ClearInRadius()` - Clears ALL clutter (combiner + painted + vanilla)
   - `RefreshInRadius()` - Refreshes clutter after terrain changes
   - `AllClutterTypes` - List of available clutter types
   - `GetDefaultGrassForBiome()` - Gets biome-appropriate grass

2. **BiomeManager.cs** (`VerdantsAscent.Terrain.Biome`)
   - Single entry point for ALL biome queries
   - `GetBiomeAt()` - Gets effective biome (painted > envbox > world)
   - `PaintBiome()` - Paints biome overlay with persistence
   - `ResetBiome()` - Resets biome to original
   - `GetBiomeColor()` - Gets vertex color for biome
   - `GetGrassColor()` - Gets grass tint color
   - `BiomeHasSnow()`, `BiomeHasGrass()`, `BiomeIsCold()`, `BiomeIsHot()` - Property queries

3. **TerrainPaintManager.cs** (`VerdantsAscent.Terrain.Paint`)
   - Single entry point for ALL terrain painting
   - `Paint()` - Paints terrain (clears clutter, paints custom+vanilla)
   - `GetPaintAt()` - Queries paint type at position
   - `IsCleared()`, `IsCultivated()`, `IsPaved()` - Paint state queries
   - `GetPaintColor()` - Gets paint mask color
   - `ParsePaintType()` - Parses paint from string

**Updated TerrainAPI.cs:**
- Now delegates to the new manager classes
- Added new methods: `IsCultivated()`, `GetPaintAt()`, `HasPaintedBiome()`, `GetClutterTypes()`, `RefreshAllBiomeColors()`
- Cleaner implementation with proper separation of concerns

**Build: ? SUCCESSFUL**

### 2025-03-06: Created TerrainAPI Facade

Created `FiresNPCs\Modules\CustomTerrain\TerrainAPI.cs` as the central entry point for all terrain operations.

**New unified API:**
```csharp
// Paint operations
TerrainAPI.Paint(center, radius, TerrainAPI.PaintType.Paved);
TerrainAPI.IsCleared(worldPos);

// Biome operations
TerrainAPI.GetBiomeAt(worldPos);
TerrainAPI.PaintBiome(center, radius, Heightmap.Biome.Plains);
TerrainAPI.GetBiomeColor(biome);

// Clutter operations
TerrainAPI.ClearClutter(center, radius);
TerrainAPI.PaintClutter(center, radius, "instanced_meadows_grass", 0.5f);
TerrainAPI.RefreshClutter(center, radius);

// Height operations
TerrainAPI.GetHeightAt(worldPos);
TerrainAPI.HasCustomTerrainAt(worldPos);
TerrainAPI.LevelTerrain(center, radius, targetHeight);
TerrainAPI.RaiseTerrain(center, radius, amount);
TerrainAPI.LowerTerrain(center, radius, amount);
TerrainAPI.RemoveTerrain(center, radius);

// Maintenance
TerrainAPI.PerformGlobalCleanup();
TerrainAPI.ForceRegenerateAll();
```

**Benefits:**
- Single entry point for all terrain operations
- Consistent behavior (always clears clutter before painting)
- Easy to find and use
- Can be gradually adopted by existing code

---

## Timeline Estimate

- **Phase 1 (Structure):** 1-2 hours
- **Phase 2 (Consolidate):** 4-6 hours
- **Phase 3 (Update Callers):** 2-3 hours
- **Phase 4 (Cleanup):** 1-2 hours

**Total: 8-13 hours of focused work**

The quick wins (facade pattern) can be done in ~1 hour and will immediately improve the situation without risk.
