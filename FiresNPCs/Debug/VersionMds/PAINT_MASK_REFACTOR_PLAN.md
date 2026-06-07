# Paint Mask System Refactor Plan
**Date:** 03/12/2026 13:45
**Issue:** Paint appearing as thin strips at mesh edges instead of filling painted area

## Problem Statement

When painting paved/dirt/cultivated on custom terrain:
- Paint only appears as thin strips at the very edges of the mesh
- Appears as if 3 layers of paint mask are being applied instead of 1
- The more you paint, the more accumulated pixels show in logs but visual result stays wrong

## What Has Already Been Checked (DO NOT REVISIT)

### 1. UV Mapping - VERIFIED CORRECT ?
Full diagnostic run on 03/12/2026 10:02:13 AM confirmed:
```
=== MESH UV0 RANGE ===
UV U range: 0.0000 to 1.0000 (expected: 0 to 1)
UV V range: 0.0000 to 1.0000 (expected: 0 to 1)

=== VERTEX UV vs PAINT UV COMPARISON ===
  [0] MeshUV=(0.359, 0.000), PaintUV=(0.359, 0.000), err=(0.000, 0.000) OK
  [1187] MeshUV=(0.694, 0.312), PaintUV=(0.694, 0.312), err=(0.000, 0.000) OK
  [2375] MeshUV=(0.880, 0.511), PaintUV=(0.880, 0.511), err=(0.000, 0.000) OK
  [3562] MeshUV=(0.310, 0.711), PaintUV=(0.310, 0.711), err=(0.000, 0.000) OK
  [4749] MeshUV=(0.583, 1.000), PaintUV=(0.583, 1.010), err=(0.000, 0.010) OK

UV mapping looks correct!
```

Bounds also verified:
```
Min X difference: 0.000 OK
Min Z difference: 0.000 OK  
Size X difference: 0.157 OK
Size Z difference: 0.400 OK
```

### 2. Shader Properties - VERIFIED CORRECT ?
- Material has `_ClearedMaskTex` property
- Shader is `Custom/Heightmap` (correct vanilla shader)
- FilterMode is Point (matches vanilla)
- Paint mask size: 82x82 (matches grid: 82x82)

### 3. Biome Painting - WORKS PERFECTLY ?
- Biome overlay via vertex colors works as expected
- This system should NOT be touched
- Uses `PaintBiomeOverlayVertexColors()` which directly modifies `mesh.colors32`

## Root Cause: Multiple Overlapping Paint Masks

**The issue is NOT UV mapping. UVs are verified correct.**

The visual symptom (paint appearing as thin edge strips) strongly suggests that multiple paint mask textures are being layered/competing for the same mesh. This could happen if:

1. **Multiple `_ClearedMaskTex` assignments from different sources**
   - Material created from Heightmap may already have a paint mask
   - We then set our own paint mask on top
   - Shader may be blending them or one is being ignored

2. **Material copied from vanilla Heightmap retains its paint data**
   - When we do `new Material(hmRenderer.sharedMaterial)` we copy ALL properties
   - The vanilla Heightmap's `_ClearedMaskTex` comes along with it
   - We set our own mask but the shader may reference the wrong one

3. **MaterialPropertyBlock conflicts with material properties**
   - We set `_ClearedMaskTex` on both `_terrainMaterial` AND via `MaterialPropertyBlock`
   - These may be competing or overwriting each other

Evidence from logs:
```
[VertexPaintSystem] UV range: U=[0.074,0.756], V=[0.636,0.997], painted 914 vertices to 914 unique pixels
```

914 pixels painted to an 82x82 texture (6724 total pixels) = ~14% coverage
Yet visually only thin edge strips appear = something is masking/overriding our paint

## Fix Applied - v2 (03/12/2026 14:30)

### Previous Fix (Material Inheritance) - DID NOT SOLVE THE ISSUE
Clearing inherited `_ClearedMaskTex` from vanilla Heightmap materials was correct but didn't fix the visual issue.

### NEW ROOT CAUSE IDENTIFIED
The `VertexPaintSystem` was over-engineered. It was:
1. Searching for mesh vertices within paint radius
2. Converting vertex UVs to pixel coordinates
3. Rebuilding the entire texture from vertex paint data

This is WRONG. Vanilla doesn't do this at all!

### How Vanilla Actually Paints (TerrainComp.PaintCleared):
```csharp
// 1. Convert world position to pixel coordinates
WorldToVertexMask(worldPos, out x, out y);

// 2. Calculate pixel radius
float pixelRadius = radius / m_hmap.m_scale;

// 3. For each pixel in radius, set paint color
for each pixel in radius:
    float falloff = Pow(1 - dist/radius, 0.1f);
    pixels[idx] = Color.Lerp(current, paintColor, falloff);

// 4. Apply texture
m_paintMask.Apply();
```

That's it. No vertex searching. No UV remapping. Just direct pixel painting.

### New Implementation: PaintDirectToTexture()
Changed `PaintAtWorldPosition()` to use simple direct pixel painting:

```csharp
private void PaintDirectToTexture(Vector3 worldPos, float radius, PaintType paintType, float strength)
{
    // 1. Convert world to UV (0-1)
    float centerU = (worldPos.x - bounds.min.x) / bounds.size.x;
    float centerV = (worldPos.z - bounds.min.z) / bounds.size.z;
    
    // 2. Convert to pixel coordinates
    int centerX = Mathf.RoundToInt(centerU * (maskWidth - 1));
    int centerY = Mathf.RoundToInt(centerV * (maskHeight - 1));
    
    // 3. Convert radius to pixel radius
    float pixelRadius = radius * (maskWidth / bounds.size.x);
    
    // 4. Paint each pixel within radius
    for each pixel in radius:
        float falloff = Pow(1 - dist/pixelRadius, 0.1f);
        pixels[idx] = Color.Lerp(current, paintColor, falloff);
    
    // 5. Apply
    _combinedPaintMask.SetPixels(pixels);
    _combinedPaintMask.Apply();
    ApplyPaintMaskToMaterial();
}
```

### Why This Should Work Now
- **Biome vertex colors work** because we paint directly to `mesh.colors32`
- **Paint mask should work the same way** - paint directly to texture pixels
- No intermediate vertex searching or UV remapping to go wrong
- Same approach as vanilla TerrainComp

---

## Previous Investigation (Reference)

### Step 1: Identify All Paint Mask Sources (PRIORITY)

Audit every place that sets `_ClearedMaskTex`:

| Location | Method | When Called |
|----------|--------|-------------|
| `DirtFloorMeshCombiner.Paint.cs` | `SetupMaterial()` | Material creation |
| `DirtFloorMeshCombiner.Paint.cs` | `ApplyPaintMaskToMaterial()` | After painting |
| `DirtFloorMeshCombiner.Paint.cs` | `SetPaintMask()` | Undo/redo |
| `DirtFloorMeshCombiner.Paint.cs` | `LoadPaintMaskData()` | Persistence restore |
| `DirtFloorMeshCombiner.Paint.cs` | `RemapPaintMaskToNewBounds()` | Bounds change |
| `DirtFloorMeshCombiner.Paint.cs` | `ClearPaintInRegion()` | Clearing paint |
| `VertexPaintSystem.cs` | `RebuildPaintMaskTexture()` | After vertex paint |
| `TerrainMaterialCache.cs` | `CreateMaterial()` | Material creation - **SUSPECT** |

**Key Question:** When `TerrainMaterialCache.CreateMaterial()` copies from a vanilla Heightmap material, does the copied material ALREADY have a `_ClearedMaskTex` with vanilla paint data?

### Step 2: Check Material Creation Flow

In `SetupMaterial()`:
```csharp
_terrainMaterial = new Material(hmRenderer.sharedMaterial);
```

This copies ALL shader properties including `_ClearedMaskTex` from the source Heightmap.
The source Heightmap may have paint data (cultivated areas, paths, etc.) that we're inheriting.

**Proposed Fix:** After creating material, IMMEDIATELY clear/replace `_ClearedMaskTex` with our blank texture BEFORE any other operations.

### Step 3: Verify Single Texture Instance

Add debug logging to verify:
- `_combinedPaintMask` instance ID stays constant
- Material's `_ClearedMaskTex` instance ID matches `_combinedPaintMask`
- No other texture is being set to `_ClearedMaskTex`

## Proposed Fixes

### Fix Option A: Clear Inherited Paint Mask on Material Creation

When creating material from vanilla Heightmap, immediately clear the inherited `_ClearedMaskTex`:

```csharp
_terrainMaterial = new Material(hmRenderer.sharedMaterial);
// IMMEDIATELY clear inherited paint mask
if (_terrainMaterial.HasProperty(s_ClearedMaskTexProperty))
{
    _terrainMaterial.SetTexture(s_ClearedMaskTexProperty, null); // Clear first
}
```

### Fix Option B: Create Material WITHOUT Copying Textures

Instead of `new Material(source)`, manually copy only the properties we need:
- Shader
- Texture arrays (_DiffuseArrayTex, _NormalArrayTex)
- Scalar properties
- NOT the `_ClearedMaskTex`

### Fix Option C: Single Paint Application Point

Consolidate ALL paint mask texture assignments to ONE method:
```csharp
private void SetPaintMaskOnMaterial(Texture2D paintMask)
{
    // This is the ONLY place _ClearedMaskTex is ever set
    _terrainMaterial.SetTexture(s_ClearedMaskTexProperty, paintMask);
    // Do NOT also use MaterialPropertyBlock for same property
}
```

Remove the `MaterialPropertyBlock` approach which may be conflicting.

## Vanilla Code Reference Needed

To properly understand how vanilla avoids this issue, we need:
1. `Heightmap.Awake()` or initialization - how vanilla creates its material
2. `Heightmap.RenderThread()` - how vanilla generates paint mask
3. How vanilla's material is set up - does it create fresh or copy?

## Next Steps

1. Add debug logging to track texture instance IDs through the flow
2. Check what texture is on material AFTER `TerrainMaterialCache.CreateMaterial()`
3. Implement Fix Option A as quickest test
4. If that fails, implement Fix Option B or C

## Files to Modify

- `FiresNPCs\Modules\CustomTerrain\Paint\DirtFloorMeshCombiner.Paint.cs` - `SetupMaterial()`
- `FiresNPCs\Modules\CustomTerrain\Utilities\TerrainMaterialCache.cs` - Check material creation
- `FiresNPCs\Modules\CustomTerrain\Paint\VertexPaintSystem.cs` - May need to remove redundant texture sets
