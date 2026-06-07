# Unified Terrain Paint System Fix

## Date: 2026-03-04
## Status: ?? INVESTIGATION

---

## Executive Summary

Our custom terrain systems have grown complex with multiple overlapping responsibilities. The `ClutterPaintTool` works perfectly - spawning ~200 instances exactly where expected. We need to make terrain paint, biome paint, and grass clearing work equally well.

---

## Current System Analysis

### ? What Works Well

| System | Status | Evidence |
|--------|--------|----------|
| `ClutterPaintTool` | ? WORKS | "Painted 199 instances of instanced_forest_groundcover" |
| Terrain Creation | ? WORKS | "Created disk and merged with X nearby disks" |
| Paint Mask Saving | ? WORKS | "Saved paint mask to TerrainComp (id=1652_-134, size=19487 bytes)" |
| Biome Overlay Painting | ?? PARTIAL | Paints but removes previous overlaps |

### ? What's Broken

| System | Status | Evidence |
|--------|--------|----------|
| Terrain Paint Visual | ? BROKEN | Paint saved but not visible |
| Clutter Clearing | ? BROKEN | "Cleared 0 clutter objects" - doesn't clear vanilla/combiner grass |
| Biome Paint Blending | ? BROKEN | "Removed 1 overlapping paints" - deletes instead of blends |

---

## Root Cause Analysis

### Problem 1: Paint Mask Not Visually Applying

**Symptom:** Paint operations log success, paint mask saves to TerrainComp, but terrain appearance doesn't change.

**Flow Analysis:**
```
TerrainOperationExecutor.ExecutePaint()
  ? DirtFloorTerrainManager.PaintArea()
    ? DirtFloorMeshCombiner.PaintAllLayersAt()
      ? combiner.PaintAtWorldPosition()
        ? Updates _combinedPaintMask texture
        ? Sets _terrainMaterial.SetTexture("_ClearedMaskTex", _combinedPaintMask)
```

**Suspected Issues:**
1. Material's shader may not have `_ClearedMaskTex` property
2. Material may not be using Valheim's terrain shader
3. Paint mask UV mapping may not match mesh UVs
4. Shader property ID mismatch (`s_ClearedMaskTexProperty`)

**Investigation Needed:**
```csharp
// Add debug logging to verify:
Debug.Log($"Material shader: {_terrainMaterial.shader.name}");
Debug.Log($"Has _ClearedMaskTex: {_terrainMaterial.HasProperty("_ClearedMaskTex")}");
Debug.Log($"Paint mask size: {_combinedPaintMask.width}x{_combinedPaintMask.height}");
```

### Problem 2: Clutter Not Being Cleared

**Symptom:** `ClutterPaintTool.ClearClutter()` reports 0 cleared, but grass remains visible.

**Root Cause:** The method only tracks objects spawned BY the ClutterPaintTool in `_spawnedClutter` list. It does NOT clear:
- Vanilla ClutterSystem instanced grass
- Combiner-spawned grass from `DirtFloorMeshCombiner.SpawnGrassOnMesh()`

**The Problem:**
```csharp
// ClutterPaintTool.ClearClutter() - ONLY checks _spawnedClutter list
for (int i = _spawnedClutter.Count - 1; i >= 0; i--)
{
    var obj = _spawnedClutter[i];  // ? Only OUR spawned objects
    // ...
}
```

**What We Need:**
1. Clear vanilla ClutterSystem grass: `ClutterSystem.instance.ResetGrass(center, radius)`
2. Clear combiner grass: `combiner.ClearGrassInRadius(center, radius)`
3. Mark vanilla terrain patches for regeneration

### Problem 3: Biome Paint Deleting Previous

**Symptom:** Each new biome paint removes overlapping previous paints instead of blending.

**Root Cause:** `BiomePaintPersistence` intentionally removes overlaps to prevent accumulation:
```csharp
// BiomePaintPersistence - removes overlaps
Debug.Log($"Removed {removed} overlapping paints to make room for new {biome} paint");
```

**Design Decision:** Is this intentional? If we want blending, we need to change the persistence strategy.

---

## Proposed Solution: Unified Paint Controller

### Architecture Goal

Make custom terrain behave like vanilla terrain:
- **One paint mask per combiner** (like vanilla's one TerrainComp per Heightmap)
- **Paint operations update the mesh material immediately**
- **Clutter clearing affects ALL grass sources**
- **Consistent coordinate mapping**

### Implementation Plan

#### Phase 1: Fix Paint Visual (Priority: HIGH)

**File:** `DirtFloorMeshCombiner.Paint.cs`

1. **Verify shader property exists:**
```csharp
private static readonly int s_ClearedMaskTexProperty = Shader.PropertyToID("_ClearedMaskTex");

// In SetupMaterial():
if (!_terrainMaterial.HasProperty(s_ClearedMaskTexProperty))
{
    Debug.LogWarning($"[DirtFloorMeshCombiner] Material shader '{_terrainMaterial.shader.name}' missing _ClearedMaskTex!");
    // Try alternative property names used by different shaders
    TryAlternativeShaderProperties();
}
```

2. **Force material update after paint:**
```csharp
public void PaintAtWorldPosition(...)
{
    // ... paint pixels ...
    
    _combinedPaintMask.Apply(false); // Don't rebuild mipmaps
    
    // FORCE material update
    if (_terrainMaterial != null)
    {
        _terrainMaterial.SetTexture(s_ClearedMaskTexProperty, _combinedPaintMask);
        
        // Debug verification
        Debug.Log($"[Paint] Applied to material. Shader={_terrainMaterial.shader.name}, " +
                  $"HasProp={_terrainMaterial.HasProperty(s_ClearedMaskTexProperty)}");
    }
    
    // Also update MeshRenderer
    if (_meshRenderer != null)
    {
        _meshRenderer.material = _terrainMaterial; // Force material refresh
    }
}
```

#### Phase 2: Fix Clutter Clearing (Priority: HIGH)

**File:** `DirtFloorTerrainManager.cs`

Create a unified clutter clear method:
```csharp
/// <summary>
/// Clears ALL grass/clutter at a position - vanilla, combiner, and painted.
/// </summary>
public static void ClearAllGrassInRadius(Vector3 center, float radius)
{
    // 1. Clear vanilla ClutterSystem grass
    if (ClutterSystem.instance != null)
    {
        ClutterSystem.instance.ResetGrass(center, radius);
    }
    
    // 2. Clear combiner-spawned grass
    var combiners = DirtFloorMeshCombiner.GetCombinersInRadius(center, radius + 5f);
    foreach (var combiner in combiners)
    {
        if (combiner != null)
        {
            combiner.ClearGrassInRadius(center, radius);
        }
    }
    
    // 3. Clear ClutterPaintTool spawned grass
    ClutterPaintTool.ClearClutter(center, radius);
    
    Debug.Log($"[DirtFloorTerrainManager] Cleared all grass at {center}, radius {radius}");
}
```

**File:** `DirtFloorMeshCombiner.Grass.cs`

Add method to clear grass in radius:
```csharp
/// <summary>
/// Clears grass instances within a radius of a world position.
/// </summary>
public void ClearGrassInRadius(Vector3 center, float radius)
{
    if (_grassInstances == null || _grassInstances.Count == 0)
        return;
    
    float radiusSq = radius * radius;
    int cleared = 0;
    
    for (int i = _grassInstances.Count - 1; i >= 0; i--)
    {
        var grass = _grassInstances[i];
        if (grass == null)
        {
            _grassInstances.RemoveAt(i);
            continue;
        }
        
        float distSq = (grass.transform.position - center).sqrMagnitude;
        if (distSq <= radiusSq)
        {
            UnityEngine.Object.Destroy(grass);
            _grassInstances.RemoveAt(i);
            cleared++;
        }
    }
    
    if (cleared > 0)
    {
        Debug.Log($"[DirtFloorMeshCombiner] Cleared {cleared} grass instances");
    }
}
```

#### Phase 3: Update Paint Area to Clear Grass (Priority: MEDIUM)

**File:** `DirtFloorTerrainManager.cs`

```csharp
public static void PaintArea(Vector3 center, float radius, DirtFloorController.PaintType paintType)
{
    // Clear grass for paint types that shouldn't have vegetation
    bool shouldClearVegetation = paintType == DirtFloorController.PaintType.Paved ||
                                 paintType == DirtFloorController.PaintType.Dirt;
    
    if (shouldClearVegetation)
    {
        // Use unified clearing method
        ClearAllGrassInRadius(center, radius);
    }
    
    // Paint directly to combiners
    DirtFloorMeshCombiner.PaintAllLayersAt(center, radius, paintType);
    
    // Visual effect
    string effect = paintType == DirtFloorController.PaintType.Paved 
        ? "vfx_Place_stone_wall_2x1" 
        : "vfx_Place_cultivator";
    PlayTerrainEffect(center, effect);
}
```

---

## Testing Checklist

### After Phase 1 (Paint Visual):
- [ ] Paint paved - should show gray stone texture
- [ ] Paint cultivated - should show brown tilled soil
- [ ] Paint dirt - should show brown path
- [ ] Paint grass - should reset to green grass

### After Phase 2 (Clutter Clearing):
- [ ] Paint paved on grassy area - grass should disappear
- [ ] Paint paved on combiner grass - combiner grass should disappear
- [ ] Paint paved on ClutterPaintTool grass - painted grass should disappear

### After Phase 3 (Integration):
- [ ] Full workflow: Create terrain ? Paint paved ? No grass visible
- [ ] Persistence: Logout ? Login ? Paint still visible, no grass

---

## Files to Modify

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.Paint.cs` | Add shader property verification, force material update |
| `DirtFloorMeshCombiner.Grass.cs` | Add `ClearGrassInRadius()` method |
| `DirtFloorTerrainManager.cs` | Add `ClearAllGrassInRadius()`, update `PaintArea()` |
| `ClutterPaintTool.cs` | (Optional) Improve `ClearClutter()` to also trigger vanilla reset |

---

## Why ClutterPaintTool Works

The ClutterPaintTool works because it:
1. **Creates actual GameObjects** at specific positions
2. **Uses raycast** to find ground position precisely
3. **Tracks what it spawns** in `_spawnedClutter` list
4. **Persists via BiomePaintPersistence** which restores on zone load

The terrain paint should work similarly:
1. **Paint to texture** at specific UV coordinates
2. **Map world position ? UV** precisely
3. **Apply texture to material** immediately
4. **Persist via TerrainComp ZDO** which restores on zone load

The paint IS being stored (we see the save logs), but the visual update isn't working. Focus on Phase 1.

---

## Next Steps

1. **Add debug logging** to `PaintAtWorldPosition` to verify shader properties
2. **Check if shader is correct** - should be "Custom/Heightmap" or similar
3. **Verify UV mapping** - paint position should map to correct texture coordinate
4. **Test with manual material assignment** - force _meshRenderer.material = _terrainMaterial

---

## Related Documentation

- `TERRAIN_PAINT_FIX_ATTEMPTS.md` - Previous fix attempts
- `DIRT_FLOOR_OPTIMIZATION.md` - Combiner architecture
- `VALHEIM_SHADERS_REFERENCE.md` - Shader property names
- `INFINITY_HAMMER_DIRTFLOOR_TOOLS.yaml` - Tool definitions
