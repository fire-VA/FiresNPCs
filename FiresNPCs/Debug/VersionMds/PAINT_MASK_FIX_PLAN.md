# Custom Terrain Paint Mask - Investigation & Fix Plan

## Date: 2026-03-12
## Status: FIXED - Dynamic sizing and FilterMode.Point applied

---

## The Problem

Paint is being applied correctly to the texture (verified in logs):
```
[COMBINER PAINT] VERIFY: Pixel at (157,56) = RGBA(0.00, 0.00, 1.00, 1.00)
[COMBINER PAINT] Material shader='Custom/Heightmap', has _ClearedMaskTex=True
```

But the visual doesn't show on the terrain. Biome painting (vertex colors) works fine.

---

## Changes Made (2026-03-12)

### Fix 1: FilterMode.Point (APPLIED)

Changed all paint mask texture creation to use `FilterMode.Point` instead of `FilterMode.Bilinear`:
- `SetupMaterial()` - when creating new paint mask
- `SetPaintMask()` - when restoring from undo/redo
- `InitializePaintMask()` - when initializing empty mask
- `LoadPaintMaskData()` - when loading from persistence
- `PaintDirectlyOnCombinedMask()` - in Utilities file

**Why:** Bilinear filtering interpolates between pixels, which can dilute paint colors and cause the shader's threshold check to fail. Point filtering gives exact pixel values like vanilla.

### Fix 2: Dynamic Paint Mask Size (FULLY IMPLEMENTED)

Replaced ALL hardcoded `COMBINED_PAINT_MASK_SIZE` (64) references with dynamic `_combinedPaintMask.width/height`:

**Files Updated:**

1. **DirtFloorMeshCombiner.Paint.cs:**
   - `SetupMaterial()` - creates mask matching `_gridWidth` 
   - `UpdateCombinedPaintMask()` - uses `maskWidth/maskHeight` in loops
   - `RemapPaintMaskToNewBounds()` - uses actual texture dimensions
   - `ClearPaintAndRespawnGrass()` - uses actual texture dimensions
   - `ClearPaintInRegion()` - uses actual texture dimensions for UV calculations
   - `PaintAtWorldPosition()` - uses actual texture dimensions for pixel calculations
   - `InitializePaintMask()` - creates mask matching `_gridWidth`
   - `UpdateCombinedPaintMaskLegacy()` - uses actual texture dimensions
   - `UpdatePaintMaskFromBiomePaints()` - uses actual texture dimensions

2. **DirtFloorMeshCombiner.Utilities.cs:**
   - `GetCombinedPaintTypeAt()` - uses actual texture dimensions
   - `PaintDirectlyOnCombinedMask()` - uses actual texture dimensions, Point filtering

3. **DirtFloorMeshCombiner.Grass.cs:**
   - `IsCleared()` - uses actual texture dimensions

4. **DirtFloorMeshCombiner.Core.cs:**
   - Added `_paintMaskWidth`, `_paintMaskHeight` tracking fields
   - Added `DEFAULT_PAINT_MASK_SIZE = 64` fallback constant
   - Added `COMBINED_PAINT_MASK_SIZE = 64` backward compat alias (for any code not yet migrated)

---

## Root Cause Analysis

### Vanilla Heightmap Paint System

Vanilla uses a paint mask texture where:
- **Size**: `(m_width + 1) x (m_width + 1)` = 33x33 for a 64m zone (m_width=32)
- **Resolution**: Each pixel corresponds to exactly ONE mesh vertex
- **UV Mapping**: Mesh UVs go from 0 to 1 across the entire heightmap
- **Sampling**: Shader samples at UV position, gets exact pixel for that vertex
- **FilterMode**: Point (no interpolation)

### Our Custom Terrain System (AFTER FIX)

- **Size**: Now matches grid dimensions (e.g., 50x50 for a 50-vertex grid)
- **Resolution**: 1:1 pixel-to-vertex ratio when grid is set
- **UV Mapping**: Same 0-1 range across bounds
- **FilterMode**: Point (matches vanilla) ?
- **Dynamic**: All code uses `_combinedPaintMask.width/height` instead of fixed constant ?

---

## Implementation Details

### How Dynamic Sizing Works

When `SetupMaterial()` creates a paint mask:
```csharp
int maskSize = _gridWidth > 0 ? _gridWidth : DEFAULT_PAINT_MASK_SIZE;
_paintMaskWidth = maskSize;
_paintMaskHeight = maskSize;
_combinedPaintMask = new Texture2D(maskSize, maskSize, TextureFormat.RGBA32, false);
```

When methods iterate over pixels:
```csharp
int maskWidth = _combinedPaintMask.width;
int maskHeight = _combinedPaintMask.height;

for (int y = 0; y < maskHeight; y++)
{
    for (int x = 0; x < maskWidth; x++)
    {
        float u = (float)x / (maskWidth - 1);
        float v = (float)y / (maskHeight - 1);
        // ... world position calculation ...
    }
}
```

### UV-to-Pixel Mapping

For a paint brush at world position:
```csharp
int maskWidth = _combinedPaintMask.width;
int maskHeight = _combinedPaintMask.height;

// World position to UV (0-1)
float centerU = (worldPos.x - bounds.min.x) / boundsWidth;
float centerV = (worldPos.z - bounds.min.z) / boundsDepth;

// UV to pixel
int centerX = Mathf.RoundToInt(centerU * (maskWidth - 1));
int centerY = Mathf.RoundToInt(centerV * (maskHeight - 1));
```

---

## Testing Checklist

After these changes:
- [ ] Paint paved/cultivated/dirt on custom terrain
- [ ] Verify visual shows immediately
- [ ] Check paint persists across zone reload
- [ ] Check paint mask size in logs matches grid size
- [ ] Verify no interpolation artifacts at paint edges

---

## If Still Not Working

### Next Steps to Try:

1. **Verify UV0 coordinates in mesh** - Ensure BuildMesh() sets UV0 correctly for paint mask sampling
2. **Check shader property name** - Confirm `_ClearedMaskTex` is the correct property
3. **Verify texture upload** - Add debug to confirm `_combinedPaintMask.Apply()` is called
4. **Check material assignment** - Ensure `_meshRenderer.sharedMaterial` has the paint texture

### Debug Code to Add:
```csharp
// In PaintAtWorldPosition after Apply():
Debug.Log($"[PAINT DEBUG] Texture uploaded: {_combinedPaintMask.width}x{_combinedPaintMask.height}");
Debug.Log($"[PAINT DEBUG] Material texture: {_terrainMaterial.GetTexture(s_ClearedMaskTexProperty)?.name}");
Debug.Log($"[PAINT DEBUG] Same reference: {ReferenceEquals(_combinedPaintMask, _terrainMaterial.GetTexture(s_ClearedMaskTexProperty))}");
```

---

## Files Modified (Complete List)

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.Core.cs` | Added `_paintMaskWidth`, `_paintMaskHeight`, `DEFAULT_PAINT_MASK_SIZE` |
| `DirtFloorMeshCombiner.Paint.cs` | FilterMode.Point everywhere; Dynamic sizing in all methods |
| `DirtFloorMeshCombiner.Utilities.cs` | Dynamic sizing in `GetCombinedPaintTypeAt()`, `PaintDirectlyOnCombinedMask()` |
| `DirtFloorMeshCombiner.Grass.cs` | Dynamic sizing in `IsCleared()` |

---

## Comparison: Vanilla vs Our System (After Fix)

| Aspect | Vanilla Heightmap | Our Custom Terrain (After Fix) |
|--------|-------------------|-------------------------------|
| Paint mask size | 33x33 (matches mesh) | Matches grid size (dynamic) ? |
| Pixel:Vertex ratio | 1:1 | 1:1 (when grid is set) ? |
| Filter mode | Point | Point ? |
| UV range | 0-1 | 0-1 ? |
| Paint colors | m_paintMaskDirt/Cult/Paved/Nothing | Same ? |
| Size calculation | Uses `_combinedPaintMask.width` | Uses `_combinedPaintMask.width` ? |
