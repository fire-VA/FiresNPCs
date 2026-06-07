# Terrain Paint Visual Debug

## Date: 2026-03-05
## Status: ?? ADDED DIAGNOSTIC LOGGING

---

## Current Symptoms

From the logs:
1. Paint works on **single disks** but not combined disks
2. When disks are combined, paint gets "wiped"  
3. Log shows: `[DirtFloorMeshCombiner] OnPaintTerrain: No terrain at paint position - skipping`

---

## Diagnostic Logging Added

Added detailed logging to trace the paint flow:

### `PaintAllLayersAt` (DirtFloorMeshCombiner.Static.cs)
- Logs total combiners checked
- Logs why each combiner was skipped (no bounds, no height, too far vertically)
- Logs which combiner is actually painted

### `OnPaintTerrain` (DirtFloorMeshCombiner.Utilities.cs)
- Logs combiner position, bounds, disk count, grid info
- Logs `SampleHeightAt` result
- If no terrain found, logs additional debugging info

### `PaintDirectlyOnCombinedMask` (DirtFloorMeshCombiner.Utilities.cs)
- Logs bounds, UV coordinates, paint color
- Logs pixel counts: painted, skipped (no terrain, out of range, out of bounds)

### `SampleHeightAt` (DirtFloorMeshCombiner.DiskManagement.cs)
- Logs world-to-grid conversion details
- Logs grid origin, cell size, grid size
- Logs result (height found or why not)

---

## Expected Log Output When Working

```
[PAINT DEBUG] PaintAllLayersAt ENTRY: pos=(1640, 37, -122), radius=4.5, type=Paved, totalCombiners=5
[PAINT DEBUG] PAINTING on combiner at (1638, 37, -120), diskCount=3
[PAINT DEBUG] OnPaintTerrain START: pos=(1640, 37, -122), radius=4.5, type=Paved
[PAINT DEBUG] Combiner: pos=(1638, 37, -120), bounds=(min, max), diskCount=3
[PAINT DEBUG] Grid: 40x35, cellSize=0.5, gridOrigin=(1625, 0, -130)
[PAINT DEBUG] SampleHeightAt: worldPos=(1640, 37, -122), grid coords=(30, 16), height=37.01
[PAINT DEBUG] PaintDirectlyOnCombinedMask START...
[PAINT DEBUG] PaintDirectlyOnCombinedMask RESULT: painted=1500, skipped(noTerrain=200, ...)
[PAINT DEBUG] Painted on combined mask and 2 disk(s)
[PAINT DEBUG] Updated material paint mask texture
```

---

## Expected Log Output When Broken

```
[PAINT DEBUG] PaintAllLayersAt ENTRY: pos=(1640, 37, -122), radius=4.5, type=Paved, totalCombiners=5
[PAINT DEBUG] Combiner at (1638, 37, -120) - SampleHeightAt returned -100 (no terrain)
[PAINT DEBUG] PaintAllLayersAt RESULT: painted=0, skipped(noBounds=2, noHeight=3, tooFar=0)
```

This would indicate that `SampleHeightAt` is returning -100 even though there should be terrain.

---

## Root Cause Hypotheses

### Hypothesis 1: Grid Not Initialized After Merge

When disks merge:
1. `AddDisk()` is called
2. `RegenerateMesh()` is called which updates grid
3. BUT if the combiner was restored from cache and `_restoredFromCache=true`, mesh regeneration is skipped!

```csharp
public void RegenerateMesh()
{
    // ... 
    if (_restoredFromCache && _combinedMesh != null && _combinedMesh.vertexCount > 0)
    {
        // SKIPS grid setup!
        return;
    }
    
    CalculateCombinedBounds();
    SetupGrid();  // <- This sets _gridOrigin, _gridWidth, _heights, etc.
    // ...
}
```

### Hypothesis 2: Paint Mask Bounds vs Grid Bounds Mismatch

The paint mask uses `_combinedBounds` for UV mapping, but height sampling uses grid coordinates. If these are out of sync:
- Paint UV might map correctly
- But `SampleHeightAt` returns -100 because grid isn't set up

### Hypothesis 3: _heights Array Not Updated

When adding a disk to an existing combiner:
1. `AddDisk()` calls `RegenerateMesh()`
2. If restored from cache, `RegenerateMesh()` early-returns
3. `_heights` still has old data that doesn't include new disk area

---

## Potential Fix

In `AddDisk()`, force regeneration even if restored from cache:

```csharp
public void AddDisk(DirtFloorController disk)
{
    // ...
    _connectedDisks.Add(disk);
    
    // FORCE full regeneration when adding new disk
    _restoredFromCache = false;  // Clear cache flag
    _isActivelyEditing = true;   // Mark as editing
    
    RegenerateMesh();  // Now this won't skip
    
    _isActivelyEditing = false;
    // ...
}
```

---

## Testing Instructions

1. Load game with debug logging enabled
2. Place a single disk with `place_dirtfloor`
3. Paint on it - should see successful paint logs
4. Place a second overlapping disk (they merge)
5. Paint on merged terrain - check logs for what fails
6. Look for `SampleHeightAt` returning -100 and WHY

---

## Files Modified

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.Static.cs` | Added entry/result logging to `PaintAllLayersAt` |
| `DirtFloorMeshCombiner.Utilities.cs` | Added detailed logging to `OnPaintTerrain` and `PaintDirectlyOnCombinedMask` |
| `DirtFloorMeshCombiner.DiskManagement.cs` | Added detailed logging to `SampleHeightAt` |

The mesh uses **two UV channels**:

```csharp
// UV0 - Paint mask sampling (bounds-relative)
float paintU = (worldPos.x - _combinedBounds.min.x) / boundsWidth;
float paintV = (worldPos.z - _combinedBounds.min.z) / boundsDepth;
uvs.Add(new Vector2(paintU, paintV));

// UV1 - World texture tiling
float worldU = worldPos.x * 0.25f;
float worldV = worldPos.z * 0.25f;
uvs2.Add(new Vector2(worldU, worldV));
```

**Problem:** When `ZonePaintMaskManager` paints using **zone-based world-space** coordinates, but the mesh samples paint using **combiner-bounds-relative** UV0, there's a coordinate mismatch!

---

## The Custom/Heightmap Shader

Valheim's terrain shader uses:
- `_ClearedMaskTex` - Paint mask (R=dirt, G=cultivated, B=paved, A=vegetation)
- `_MainTex` - Base diffuse texture array
- `_BumpMap` - Normal map texture array
- Various biome-specific textures

The shader samples `_ClearedMaskTex` using UV0 coordinates to determine which terrain texture to show.

---

## Root Cause Analysis

### Scenario: Paint at World Position (1650, 36, -132)

1. **ZonePaintMaskManager** calculates zone UV:
   - Zone ID: (25, -3) for 64m zones
   - Zone UV: ((1650 % 64) / 64, ((-132 % 64) + 64) / 64) = (0.406, 0.5)
   
2. **Combiner** has bounds: min=(1640, -150), size=(30, 40)
   - World (1650, -132) ? UV0 = ((1650-1640)/30, ((-132)-(-150))/40) = (0.33, 0.45)
   
3. **Mismatch!** Zone paints at (0.406, 0.5) but mesh samples at (0.33, 0.45)

---

## Current Paint Flow (After Recent Changes)

```
User paints at worldPos
  ?
DirtFloorTerrainManager.PaintArea()
  ?
ZonePaintMaskManager.PaintAtWorldPosition()
  ? Stores paint in zone-level texture using zone-space UV
  ?
combiner.RefreshPaintFromZone()
  ? Iterates combiner's paint mask pixels
  ? For each pixel, converts to world position
  ? Samples from ZonePaintMaskManager using world position
  ? Sets combiner paint mask pixel
  ?
Material uses _ClearedMaskTex (combiner's paint mask)
Shader samples using UV0 (bounds-relative)
```

This SHOULD work because `RefreshPaintFromZone()` re-samples zone paint into combiner's bounds-relative paint mask.

---

## Possible Issues

### Issue 1: RefreshPaintFromZone Not Being Called

The recent change to `PaintAllLayersAt` now calls:
```csharp
ZonePaintMaskManager.PaintAtWorldPosition(...);  // Paint to zone
combiner.RefreshPaintFromZone();                  // Refresh combiner from zone
```

But is `RefreshPaintFromZone()` actually working?

### Issue 2: Multiple Combiners Refreshing

If multiple combiners cover the same area (at different heights), they might all refresh and show different paint.

### Issue 3: Material Not Updated After RefreshPaintFromZone

Check if `_terrainMaterial.SetTexture()` is called after refresh.

### Issue 4: Old Paint Data Still Present

The combiner might have old paint data that's not being cleared before refresh.

---

## Debug Steps

### Step 1: Add Logging to RefreshPaintFromZone

```csharp
public void RefreshPaintFromZone()
{
    Debug.Log($"[RefreshPaintFromZone] START: combiner at {transform.position}, bounds={_combinedBounds}");
    
    // ... existing code ...
    
    if (modified)
    {
        Debug.Log($"[RefreshPaintFromZone] Modified {modifiedCount} pixels");
        
        // Verify material update
        if (_terrainMaterial != null)
        {
            Debug.Log($"[RefreshPaintFromZone] Material shader: {_terrainMaterial.shader.name}");
            Debug.Log($"[RefreshPaintFromZone] Has _ClearedMaskTex: {_terrainMaterial.HasProperty("_ClearedMaskTex")}");
        }
    }
    else
    {
        Debug.Log($"[RefreshPaintFromZone] No changes");
    }
}
```

### Step 2: Verify Zone Paint Data

```csharp
// In PaintAllLayersAt, after painting to zone:
var testColor = ZonePaintMaskManager.GetPaintAtWorldPosition(worldPos);
Debug.Log($"[PaintAllLayersAt] Zone paint at {worldPos}: R={testColor.r:F2}, G={testColor.g:F2}, B={testColor.b:F2}");
```

### Step 3: Check UV Mapping

Add temporary visualization:
```csharp
// In BuildMesh, log UV values for a few vertices
if (vertexIndex < 5)
{
    Debug.Log($"[BuildMesh] Vertex {vertexIndex}: worldPos={worldPos}, UV0=({paintU:F3}, {paintV:F3}), UV1=({worldU:F3}, {worldV:F3})");
}
```

---

## Alternative Fix: Bypass Zone System

If the zone system is causing issues, we could go back to direct combiner painting:

```csharp
public static void PaintAllLayersAt(Vector3 worldPos, float radius, 
    DirtFloorController.PaintType paintType, float heightThreshold = 10f)
{
    foreach (var combiner in s_allCombiners)
    {
        if (combiner == null) continue;
        
        // Check bounds and height
        if (!CombinerIsInRange(combiner, worldPos, radius, heightThreshold))
            continue;
        
        // Paint DIRECTLY to combiner (not via zone)
        combiner.PaintAtWorldPosition(worldPos, radius, paintType, 1f);
    }
}
```

This is simpler and the UV mapping is self-consistent (bounds-relative ? bounds-relative).

---

## Next Actions

1. [ ] Add debug logging to trace paint flow
2. [ ] Verify `RefreshPaintFromZone()` is being called
3. [ ] Check if zone paint data is correct
4. [ ] Consider reverting to direct combiner painting if zone system is buggy

---

## Files to Investigate

| File | What to Check |
|------|---------------|
| `DirtFloorMeshCombiner.Static.cs` | `PaintAllLayersAt()` - is zone paint working? |
| `DirtFloorMeshCombiner.Paint.cs` | `RefreshPaintFromZone()` - is it being called and working? |
| `ZonePaintMaskManager.cs` | `GetPaintAtWorldPosition()` - is it returning correct data? |
| `DirtFloorMeshCombiner.MeshGen.cs` | `BuildMesh()` - are UV0 coordinates correct? |
