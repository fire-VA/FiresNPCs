# Biome Overlay & Clutter Paint System - Changelog

## Session Date: Latest Changes (V3.1)

### Summary
Fixed biome overlay painting - removed the `ForceHeightmapRegeneration()` call that was immediately wiping painted colors.

---

## The Bug (V3 Issue)

The V3 approach was correct but had a critical bug in the execution order:

1. `BiomeOverlayPainter.PaintOverlay()` paints vertex colors ?
2. Calls `BiomePaintPersistence.RecordBiomePaint()` ?
3. `RecordBiomePaint()` saves to ZDO ?
4. **BUG:** `RecordBiomePaint()` calls `ForceHeightmapRegeneration()` ?
5. `Heightmap.Regenerate()` rebuilds mesh and **wipes our paint** ?
6. Our `RebuildRenderMesh` patch runs but paint is gone ?

**Result:** Paint flashes on screen for one frame then disappears.

---

## The Fix (V3.1)

**Removed `ForceHeightmapRegeneration()` from `RecordBiomePaint()`**

The regeneration is NOT needed during initial paint because:
1. `BiomeOverlayPainter.PaintOverlay()` already applies the visual change
2. The cache is set up for future `Regenerate()` calls
3. When the zone reloads, `TerrainComp.Load` ? our postfix will populate cache
4. Then `Heightmap.Regenerate()` ? our `RebuildRenderMesh` patch will apply colors

**Correct Flow Now:**
```
Initial Paint:
PaintOverlay() ? modify vertex colors ? RecordBiomePaint() ? save to ZDO + cache
                                                          ?
                                                    NO regeneration
                                                    (paint stays visible)

Zone Reload:
TerrainComp.Load() ? [Our Postfix] load from ZDO ? update cache
                                                        ?
                                        Heightmap.Regenerate() (vanilla)
                                                        ?
                              [Our RebuildRenderMesh Postfix] apply cached colors
```

---

## Key Changes in V3

### `BiomePaintPersistence.cs`

**New: In-memory paint cache**
```csharp
// Cache of loaded paint data for fast access during RebuildRenderMesh
private static Dictionary<long, List<BiomePaintData>> _paintCacheByPosition;
private static bool _hasAnyPaintedAreas = false;
```

**New: GetPaintAtPosition() for fast lookup**
```csharp
public static BiomePaintData GetPaintAtPosition(float worldX, float worldZ)
{
    if (!_hasAnyPaintedAreas) return null;
    
    foreach (var paint in allPaints)
    {
        if (distance <= paint.Radius)
            return paint;
    }
    return null;
}
```

**New: Heightmap.RebuildRenderMesh Postfix Patch**
```csharp
[HarmonyPatch(typeof(Heightmap), "RebuildRenderMesh")]
[HarmonyPostfix]
[HarmonyPriority(Priority.Last - 5)]
private static void Heightmap_RebuildRenderMesh_Postfix(Heightmap __instance)
{
    if (!_hasAnyPaintedAreas) return;  // Fast exit
    
    for each vertex (x, z):
        var paint = GetPaintAtPosition(worldX, worldZ);
        if (paint != null)
            color = Heightmap.GetBiomeColor(paint.Biome);
        else
            color = Heightmap.GetBiomeColor(worldBiome);
    
    renderMesh.SetColors(tempColors);
}
```

**New: Force heightmap regeneration after paint**
```csharp
private static void ForceHeightmapRegeneration(Vector3 center, float radius)
{
    foreach (heightmap in nearby heightmaps)
        heightmap.Regenerate();  // Triggers our RebuildRenderMesh patch
}
```

---

## Testing Checklist (V3)

### Immediate Paint Test
- [ ] Paint snow overlay - should appear immediately and STAY
- [ ] Paint lava overlay - should appear immediately and STAY
- [ ] Walk around painted area - paint should persist as you move

### Persistence Test
- [ ] Paint biome overlay
- [ ] Walk far away (zone unloads)
- [ ] Return to area - paint should still be there
- [ ] Logout and login - paint should still be there
- [ ] Restart server - paint should still be there

### Clutter Test
- [ ] Paint clutter (grass)
- [ ] Logout and login - clutter should respawn
- [ ] Clear clutter - should be removed from ZDO

### Debug Logging
Check logs for:
```
[BiomePaintPersistence] Recorded biome paint: Lava at (x,y,z), radius 5
[BiomePaintPersistence] Loaded X biome paints for TerrainComp at (x,y,z)
```

---

## Architecture Summary (V3)

### Data Storage
- **ZDO Keys:** `va_biome_overlays`, `va_clutter_paints`
- **Location:** TerrainComp's ZDO (one per 64m zone)
- **Format:** Compressed ZPackage (same as vanilla terrain data)

### Runtime Cache
- `_paintCacheByPosition` - Dictionary keyed by zone hash
- `_hasAnyPaintedAreas` - Fast boolean check for patch skip

### Harmony Patches
| Patch | Purpose |
|-------|---------|
| `TerrainComp.Load` (Postfix) | Load paint data from ZDO into cache |
| `TerrainComp.OnDestroy` (Postfix) | Clean up cache |
| `Heightmap.RebuildRenderMesh` (Postfix) | Apply paint colors during mesh rebuild |
| `ZNetScene.OnDestroy` (Prefix) | Clean up on world unload |

---

## Why This Should Work

1. **Paint is applied during mesh generation, not after**
   - Vanilla can't overwrite our colors because we apply them last
   
2. **Data persists in TerrainComp ZDO**
   - ZDO data is saved with the world automatically
   - Syncs in multiplayer automatically
   
3. **Cache provides fast lookup**
   - RebuildRenderMesh is called frequently
   - Need O(1) lookup, not O(n) database queries
   
4. **Same pattern as working EnvironmentBoxPatches**
   - Env boxes use exact same approach for biome overrides
   - They work perfectly, so this should too

---

## Console Commands

```
paint_biome <type> [radius]  - Paint biome overlay at cursor
paint_snow [radius]          - Paint snow overlay
paint_lava [radius]          - Paint lava/ash overlay
reset_biome_overlay [radius] - Reset biome overlay

paint_clutter [type] [radius] [density] - Paint clutter
clear_clutter [radius]                   - Clear clutter
```

---

## Files Modified

| File | Changes |
|------|---------|
| `BiomePaintPersistence.cs` | Added RebuildRenderMesh patch, paint cache, ForceHeightmapRegeneration |
| `BiomeOverlayPainter.cs` | Calls RecordBiomePaint for persistence |
| `ClutterPaintTool.cs` | Calls RecordClutterPaint for persistence |
