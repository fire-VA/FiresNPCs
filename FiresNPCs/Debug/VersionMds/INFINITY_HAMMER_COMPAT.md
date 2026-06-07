# Infinity Hammer / World Edit Commands Compatibility

## Overview

This document tracks our efforts to make custom dirt floor terrain compatible with Infinity Hammer and World Edit Commands terrain tools.

## The Problem

Our custom terrain (`DirtFloorController`, `DirtFloorMeshCombiner`) is a **standalone terrain system** that:
- Does NOT use Valheim's `Heightmap` or `TerrainComp` systems
- Uses its own mesh generation and paint mask system
- Merges multiple disk pieces into combined terrain

Infinity Hammer and World Edit Commands expect to work with vanilla terrain:
- They find terrain via `Heightmap.FindHeightmap(position)`
- They get `TerrainComp` via `heightmap.GetAndCreateTerrainCompiler()`
- They modify `TerrainComp.m_levelDelta`, `m_smoothDelta`, `m_paintMask` arrays
- They call `Terrain.Save()` which triggers `heightmap.Poke()`

---

## GAME PLAN (Current Sprint)

### Phase 1: Hover Text Enhancement ? DONE
**Goal:** Show current grass type when hovering with cultivator/hoe

### Phase 2: Gap Fill on Level Tool ? DONE  
**Goal:** When leveling over gaps, automatically fill with new disks

### Phase 3: Full World Edit Commands Integration ? DONE
**Goal:** Support ALL WEC terrain operations with proper parameter parsing

**Architecture:**
```
WEC Command "terrain circle=5 level smooth=0.5"
        ?
InfinityHammerCompat.TryHandleTerrainCommand()
        ?
TerrainCommandParser.Parse() ? TerrainOperationParams
        ?
TerrainOperationExecutor.Execute()
        ?
DirtFloorTerrainManager.[Operation]Area()
        ?
DirtFloorMeshCombiner / DirtFloorController
```

**New Classes:**
1. `TerrainOperationParams` - Holds parsed operation parameters  
2. `TerrainCommandParser` - Parses WEC-style command parameters
3. `TerrainOperationExecutor` - Executes operations on our terrain

**Operations Supported:**
| WEC Operation | Our Implementation | Status |
|--------------|-------------------|--------|
| `level` | `LevelArea()` | ? |
| `raise` | `RaiseArea()` | ? |
| `lower` | `LowerArea()` | ? |
| `smooth` | `SmoothArea()` | ? |
| `slope` | `SlopeArea()` | ? |
| `paint` | `PaintArea()` | ? |
| `reset` | `ResetArea()` | ? |
| `delta` | `SetDeltaArea()` | ? |
| `min` | `ApplyMinHeight()` | ? |
| `max` | `ApplyMaxHeight()` | ? |
| `void` | N/A (not applicable) | ? |

**Parameter Support:**
| WEC Parameter | Description | Status |
|--------------|-------------|--------|
| `circle=X` | Circular radius | ? |
| `rect=W,D` | Rectangle width/depth | ? |
| `from=X,Z,Y` | Start position | ? |
| `to=X,Z,Y` | End position (auto-calculates) | ? |
| `angle=X` | Rotation angle | ? |
| `smooth=X` | Edge smoothing (0-1) | ? |
| `offset=F,R,U` | Position offset | ? |
| `step=F,R,U` | Step-based offset | ? |
| `paint=type` | Paint type name | ? |
| `blockcheck` | Check for structures | ? |
| `within=min-max` | Altitude filter | ? |
| `chance=X` | Random chance | ? |

---

## Architecture

### Current Flow (Problematic)
```
User Action ? TerrainOp ? Heightmap ? TerrainComp ? Vanilla Terrain
                ?
         (Our terrain ignored)
```

### Target Flow
```
User Action ? TerrainOp ? InfinityHammerCompat.Intercept()
                              ?
                    DirtFloorTerrainManager
                              ?
              ?????????????????????????????????
              ?               ?               ?
         CreateDisk      ModifyExisting   StitchEdges
              ?               ?               ?
         DirtFloorController  DirtFloorMeshCombiner
```

### Key Classes

**`DirtFloorTerrainManager`** (NEW - Centralized operations)
```csharp
public static class DirtFloorTerrainManager
{
    // Large area operations (creates/removes disks)
    public static void LevelArea(Vector3 center, float radius, float height);
    public static void CreateFlatDisk(Vector3 center, float radius, float height);
    public static void RemoveDisksInRadius(Vector3 center, float radius);
    
    // Modification operations (modifies existing disks)
    public static void RaiseArea(Vector3 center, float radius, float amount);
    public static void LowerArea(Vector3 center, float radius, float amount);
    public static void SmoothArea(Vector3 center, float radius);
    public static void SlopeArea(Vector3 center, float radius, Vector3 direction, float angle);
    public static void PaintArea(Vector3 center, float radius, PaintType type);
    
    // Stitching
    public static void StitchDiskToNeighbors(DirtFloorController disk);
    public static List<DirtFloorController> GetDisksInRadius(Vector3 center, float radius);
    public static List<DirtFloorController> GetDisksOverlappingRadius(Vector3 center, float radius);
}
```

**`InfinityHammerCompat`** (Enhanced interception)
- Intercept `TerrainOp.Awake()` with full settings parsing
- Route to `DirtFloorTerrainManager` based on operation type
- Handle IH-specific features (radius from tool, selection area)

---

## Implementation Order

1. ? **Create `DirtFloorTerrainManager`** - Central hub for all terrain ops
2. ? **Enhance hover text** - Shows terrain info when holding cultivator/hoe
3. ? **Implement `LevelArea`** with disk creation/removal
4. ? **Implement other area operations** (raise, lower, smooth)
5. ? **Add slope support**
6. ? **Test with Infinity Hammer tools**
7. ? **Test console commands**

## File Structure

```
FiresNPCs/Utilities/
??? DirtFloorController.cs       # Main controller, paint mask owner
??? DirtFloorMeshCombiner.cs     # Merges multiple disks into one mesh
??? DirtFloorHeightmap.cs        # Per-vertex height manipulation
??? CustomHeightmap.cs           # Alternative heightmap implementation
??? DirtFloorPatches.cs          # Plant placement, cultivator patches
??? DirtFloorTerrainManager.cs   # NEW: Centralized terrain operations
??? InfinityHammerCompat.cs      # Infinity Hammer/WEC compatibility
??? DirtFloorSaveManager.cs      # Batched ZDO saving
```

---

## Resolved Issues

### Paint Texture Overlap (FIXED)
When painting (pave/cultivate/dirt) on merged terrain, textures were overlapping at disk boundaries.

**Solution:** Paint on ALL overlapping disks + weighted blending in combined mask.

### Gap Fill on Level Tool (FIXED)
When using Infinity Hammer's level tool over gaps in combined mesh, vanilla terrain could show through.

**Root Cause:** The level operation only modified existing disks, it didn't fill areas with no disk coverage.

**Solution Implemented:**
1. **Gap Detection** - `HasGapsInCoverage()` samples points in a grid pattern to detect areas not covered by any disk
2. **Automatic Fill** - If coverage is less than 90%, automatically creates a new disk to fill the entire radius
3. **Smart Stitching** - New disk is added to the same combiner as edge disks for seamless integration
4. **Proper Cleanup** - Fully enclosed disks are removed to prevent overlap

**Code changes in `DirtFloorTerrainManager.cs`:**
- `LevelArea()` - Now checks for gaps before deciding whether to modify or create
- `HasGapsInCoverage()` - Samples grid of points to detect uncovered areas
- `IsPointCoveredByDisk()` - Checks if a specific point has disk coverage
- `CreateFlatDiskWithCleanup()` - Enhanced to properly stitch new disk into existing combiner

---

## Paint Mask & Grass Scaling Fix (FIXED)

### Problem Description

When using Infinity Hammer's level tool with a **larger radius**, the resulting terrain showed:
1. **Grid-like paint pattern artifacts** - Paint appeared in a repeating grid pattern instead of smooth blending
2. **Grass spawning on painted areas** - Grass would appear where it shouldn't (on cultivated/paved terrain)
3. **Paint/collision mismatch at disk boundaries** - Visual paint appeared where collision mesh didn't exist

### Visual Symptom
The terrain displayed a distinct repeating pattern that looked like individual disk paint masks tiled across the surface, rather than properly blended into the combined mesh's UV space.

### Root Cause Analysis

**Issue 1: Incorrect UV Transformation in Paint Sampling**

The original `UpdateCombinedPaintMask()` code called `disk.GetPaintTypeAt(worldPos)` which:
1. Converted world position to the disk's local space
2. Used incorrect UV calculation that didn't properly account for disk radius scaling
3. When disks were expanded via Infinity Hammer, the UV mapping didn't scale correctly
4. This caused paint to repeat/tile in a grid pattern

**Issue 2: Grass Using Individual Disk Paint Check**

The grass spawning code used `bestDisk.GetPaintTypeAt(worldPos)` which:
1. Only checked ONE disk's paint mask
2. Missed paint from OTHER overlapping disks
3. Result: Grass could spawn where another disk was painted

**Issue 3: No Paint for Valid Collision Areas**

When `totalWeight == 0` (no disk contributed paint):
1. Pixels were left black (invalid)
2. Caused visual artifacts at disk boundaries

### The Fix

#### New Method: `SampleDiskPaintColor()`
**File:** `DirtFloorMeshCombiner.cs`

```csharp
/// <summary>
/// Samples paint color directly from a disk's paint mask texture.
/// Uses bilinear interpolation for smooth sampling between pixels.
/// </summary>
private Color SampleDiskPaintColor(DirtFloorController disk, Vector3 worldPos)
{
    // Get paint mask texture
    Texture2D paintMask = GetDiskPaintMask(disk);
    if (paintMask == null) return Heightmap.m_paintMaskNothing;
    
    // Convert world position to disk's local space
    Vector3 localPos = disk.transform.InverseTransformPoint(worldPos);
    
    // Convert local position to UV coordinates
    // DirtFloorController uses: uvX = (localPos.x / (DirtRadius * 2f)) + 0.5f
    float diameter = disk.DirtRadius * 2f;
    float uvX = (localPos.x / diameter) + 0.5f;
    float uvZ = (localPos.z / diameter) + 0.5f;
    
    // Use bilinear sampling for smooth interpolation
    // Important: paint mask is 32x32 but combined is 128x128
    return paintMask.GetPixelBilinear(uvX, uvZ);
}
```

**Key insight:** Uses `GetPixelBilinear()` instead of `GetPixel()` for smooth interpolation when sampling the low-resolution (32x32) disk paint mask into the higher-resolution (128x128) combined paint mask.

#### New Method: `GetCombinedPaintTypeAt()`
**File:** `DirtFloorMeshCombiner.cs`

```csharp
/// <summary>
/// Gets the paint type at a world position from the combined paint mask.
/// This properly handles paint from ALL overlapping disks, not just one.
/// Used for grass spawning to ensure paint from any disk is respected.
/// </summary>
private DirtFloorController.PaintType GetCombinedPaintTypeAt(Vector3 worldPos)
{
    // Sample from combined paint mask using UV coordinates
    float u = (worldPos.x - _combinedBounds.min.x) / _combinedBounds.size.x;
    float v = (worldPos.z - _combinedBounds.min.z) / _combinedBounds.size.z;
    
    Color pixel = _combinedPaintMask.GetPixel(px, py);
    
    // Determine paint type from color (same logic as SnapToNearestPaintColor)
    // Returns PaintType enum instead of Color
}
```

#### New Public Method: `GetPaintMask()`
**File:** `DirtFloorController.cs`

```csharp
/// <summary>
/// Gets the paint mask texture for external sampling (e.g., by DirtFloorMeshCombiner).
/// </summary>
public Texture2D GetPaintMask()
{
    return _paintMask;
}
```

**Purpose:** Exposes the private `_paintMask` field cleanly without reflection.

#### Modified: `UpdateCombinedPaintMask()`

Changed paint sampling from:
```csharp
// OLD - Incorrect UV scaling
var paintType = disk.GetPaintTypeAt(worldPos);
Color diskColor = GetPaintColor(paintType);
```

To:
```csharp
// NEW - Proper UV transformation
Color diskColor = SampleDiskPaintColor(disk, worldPos);
```

Also added default paint for valid collision areas:
```csharp
if (totalWeight > 0.001f)
{
    pixels[...] = SnapToNearestPaintColor(blendedColor);
}
else
{
    // FIX: Default to grass instead of leaving black
    pixels[...] = Heightmap.m_paintMaskNothing;
}
```

#### Modified: `SpawnGrassOnMesh()`

Changed paint check from:
```csharp
// OLD - Only checks one disk
var paintType = bestDisk.GetPaintTypeAt(worldPos);
```

To:
```csharp
// NEW - Checks combined paint mask (all disks)
var paintType = GetCombinedPaintTypeAt(worldPos);
```

### Files Modified

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.cs` | Added `SampleDiskPaintColor()`, `GetDiskPaintMask()`, `GetCombinedPaintTypeAt()`; Modified `UpdateCombinedPaintMask()` to use new sampling; Modified `SpawnGrassOnMesh()` to use combined paint mask |
| `DirtFloorController.cs` | Added public `GetPaintMask()` method |

### Expected Results

After this fix:

1. **Paint scales correctly** - When using Infinity Hammer to create terrain with large radii, paint masks properly scale with the disk size
2. **No grid pattern** - Paint blends smoothly across disk boundaries without repeating/tiling artifacts (uses bilinear interpolation)
3. **Grass removed from painted areas** - Grass is properly removed when terrain is painted (paved/cultivated/dirt)
4. **Default paint for collision areas** - Areas with valid collision but no explicit paint default to grass (green) instead of black

### Key Design Decisions

**Grass Removed from Painted Areas:**
- Grass is removed when terrain is painted (paved/cultivated/dirt)
- `SpawnGrassOnMesh()` checks `GetCombinedPaintTypeAt()` before spawning each grass instance
- Only spawns grass where `PaintType.Reset` (default grass state) is set
- `OnPaintTerrain()` calls `SpawnGrassOnMesh()` to refresh grass after painting

**Bilinear Interpolation for Paint Sampling:**
- Previous behavior: Nearest-pixel sampling caused blocky artifacts when disk paint mask (32x32) was sampled into combined paint mask (128x128)
- New behavior: Uses `GetPixelBilinear()` for smooth interpolation
- Result: Smooth paint transitions even at large scales

### Testing Checklist

- [ ] Place terrain with default (small) radius - paint should work normally
- [ ] Use Infinity Hammer level tool with radius=10+ - no grid pattern should appear  
- [ ] Paint terrain (pave/cultivate) on overlapping disk area - paint should be solid, no gaps
- [ ] Verify grass is REMOVED from painted areas (paved/cultivated/dirt)
- [ ] Verify grass spawns on unpainted (grass/reset) terrain areas
- [ ] Verify collision exists everywhere grass is visible

---

## Large Scale Terrain Fix (20m+ Radius)

### Problem Description

When creating terrain with large radii (20m+) using Infinity Hammer's level tool:
1. **Paint displayed incorrect grid pattern** - at large scale, UV mapping broke down
2. **Grass didn't fully populate** - only sparse grass at large scales
3. **Mesh quality degraded** - fixed grid resolution caused very coarse terrain

### Root Cause Analysis

**Issue 1: Fixed CustomHeightmap Grid Resolution**
- `CustomHeightmap.m_width = 16` was fixed regardless of disk radius
- A 30m radius disk still used 17x17 vertices = very coarse mesh
- Caused poor collision and visual quality at scale

**Issue 2: Fixed DirtFloorMeshCombiner Grid Resolution**
- `GridResolution = 4` (4 verts/meter) with `MAX_GRID_SIZE = 128`
- Max coverage: 128 / 4 = 32m
- Large disks exceeded this, causing clipping/artifacts

**Issue 3: Fixed Paint Mask Resolution**
- Combined paint mask was only 128x128 pixels
- At 60m bounds, each pixel covers ~0.47m - too coarse
- Individual disk paint masks were 32x32 - also too coarse for large radii

**Issue 4: Fixed Grass Spacing**
- `spacing = 0.35f` regardless of area size
- Large areas spawned excessive grass (performance) or couldn't iterate all points

### The Fix

#### 1. Dynamic CustomHeightmap Grid Resolution
**File:** `CustomHeightmap.cs`

```csharp
// Target vertex density - vertices per meter for consistent detail at any scale
private const float TARGET_VERTS_PER_METER = 2f; // 2 verts/meter = 0.5m spacing
private const int MIN_WIDTH = 8;  // Minimum grid size
private const int MAX_WIDTH = 64; // Maximum grid size to prevent memory issues

public void ReconfigureForRadius(float newRadius)
{
    float diameter = newRadius * 2f;
    
    // CRITICAL: Scale grid resolution based on radius
    int targetWidth = Mathf.RoundToInt(diameter * TARGET_VERTS_PER_METER);
    targetWidth = Mathf.Clamp(targetWidth, MIN_WIDTH, MAX_WIDTH);
    
    if (Mathf.Abs(targetWidth - m_width) > 2)
    {
        m_width = targetWidth;
    }
    
    m_scale = diameter / m_width;
    // ... rest of method
}
```

**Result:** A 30m radius disk now uses 64x64 vertices (capped at MAX_WIDTH), providing consistent 0.94m spacing.

#### 2. Dynamic DirtFloorMeshCombiner Grid Resolution
**File:** `DirtFloorMeshCombiner.cs`

```csharp
private void SetupGrid()
{
    // TARGET: ~2 vertices per meter for consistent detail
    float targetVertsPerMeter = 2f;
    
    // Calculate ideal grid dimensions
    int idealWidth = Mathf.CeilToInt(width * targetVertsPerMeter) + 1;
    int idealHeight = Mathf.CeilToInt(height * targetVertsPerMeter) + 1;
    
    // If ideal dimensions exceed MAX_GRID_SIZE, reduce resolution
    if (idealWidth > MAX_GRID_SIZE || idealHeight > MAX_GRID_SIZE)
    {
        // Calculate the cell size that fits within limits
        float requiredCellSizeX = width / (MAX_GRID_SIZE - 1);
        float requiredCellSizeZ = height / (MAX_GRID_SIZE - 1);
        _cellSize = Mathf.Max(requiredCellSizeX, requiredCellSizeZ);
        
        _gridWidth = Mathf.Min(Mathf.CeilToInt(width / _cellSize) + 1, MAX_GRID_SIZE);
        _gridHeight = Mathf.Min(Mathf.CeilToInt(height / _cellSize) + 1, MAX_GRID_SIZE);
    }
    else
    {
        _cellSize = 1f / targetVertsPerMeter;
        _gridWidth = idealWidth;
        _gridHeight = idealHeight;
    }
}
```

**Result:** Grid automatically adjusts cell size for large areas while staying within 128x128 limit.

#### 3. Increased Combined Paint Mask Resolution
**File:** `DirtFloorMeshCombiner.cs`

```csharp
// OLD: private const int COMBINED_PAINT_MASK_SIZE = 128;
private const int COMBINED_PAINT_MASK_SIZE = 256;
```

**Result:** 256x256 paint mask provides 4x more detail, covering up to 64m bounds with 0.25m/pixel.

#### 4. Dynamic Grass Spacing
**File:** `DirtFloorMeshCombiner.cs`

```csharp
// DYNAMIC SPACING: Scale grass density based on area size
float boundsSize = Mathf.Max(_combinedBounds.size.x, _combinedBounds.size.z);
float spacing;

if (boundsSize < 10f)
{
    spacing = 0.35f; // Dense for small areas
}
else if (boundsSize < 30f)
{
    // Linear interpolation from 0.35 to 0.5
    float t = (boundsSize - 10f) / 20f;
    spacing = Mathf.Lerp(0.35f, 0.5f, t);
}
else
{
    // For very large areas, use wider spacing
    float t = Mathf.Clamp01((boundsSize - 30f) / 30f);
    spacing = Mathf.Lerp(0.5f, 0.8f, t);
}
```

**Result:** Grass density adapts to area size - dense for small areas, sparser for large to maintain performance.

### Files Modified

| File | Changes |
|------|---------|
| `CustomHeightmap.cs` | Added `TARGET_VERTS_PER_METER`, `MIN_WIDTH`, `MAX_WIDTH`; Modified `ReconfigureForRadius()` and `ReconfigureForRadiusWithHeight()` to scale grid |
| `DirtFloorMeshCombiner.cs` | Increased `COMBINED_PAINT_MASK_SIZE` to 256; Modified `SetupGrid()` for dynamic resolution; Modified `SpawnGrassOnMesh()` for dynamic spacing |

### Testing Checklist (Large Scale)

- [ ] Create terrain with radius=5m - should work as before
- [ ] Create terrain with radius=15m - should have good detail
- [ ] Create terrain with radius=30m - should still work, slightly reduced detail
- [ ] Grass should populate fully at all scales
- [ ] Paint should display correctly without grid artifacts
- [ ] Collision should exist everywhere (no fall-through)
- [ ] Performance should remain acceptable at large scales

---

## Level Tool Elevation Change Fix (FIXED)

### Problem Description

When using the level tool to create terrain at a **different elevation** within an existing disk:
1. **Floating paint** - Paint mask would remain at the old elevation
2. **Floating grass** - Grass would spawn at old height positions
3. **Visual/collision mismatch** - New collision at target height, but visuals floating above/below

### Root Cause Analysis

The terrain system stores heights, paint masks, and grass positions in a unified grid based on XZ world coordinates. When leveling terrain to a new elevation:

1. **Heights array** was being updated to new target height ?
2. **Paint mask** was NOT being cleared for the affected area ?
3. **Grass** was spawning based on old height data still in grid ?
4. **IsPositionInsideAnyDisk()** only checked horizontal distance, not elevation ?

The core issue: leveling to a different elevation creates NEW terrain, but doesn't invalidate the OLD terrain at the previous elevation. This leaves paint/grass floating.

### The Fix

#### New Constant: `MAX_LEVEL_VERTICAL_THRESHOLD`
**File:** `DirtFloorMeshCombiner.cs`

```csharp
/// <summary>
/// Vertical threshold for level tool elevation changes.
/// Heights more than this distance from target will be invalidated.
/// This prevents floating paint/grass when leveling to a different elevation.
/// </summary>
private const float MAX_LEVEL_VERTICAL_THRESHOLD = 8f;
```

#### New Method: `InvalidateTerrainAtDifferentElevation()`
**File:** `DirtFloorMeshCombiner.cs`

```csharp
/// <summary>
/// Invalidates terrain (heights, paint, grass) at elevations significantly different
/// from the target height within the specified radius.
/// </summary>
private void InvalidateTerrainAtDifferentElevation(Vector3 center, float radius, float targetHeight, float verticalThreshold)
{
    // For each grid point within horizontal radius...
    // If height is more than verticalThreshold from targetHeight...
    // Invalidate it by setting _heights[idx] = -100f
    
    // Also clears paint mask in the affected area
    ClearPaintMaskInRadius(center, radius);
}
```

**Purpose:** Before creating new terrain at a different elevation, this method:
1. Scans all grid points within the operation radius
2. Invalidates any heights that are significantly different from target
3. Clears the paint mask in the affected area

#### New Method: `ClearPaintMaskInRadius()`
**File:** `DirtFloorMeshCombiner.cs`

```csharp
/// <summary>
/// Clears the paint mask in a circular radius.
/// This is used when invalidating terrain to prevent floating paint.
/// </summary>
private void ClearPaintMaskInRadius(Vector3 center, float radius)
{
    // Sets all paint mask pixels within radius to black (default)
    // Paint will be regenerated based on new terrain after mesh rebuild
}
```

#### New Method: `IsPositionInsideAnyDiskAtHeight()`
**File:** `DirtFloorMeshCombiner.cs`

```csharp
/// <summary>
/// Checks if a world position is inside any connected disk within vertical range.
/// This version also checks height to avoid including disks at very different elevations.
/// </summary>
private bool IsPositionInsideAnyDiskAtHeight(Vector3 worldPos, float heightTolerance)
{
    // Like IsPositionInsideAnyDisk but also validates vertical distance
    // Prevents old disks at different elevations from claiming positions
}
```

#### Modified: `OnLevelGround()`

Added call to invalidate old terrain before creating new:

```csharp
public void OnLevelGround(Vector3 worldPos, float radius, float targetHeight)
{
    // CRITICAL: Before creating new terrain, invalidate any existing terrain at
    // significantly different elevations within this radius.
    InvalidateTerrainAtDifferentElevation(worldPos, radius, targetHeight, MAX_LEVEL_VERTICAL_THRESHOLD);

    // Create a new disk at the exact position, radius, and height
    var newDisk = DirtFloorTerrainManager.CreateDisk(worldPos, radius, targetHeight);
    // ... rest of method
}
```

### Files Modified

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.cs` | Added `MAX_LEVEL_VERTICAL_THRESHOLD` constant; Added `InvalidateTerrainAtDifferentElevation()` method; Added `ClearPaintMaskInRadius()` method; Added `IsPositionInsideAnyDiskAtHeight()` method; Modified `OnLevelGround()` to call invalidation |

### Expected Results

After this fix:

1. **No floating paint** - Paint mask is cleared when leveling to different elevation
2. **No floating grass** - Old heights are invalidated, grass only spawns at new elevation
3. **Collision matches visuals** - Both are regenerated together at new elevation
4. **Multi-layer terrain still works** - 8m threshold allows cave systems (floor/ceiling separated by >8m)

### How It Works (Flow)

```
User: Level tool at Y=10 on terrain at Y=20
           ?
OnLevelGround(pos, radius, targetY=10)
           ?
InvalidateTerrainAtDifferentElevation()
  - Scans grid within radius
  - Heights at Y?20 are >8m from target Y=10
  - Invalidates those heights (-100)
  - Clears paint mask in radius
           ?
CreateDisk() at Y=10
           ?
ForceRegenerate()
  - SampleHeights() - only gets valid heights (at Y?10)
  - BuildMesh() - creates collision at Y?10
  - UpdateCombinedPaintMask() - only paints where heights exist
  - SpawnGrassOnMesh() - only spawns where collision exists
           ?
Result: Clean terrain at Y=10, no floating artifacts
```

### Testing Checklist (Elevation Changes)

- [ ] Level terrain to LOWER elevation within existing disk - no floating paint/grass above
- [ ] Level terrain to HIGHER elevation within existing disk - no sunken paint/grass below  
- [ ] Level terrain adjacent to existing terrain at same height - seamless blend
- [ ] Level terrain to create "step" (partial overlap) - clean transition at boundary
- [ ] Cave ceiling 10m above floor - both remain independent (threshold = 8m)

---

## Testing Checklist

### Hover Text
- [ ] Shows grass type when hovering with cultivator
- [ ] Shows paint type (paved/cultivated/dirt/grass) at position
- [ ] Updates as cursor moves

### Level Tool (Large Radius)
- [ ] Creates new flat disk at IH selection size
- [ ] Removes disks fully inside radius
- [ ] Stitches to partially overlapping disks
- [ ] Resulting terrain is walkable and seamless

### All IH Tools
- [ ] Level - Flattens area
- [ ] Raise - Increases height uniformly
- [ ] Lower - Decreases height uniformly  
- [ ] Smooth - Averages heights for gentle slopes
- [ ] Slope - Creates angled terrain
- [ ] Paint - Applies texture types
- [ ] Reset - Restores to grass

### Console Commands
- [ ] `terrain circle=5 level`
- [ ] `terrain circle=5 raise=1`
- [ ] `terrain circle=5 paint=paved`
- [ ] `terrain circle=10 smooth`
