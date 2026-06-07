# Vanilla Valheim Terrain System Analysis

## Date: 2026-03-04
## Purpose: Understand vanilla terrain to fix our custom terrain system

---

## Key Findings from Decompiled Code

### 1. Paint Mask System

**Heightmap.cs - Paint Colors:**
```csharp
public static Color m_paintMaskDirt = new Color(1f, 0.0f, 0.0f, 1f);       // R channel
public static Color m_paintMaskCultivated = new Color(0.0f, 1f, 0.0f, 1f); // G channel
public static Color m_paintMaskPaved = new Color(0.0f, 0.0f, 1f, 1f);      // B channel
public static Color m_paintMaskNothing = new Color(0.0f, 0.0f, 0.0f, 1f);  // No RGB, full alpha
public static Color m_paintMaskClearVegetation = new Color(0.0f, 0.0f, 0.0f, 0.0f); // Clear alpha
```

**KEY INSIGHT:** 
- RGB channels = terrain texture paint (dirt/cultivated/paved)
- **Alpha channel = vegetation mask** (used to control grass spawning AND lava in Ashlands!)

### 2. Shader Property

```csharp
private static int s_shaderPropertyClearedMaskTex = Shader.PropertyToID("_ClearedMaskTex");

// In Regenerate():
this.m_materialInstance.SetTexture(Heightmap.s_shaderPropertyClearedMaskTex, (Texture) this.m_paintMask);
```

**The shader property is `_ClearedMaskTex`** - this is what we need to set on our material.

### 3. IsCleared() - How Clutter Decides to Spawn

```csharp
public bool IsCleared(Vector3 worldPos)
{
    worldPos.x -= 0.5f;
    worldPos.z -= 0.5f;
    int x, y;
    this.WorldToVertexMask(worldPos, out x, out y);
    Color pixel = this.m_paintMask.GetPixel(x, y);
    return (double) pixel.r > 0.5 || (double) pixel.g > 0.5 || (double) pixel.b > 0.5;
}
```

**KEY INSIGHT:** Terrain is "cleared" if ANY of R, G, or B > 0.5. This is how ClutterSystem knows NOT to spawn grass.

### 4. ClutterSystem - How It Checks Paint

In `GenerateVegPatch()`:
```csharp
if (!clutter.m_onCleared || !clutter.m_onUncleared)
{
    bool flag = hmap.IsCleared(point);
    if (clutter.m_onCleared && !flag || clutter.m_onUncleared & flag)
        continue;  // Skip spawning this clutter instance
}
```

**KEY INSIGHT:**
- Most grass has `m_onUncleared = true, m_onCleared = false`
- So grass only spawns where `IsCleared() == false` (no paint)
- Some special clutter (like cultivated dirt texture) has `m_onCleared = true`

### 5. ResetGrass() - How Grass is Cleared

```csharp
public void ResetGrass(Vector3 center, float radius)
{
    float num = this.m_grassPatchSize / 2f;
    foreach (KeyValuePair<Vector2Int, ClutterSystem.PatchData> patch in this.m_patches)
    {
        Vector3 center1 = patch.Value.center;
        // Check if patch overlaps with radius
        if (overlaps)
        {
            patch.Value.m_reset = true;  // Mark for rebuild
            this.m_forceRebuild = true;
        }
    }
}
```

**KEY INSIGHT:** `ResetGrass()` doesn't destroy grass objects immediately. It marks patches for rebuild on next frame. The rebuild will re-check `IsCleared()` and skip spawning grass where paint exists.

### 6. TerrainComp - Paint Persistence

```csharp
private void PaintCleared(Vector3 worldPos, float radius, TerrainModifier.PaintType paintType, ...)
{
    // Convert world pos to texture coordinates
    int x1, y1;
    this.m_hmap.WorldToVertexMask(worldPos, out x1, out y1);
    
    // Calculate pixel radius
    float f = radius / this.m_hmap.m_scale;
    int num2 = Mathf.CeilToInt(f);
    
    // For each pixel in radius
    for (int y2 = y1 - num2; y2 <= y1 + num2; ++y2)
    {
        for (int x2 = x1 - num2; x2 <= x1 + num2; ++x2)
        {
            // Distance-based falloff
            float t = Mathf.Pow(1f - Mathf.Clamp01(dist / f), 0.1f);
            
            Color a2 = this.m_hmap.GetPaintMask(x2, y2);
            float a3 = a2.a;  // PRESERVE ALPHA!
            
            switch (paintType)
            {
                case PaintType.Dirt:
                    a2 = Color.Lerp(a2, Heightmap.m_paintMaskDirt, t);
                    break;
                case PaintType.Cultivate:
                    a2 = Color.Lerp(a2, Heightmap.m_paintMaskCultivated, t);
                    break;
                case PaintType.Paved:
                    a2 = Color.Lerp(a2, Heightmap.m_paintMaskPaved, t);
                    break;
                case PaintType.Reset:
                    a2 = Color.Lerp(a2, Heightmap.m_paintMaskNothing, t);
                    break;
            }
            a2.a = a3;  // RESTORE ALPHA!
            
            this.m_modifiedPaint[index] = true;
            this.m_paintMask[index] = a2;
        }
    }
}
```

**KEY INSIGHTS:**
1. **Alpha is preserved separately** - RGB is paint, Alpha is vegetation mask
2. **Uses `Color.Lerp` with falloff** for soft edges
3. **Falloff power is 0.1f** - very steep falloff at edges
4. **Stores in `m_paintMask` array AND marks `m_modifiedPaint`**

### 7. The Complete Paint ? Grass Flow

```
1. Player uses hoe/cultivator
2. TerrainOp.Awake() ? TerrainComp.ApplyOperation()
3. TerrainComp.DoOperation() ?
   - TerrainComp.PaintCleared() - modifies m_paintMask array
   - TerrainComp.Save() - saves to ZDO
   - Heightmap.Poke(false) - triggers rebuild
   - ClutterSystem.ResetGrass() - marks patches for rebuild

4. Heightmap.Regenerate() ?
   - TerrainComp.ApplyToHeightmap() - copies paint to Heightmap.m_paintMask texture
   - m_paintMask.Apply() - uploads texture to GPU
   - m_materialInstance.SetTexture("_ClearedMaskTex", m_paintMask)

5. ClutterSystem.UpdateGrass() ?
   - GenerateVegPatch() - for each clutter position:
     - hmap.IsCleared(point) - checks if R/G/B > 0.5
     - If cleared AND clutter.m_onUncleared: skip spawning
```

---

## What We're Doing Wrong

### Problem 1: Our Paint Mask Isn't Connected to IsCleared()

Our `DirtFloorMeshCombiner` has its own paint mask, but:
- ClutterSystem still calls `hmap.IsCleared()` on the **vanilla Heightmap**
- Our custom terrain doesn't have a Heightmap underneath
- So `IsCleared()` returns false (no paint) and grass spawns anyway

**Solution:** We need to either:
1. Patch `ClutterSystem.GetGroundInfo()` to check our terrain
2. Patch `Heightmap.IsCleared()` to delegate to our terrain when appropriate
3. OR manage our own grass spawning (which we do) and clear it ourselves

### Problem 2: Our Grass Clearing Doesn't Work

`ClutterPaintTool.ClearClutter()` only clears objects in `_spawnedClutter` list.
- Vanilla `ClutterSystem.ResetGrass()` marks patches for rebuild
- Our combiner-spawned grass isn't tracked by ClutterPaintTool
- Vanilla grass isn't tracked by us at all

**Solution:** When painting on our terrain:
1. Call `ClutterSystem.instance.ResetGrass()` (handles vanilla grass)
2. Clear our combiner-spawned grass instances
3. Clear ClutterPaintTool spawned grass

### Problem 3: Our Paint Mask Format May Be Wrong

Vanilla uses:
- `m_paintMask` is `Texture2D` with size `(m_width + 1) x (m_width + 1)`
- For 32-width heightmap: 33x33 = 1089 pixels
- UV mapping: `WorldToVertexMask()` converts world pos to pixel coords

Our combiner uses:
- `COMBINED_PAINT_MASK_SIZE = 256` (constant)
- UV mapping based on `_combinedBounds`

**This could cause resolution/mapping issues.**

### Problem 4: We're Not Using the Right Shader Property

We have:
```csharp
private static readonly int s_ClearedMaskTexProperty = Shader.PropertyToID("_ClearedMaskTex");
```

This looks correct, but we need to verify our material actually has this property.

---

## Correct Implementation Plan

### Step 1: Verify Shader Property (Immediate)

Add debug logging:
```csharp
Debug.Log($"[Paint] Material: {_terrainMaterial.shader.name}");
Debug.Log($"[Paint] Has _ClearedMaskTex: {_terrainMaterial.HasProperty("_ClearedMaskTex")}");
```

### Step 2: Fix Grass Clearing (Priority 1)

In `DirtFloorTerrainManager.PaintArea()`:
```csharp
public static void PaintArea(Vector3 center, float radius, PaintType paintType)
{
    bool shouldClearGrass = paintType != PaintType.Reset && paintType != PaintType.Grass;
    
    if (shouldClearGrass)
    {
        // 1. Clear vanilla ClutterSystem grass
        if (ClutterSystem.instance != null)
        {
            ClutterSystem.instance.ResetGrass(center, radius);
        }
        
        // 2. Clear our combiner grass
        var combiners = DirtFloorMeshCombiner.GetCombinersInRadius(center, radius + 5f);
        foreach (var combiner in combiners)
        {
            combiner.ClearGrassInRadius(center, radius);
        }
        
        // 3. Clear ClutterPaintTool spawned grass
        ClutterPaintTool.ClearClutter(center, radius);
    }
    
    // Apply paint
    DirtFloorMeshCombiner.PaintAllLayersAt(center, radius, paintType);
}
```

### Step 3: Implement ClearGrassInRadius (Priority 1)

In `DirtFloorMeshCombiner.Grass.cs`:
```csharp
public void ClearGrassInRadius(Vector3 center, float radius)
{
    // Clear instanced grass containers
    if (_grassContainers != null)
    {
        float radiusSq = radius * radius;
        for (int i = _grassContainers.Count - 1; i >= 0; i--)
        {
            var container = _grassContainers[i];
            if (container == null)
            {
                _grassContainers.RemoveAt(i);
                continue;
            }
            
            // Check if container overlaps with radius
            float distSq = (container.transform.position - center).sqrMagnitude;
            if (distSq <= (radius + 10f) * (radius + 10f))
            {
                // Container might be affected - need to rebuild or clear instances
                var renderer = container.GetComponent<InstanceRenderer>();
                if (renderer != null)
                {
                    // For now, just destroy and let it rebuild
                    Object.Destroy(container);
                    _grassContainers.RemoveAt(i);
                }
            }
        }
    }
    
    // Mark for grass respawn
    _needsGrassRespawn = true;
}
```

### Step 4: Match Vanilla Paint Format (Priority 2)

Ensure our paint colors match vanilla exactly:
```csharp
private Color GetPaintColor(PaintType paintType)
{
    switch (paintType)
    {
        case PaintType.Dirt:
            return Heightmap.m_paintMaskDirt;  // Use Heightmap's constants!
        case PaintType.Cultivate:
            return Heightmap.m_paintMaskCultivated;
        case PaintType.Paved:
            return Heightmap.m_paintMaskPaved;
        default:
            return Heightmap.m_paintMaskNothing;
    }
}
```

### Step 5: Implement IsCleared() for Our Terrain (Priority 2)

```csharp
public bool IsCleared(Vector3 worldPos)
{
    if (_combinedPaintMask == null) return false;
    
    // Convert world to UV
    float u = (worldPos.x - _combinedBounds.min.x) / _combinedBounds.size.x;
    float v = (worldPos.z - _combinedBounds.min.z) / _combinedBounds.size.z;
    
    if (u < 0 || u > 1 || v < 0 || v > 1) return false;
    
    int x = Mathf.FloorToInt(u * (COMBINED_PAINT_MASK_SIZE - 1));
    int y = Mathf.FloorToInt(v * (COMBINED_PAINT_MASK_SIZE - 1));
    
    Color pixel = _combinedPaintMask.GetPixel(x, y);
    
    // Match vanilla logic exactly
    return pixel.r > 0.5f || pixel.g > 0.5f || pixel.b > 0.5f;
}
```

---

## Testing Checklist

After implementing fixes:

1. [ ] Paint paved on custom terrain ? visual shows gray stone
2. [ ] Paint paved ? vanilla ClutterSystem grass disappears (ResetGrass called)
3. [ ] Paint paved ? our combiner grass disappears
4. [ ] Paint paved ? ClutterPaintTool grass disappears
5. [ ] Paint cultivated ? brown tilled soil visible
6. [ ] Paint reset/grass ? default grass texture returns
7. [ ] Save/load ? paint persists correctly
8. [ ] Multiple overlapping disks ? paint applies correctly without seams

---

## Files to Modify

| File | Changes |
|------|---------|
| `DirtFloorTerrainManager.cs` | Fix `PaintArea()` to clear all grass types |
| `DirtFloorMeshCombiner.Grass.cs` | Add `ClearGrassInRadius()` method |
| `DirtFloorMeshCombiner.Paint.cs` | Add `IsCleared()` method, verify shader property |
| `ClutterPaintTool.cs` | (Optional) Improve to also call vanilla ResetGrass |

---

## Summary

The vanilla system is elegant:
1. Paint mask texture (RGB = paint type, A = vegetation mask)
2. Shader samples `_ClearedMaskTex` to blend terrain textures
3. `IsCleared()` checks RGB > 0.5 to determine if grass should spawn
4. `ResetGrass()` marks patches for rebuild, rebuild checks `IsCleared()`

Our system needs to:
1. Ensure `_ClearedMaskTex` is set correctly on our material
2. Clear ALL grass sources when painting (vanilla, combiner, ClutterPaintTool)
3. Implement `IsCleared()` for our own grass spawning logic
4. Use the same paint color values as vanilla

---

## Implementation Status (2025-03-04)

### ? Implemented

1. **`DirtFloorMeshCombiner.ClearGrassInRadius()`** - Clears combiner-spawned grass in a radius
2. **`DirtFloorMeshCombiner.IsCleared()`** - Matches vanilla logic for checking if terrain is painted
3. **`DirtFloorTerrainManager.PaintArea()`** - Now clears ALL grass sources:
   - Vanilla `ClutterSystem.ResetGrass()`
   - Combiner grass via `ClearGrassInRadius()`
   - ClutterPaintTool grass via `ClearClutter()`

### ? Fixed (2025-03-04) - Zone-Level Paint

**The Core Problem:** Paint was stored per-combiner, causing seams at combiner boundaries.

**The Fix:** `PaintAllLayersAt` now uses `ZonePaintMaskManager` for zone-level paint storage:

1. **Zone-Level Paint Storage** - Paint stored in 64m zones (like vanilla Heightmap)
2. **World-Space UV Mapping** - Same world position always maps to same UV
3. **Combiner Refresh** - Combiners sample from zone paint via `RefreshPaintFromZone()`

**Before:**
```
Paint ? Each combiner's individual mask ? Seams at boundaries
```

**After:**
```
Paint ? ZonePaintMaskManager (zone-level) ? Combiners refresh from zone ? Seamless
```

### ?? Still Needs Testing

1. Verify paint visual shows correctly across combiner boundaries
2. Verify grass respawns correctly in unpainted areas
3. Verify persistence works after save/load

### ?? Files Modified

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.Grass.cs` | Added `ClearGrassInRadius()`, `IsCleared()`, made `ClearGrass()` public |
| `DirtFloorMeshCombiner.Static.cs` | **FIXED:** `PaintAllLayersAt()` now uses `ZonePaintMaskManager` |
| `DirtFloorTerrainManager.cs` | Updated `PaintArea()` to clear all grass sources |
