# Terrain Paint Overlap Fix

## Problem Summary

When painting biome overlays or terrain paint on custom terrain (DirtFloor disks), the paint doesn't properly cover overlapping areas:

1. **Biome overlays** (plains, lava, snow) appear as independent circles
2. **Terrain paint** (paved, cultivated, dirt) doesn't blend across disk boundaries
3. When new disks are added that overlap existing painted disks, the paint doesn't recalculate

## Root Causes

### Issue 1: Paint Mask UV Remapping
When a new disk is added to a combiner:
- `_combinedBounds` expands to include the new disk
- The paint mask texture stays the same size (256x256)
- **BUT** the UV mapping changes because bounds changed
- Existing paint data is now at wrong UV coordinates

### Issue 2: Biome Paint Circle Independence
`BiomePaintPersistence.PaintBiomeOverlay()` stored each paint as a separate circle and only removed paints **fully contained** within new paints, not **overlapping** paints.

### Issue 3: No Paint Recalculation on Disk Add
`DirtFloorMeshCombiner.AddDisk()` didn't remap existing paint to new bounds.

## Fixes Applied (2025-03-03)

### Fix 1: Paint Mask Remapping on Bounds Change ?
**File:** `DirtFloorMeshCombiner.Paint.cs`

Added `RemapPaintMaskToNewBounds()` method that:
- Copies existing paint pixels to correct UV coordinates when bounds expand
- Called automatically from `AddDisk()` when bounds change

```csharp
public void RemapPaintMaskToNewBounds(Bounds oldBounds, Bounds newBounds)
{
    // For each pixel in new texture, find world position, 
    // then sample from old texture at that world position
    // ...
}
```

### Fix 2: Remove ALL Overlapping Biome Paints ?
**File:** `BiomePaintPersistence.cs`

Changed paint removal logic from "fully contained" to "overlapping at all":

```csharp
// BEFORE: Only removed if fully contained
existingPaints.RemoveAll(p => dist + p.Radius <= radius);

// AFTER: Remove if circles overlap at all
existingPaints.RemoveAll(p => dist <= radius + p.Radius);
```

### Fix 3: Auto-Remap Paint on Disk Add ?
**File:** `DirtFloorMeshCombiner.DiskManagement.cs`

Modified `AddDisk()` to:
1. Store old bounds before adding disk
2. After mesh regeneration, call `RemapPaintMaskToNewBounds()`

### Fix 4: Refresh Both Biome Colors AND Grass ?
**File:** `DirtFloorMeshCombiner.Static.cs`

Added `RefreshBiomeAndClutterInRadius()` which:
- Updates vertex colors for biome
- Clears old grass
- Respawns grass appropriate for new biome

**File:** `BiomePaintPersistence.cs`

Updated to call `RefreshBiomeAndClutterInRadius()` instead of just `RefreshBiomeColorsInRadius()`

## Implementation Files Changed

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.Paint.cs` | Added `RemapPaintMaskToNewBounds()`, `HasPaintData()`, `ClearPaintAndRespawnGrass()` |
| `DirtFloorMeshCombiner.DiskManagement.cs` | Added paint remap call in `AddDisk()` |
| `DirtFloorMeshCombiner.Static.cs` | Added `RefreshBiomeAndClutterInRadius()`, `GetCombinersInRadius()` |
| `BiomePaintPersistence.cs` | Fixed overlapping paint removal, updated to use new refresh method |

## Test Procedure

1. Place several overlapping terrain disks
2. Paint biome overlay (e.g., plains) across all of them
3. Verify paint covers entire combined area, not individual circles
4. Add another disk that overlaps the painted area
5. Verify paint remaps correctly (existing paint visible at correct positions)
6. Check grass/clutter matches the biome
7. Save, exit, reload - verify persistence

## Related Issues

- Grass not spawning after biome paint
- Paint appearing at wrong positions after adding disks
- Clutter from original biome persisting after paint
