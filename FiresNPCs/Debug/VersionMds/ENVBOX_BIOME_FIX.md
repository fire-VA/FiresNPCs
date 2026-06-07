# Environment Box Biome Override Fix

## Problem
Environment Boxes are not properly re-drawing biome clutter and terrain paint when forcing a biome override. The terrain shows the wrong textures and clutter/grass doesn't change.

## Status: ? FULLY WORKING (Iteration 5 - Terrain Visual Fix)

---

## ?? CRITICAL: DO NOT MODIFY THE 2D BOUNDS CHECK ??

The terrain visual override works because we use **2D (XZ) bounds checking** instead of 3D `ContainsPoint()`.

### Why This Is Critical

**The Problem We Had:**
- Heightmap transform position Y is always 0 (e.g., `heightmap at (-3712.00, 0.00, 2368.00)`)
- Env box center Y was at terrain height (e.g., Y=140)
- Using `ContainsPoint()` with `hmPos.y` (0) meant all vertices were at Y=0
- This was OUTSIDE the env box Y range (140 ± 2), so nothing matched!

**The Fix:**
- Changed to 2D (XZ) bounds checking that IGNORES the Y coordinate
- Terrain painting only cares about XZ position on the ground, not height
- The Y coordinate of heightmap vertices is irrelevant for biome texture selection

### Code That Must Stay As-Is

**In PREFIX patch:**
```csharp
// Get env box 2D bounds (ignore Y - heightmap is at Y=0 but terrain is at world height)
Bounds envBox2DBounds = playerEnvBox.GetWorldBounds();
float minX = envBox2DBounds.min.x;
float maxX = envBox2DBounds.max.x;
float minZ = envBox2DBounds.min.z;
float maxZ = envBox2DBounds.max.z;

// Check each corner using 2D (XZ) containment
bool c0Inside = c0x >= minX && c0x <= maxX && c0z >= minZ && c0z <= maxZ;
```

**In POSTFIX patch:**
```csharp
// Check if this vertex is inside the env box using 2D (XZ) bounds
bool insideBox = worldX >= minX && worldX <= maxX && 
                 worldZ >= minZ && worldZ <= maxZ;
```

**DO NOT replace these with `playerEnvBox.ContainsPoint()` - it will break terrain painting!**

---

## How the Biome Override Now Works

### Flow when player enters an Environment Box:

1. **`OnPlayerEnter()`** is called
2. **`SetHasBiomeOverrideBoxes(true)`** enables the Harmony patches
3. **`RefreshTerrainInBounds()`** calls `Heightmap.Regenerate()` on nearby heightmaps
4. **`Regenerate()`** triggers `RebuildRenderMesh()`
5. Our **PREFIX patch** sets corner biomes for texture selection
6. Our **POSTFIX patch** sets per-vertex colors for visual blending
7. **Custom clutter spawner** spawns biome-appropriate grass/vegetation

### The 2D Bounds Check (Critical Fix)

The terrain painting works because we use **2D (XZ) bounds checking**:

```
Heightmap position Y = 0 (always)
Env box position Y = 140 (terrain height)
```

Using `ContainsPoint()` with Y=0 would NEVER match the env box at Y=140!
The fix: Check only XZ coordinates, ignore Y entirely for terrain painting.

---

## Biome Overlay Painting Tools (NEW)

In addition to Environment Box biome overrides, we now have **standalone terrain painting tools**
that can paint biome overlays anywhere, independent of env boxes.

### Console Commands

```
paint_snow [radius]     - Paint snow overlay at cursor position
paint_lava [radius]     - Paint lava/ash overlay at cursor position  
paint_biome <type> [radius] - Paint any biome overlay
reset_biome_overlay [radius|all] - Reset to original biome
```

### Biome Types for paint_biome

- `snow` / `mountain` - Mountain snow texture
- `lava` / `ash` / `ashlands` - Ashlands char/lava texture
- `swamp` - Swamp ground texture
- `forest` / `blackforest` - Black Forest floor texture
- `plains` - Plains golden grass texture
- `mistlands` - Mistlands dark texture
- `meadows` - Meadows bright grass texture
- `reset` - Restore original world biome

### Infinity Hammer Integration

See `INFINITY_HAMMER_BIOME_TOOLS.yaml` for ready-to-use tool definitions.
Add these to your `infinity_hammer.yaml` hoe section.

---

## Clutter/Grass Painting Tools (NEW)

Paint any grass or vegetation type anywhere, with Alt+Scroll cycling through all options!

### Console Commands

```
paint_clutter [type] [radius] [density] - Paint clutter at cursor
list_clutter [filter] - List all available clutter types  
cycle_clutter [next|prev|<name>] - Cycle or set clutter type
clear_clutter [radius|all] - Clear painted clutter
clutter_cycle_mode [on|off] - Enable/disable Alt+Scroll cycling
```

### Alt+Scroll Cycling

When using the Paint Clutter tool (or after running `clutter_cycle_mode on`):
- Hold **Alt + Scroll Up** to cycle to next clutter type
- Hold **Alt + Scroll Down** to cycle to previous clutter type
- Current selection is shown on-screen

### Available Clutter Types

**Meadows:**
- `instanced_meadows_grass` - Standard meadows grass
- `instanced_meadows_grass_short` - Short grass for clearings
- `instanced_meadows_flowers` - Wildflowers

**Black Forest:**
- `instanced_forest_groundcover` - Green forest floor
- `instanced_forest_groundcover_brown` - Autumn-colored floor
- `instanced_ormbunke` - Large ferns

**Swamp:**
- `instanced_swamp_grass` - Dark swamp vegetation
- `instanced_swamp_grass_bloom` - Flowering swamp plants

**Plains:**
- `instanced_heathgrass` - Golden plains grass
- `instanced_heathflowers` - Yellow wildflowers

**Mistlands:**
- `instanced_mistlands_grass_short` - Dark purple grass

**Ashlands:**
- `instanced_ashlands_grass_short` - Short charred grass
- `instanced_ashlands_grass_long` - Tall charred grass

**Water Plants:**
- `instanced_vass` - Water reeds
- `instanced_waterlilies` - Floating lily pads

**Misc:**
- `instanced_shrub` - Small decorative bushes

### Infinity Hammer Tools

The YAML file includes:
- **Paint Clutter** - Main tool with Alt+Scroll cycling
- Individual preset tools for each grass type
- Clear tools to remove painted clutter

---

The ClutterSystem spawns clutter in two stages:

### Stage 1: Eligibility Check (GetPatchBiomes)
```csharp
Heightmap.Biome patchBiomes = GetPatchBiomes(center, halfSize);
// This returns a biome MASK (e.g., Meadows)

foreach (Clutter clutter in m_clutter)
{
    // FILTER: Only consider clutter whose biome mask overlaps with patch biomes
    if ((patchBiomes & clutter.m_biome) != Heightmap.Biome.None)
    {
        // This clutter type CAN spawn...
    }
}
```

**Problem**: If `GetPatchBiomes()` returns `Meadows`, then Mountain clutter (which has `m_biome = Mountain`) is **filtered out** before we even get to spawn it.

### Stage 2: Individual Placement (GetGroundInfo)
```csharp
if (GetGroundInfo(position, out point, out normal, out hmap, out biome))
{
    // Second check: does this specific point's biome match?
    if ((clutter.m_biome & biome) != Heightmap.Biome.None)
    {
        // SPAWN the clutter
    }
}
```

Our existing patches on `GetGroundInfo` and `Heightmap.GetBiome` handle Stage 2, but the clutter was already filtered out in Stage 1!

---

## Solution: Patch GetPatchBiomes

We added a new patch on `ClutterSystem.GetPatchBiomes()` that:
1. Checks if the patch area overlaps with an env box
2. If so, **adds** the forced biome to the result mask
3. This allows clutter definitions for that biome to pass the eligibility check

```csharp
[HarmonyPatch(typeof(ClutterSystem), "GetPatchBiomes")]
[HarmonyPostfix]
private static void ClutterSystem_GetPatchBiomes_Postfix(Vector3 center, float halfSize, ref Heightmap.Biome __result)
{
    // Check if patch overlaps with env box
    var envBox = EnvironmentBoxController.GetEnvBoxAtPosition(center);
    // ... also check corners ...
    
    if (envBox != null && envBox.ForcedBiome != Heightmap.Biome.None)
    {
        // Add forced biome to the mask
        __result |= envBox.ForcedBiome;
    }
}
```

---

## Analysis: How Territory Module Handles Biome Override

### Key Components in TerritoryRuntime.Environment.cs

1. **Cached Territory Data with Bounds**
   - Uses `ForceBiomeTerritoryData` struct with pre-computed AABB bounds
   - Fast spatial filtering before detailed checks
   - Separate tracking for different flags (ForceBiome, SnowMask, CustomPaint)

2. **Heightmap.GetBiome Patch** (Line ~550)
   - Guards against null instance and early world loading
   - Uses `_hasAnyForceBiomeTerritories` fast check
   - Returns forced biome from `GetTerritory(point)`

3. **WorldGenerator.GetBiome Patch** (Line ~600)
   - Uses cached `_cachedForceBiomeTerritories` array
   - Fast AABB bounds check before territory check
   - Modifies biome DURING world generation (affects terrain textures)

4. **Heightmap.Regenerate Patch** (Line ~635)
   - Modifies `m_cornerBiomes` field via reflection
   - Forces `RebuildRenderMesh` to apply new biome colors
   - Pre-computes corner positions for fast checking

5. **Heightmap.RebuildRenderMesh Patch** (Line ~750)
   - Applies per-vertex biome colors for visual override
   - Only runs for SnowMask territories (visual-only)
   - Uses `s_tempColors` static list for efficiency

6. **Key Reflection Fields**:
   - `m_cornerBiomes` - Array of 4 biomes for heightmap corners
   - `m_renderMesh` - The rendered terrain mesh
   - `s_tempColors` - Static list for vertex colors
   - `m_isDistantLod` - Skip distant LOD heightmaps

### Territory Module Flow:
1. Player enters territory with ForceBiome flag
2. `EnsureTerritoryCacheValid()` builds cached territory data
3. Patches intercept biome queries:
   - `WorldGenerator.GetBiome` ? Returns forced biome (affects generation)
   - `Heightmap.GetBiome` ? Returns forced biome (affects queries)
   - `Heightmap.Regenerate` ? Modifies corner biomes + rebuilds mesh
4. `RefreshClutterInBounds()` forces clutter system to regenerate

---

## Current Environment Box Implementation

### What's Working:
- `Heightmap.GetBiome` patch ?
- `Heightmap.FindBiomeClutter` patch ?  
- `ClutterSystem.GetGroundInfo` patch ?
- `EnvMan.GetBiome` patch ?
- `Heightmap.RebuildRenderMesh` patch ? (per-vertex colors)

### What's Missing:

1. **NO `Heightmap.Regenerate` Patch**
   - Territory module patches this to modify `m_cornerBiomes`
   - This is CRITICAL for terrain texture blending
   - Without it, terrain looks like original biome even with vertex colors

2. **Missing Corner Biomes Reflection**
   - Territory caches `_cornerBiomesField` for direct biome modification
   - Env boxes don't have this

3. **Missing `_rebuildRenderMeshMethod` Invocation**
   - After modifying corner biomes, need to call `RebuildRenderMesh`
   - Territory does this in `Heightmap_Regenerate_Postfix`

4. **Clutter Refresh Timing Issue**
   - `RefreshClutterInBounds()` may be called before patches are active
   - Need to ensure `_hasAnyBiomeOverrideBoxes` is set FIRST

---

## Planned Changes

### 1. Add Missing Reflection Fields (EnvironmentBoxPatches.cs)

**BEFORE:**
```csharp
private static bool _reflectionCached = false;
private static FieldInfo _renderMeshField;
private static FieldInfo _tempColorsField;
private static FieldInfo _isDistantLodField;
```

**AFTER:**
```csharp
private static bool _reflectionCached = false;
private static FieldInfo _renderMeshField;
private static FieldInfo _tempColorsField;
private static FieldInfo _isDistantLodField;
private static FieldInfo _cornerBiomesField;      // NEW
private static MethodInfo _rebuildRenderMeshMethod; // NEW
```

### 2. Update EnsureReflectionCached() (EnvironmentBoxPatches.cs)

**BEFORE:**
```csharp
private static void EnsureReflectionCached()
{
    if (_reflectionCached) return;
    _reflectionCached = true;
    
    try
    {
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        
        _renderMeshField = typeof(Heightmap).GetField("m_renderMesh", instanceFlags);
        _isDistantLodField = typeof(Heightmap).GetField("m_isDistantLod", instanceFlags);
        _tempColorsField = typeof(Heightmap).GetField("s_tempColors", staticFlags);
    }
    catch (Exception ex)
    {
        Debug.LogWarning($"[EnvironmentBoxPatches] Failed to cache reflection: {ex.Message}");
    }
}
```

**AFTER:**
```csharp
private static void EnsureReflectionCached()
{
    if (_reflectionCached) return;
    _reflectionCached = true;
    
    try
    {
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        
        _renderMeshField = typeof(Heightmap).GetField("m_renderMesh", instanceFlags);
        _isDistantLodField = typeof(Heightmap).GetField("m_isDistantLod", instanceFlags);
        _tempColorsField = typeof(Heightmap).GetField("s_tempColors", staticFlags);
        _cornerBiomesField = typeof(Heightmap).GetField("m_cornerBiomes", instanceFlags);      // NEW
        _rebuildRenderMeshMethod = typeof(Heightmap).GetMethod("RebuildRenderMesh", instanceFlags); // NEW
    }
    catch (Exception ex)
    {
        Debug.LogWarning($"[EnvironmentBoxPatches] Failed to cache reflection: {ex.Message}");
    }
}
```

### 3. Add Heightmap.Regenerate Patch (EnvironmentBoxPatches.cs)

**NEW CODE to add after EnvMan_GetBiome_Postfix:**
```csharp
#region Heightmap.Regenerate Patch - Corner Biome Override

/// <summary>
/// Patch Heightmap.Regenerate to force corner biomes for env boxes.
/// This is CRITICAL for terrain texture blending - without it, the terrain
/// shader won't show the correct biome textures even if vertex colors are set.
/// 
/// Based on TerritoryRuntime.Environment.cs approach.
/// </summary>
[HarmonyPatch(typeof(Heightmap), nameof(Heightmap.Regenerate))]
[HarmonyPostfix]
[HarmonyPriority(Priority.Last)]
private static void Heightmap_Regenerate_Postfix(Heightmap __instance)
{
    // Fast exits
    if (__instance == null) return;
    if (ZNet.instance == null) return;
    if (!HasAnyBiomeOverrideBoxes()) return;
    
    // Ensure reflection is cached
    EnsureReflectionCached();
    if (_cornerBiomesField == null) return;
    
    // Get player's env box
    var playerEnvBox = EnvironmentBoxController.GetPlayerEnvBox();
    if (playerEnvBox == null || playerEnvBox.ForcedBiome == Heightmap.Biome.None)
        return;
    
    // Skip distant LOD heightmaps
    if (_isDistantLodField != null)
    {
        try
        {
            bool isDistantLod = (bool)_isDistantLodField.GetValue(__instance);
            if (isDistantLod) return;
        }
        catch { return; }
    }
    
    // Get heightmap bounds
    var hmPos = __instance.transform.position;
    float scale = __instance.m_scale;
    int width = __instance.m_width;
    float halfSize = width * scale * 0.5f;
    
    // Quick bounds check
    Bounds hmBounds = new Bounds(hmPos, new Vector3(width * scale, 1000f, width * scale));
    Bounds envBoxBounds = playerEnvBox.GetWorldBounds();
    
    if (!hmBounds.Intersects(envBoxBounds))
        return;
    
    try
    {
        // Get the corner biomes array via reflection
        var cornerBiomes = _cornerBiomesField.GetValue(__instance) as Heightmap.Biome[];
        if (cornerBiomes == null || cornerBiomes.Length < 4) return;
        
        // Pre-compute corner positions
        float c0x = hmPos.x - halfSize, c0z = hmPos.z - halfSize;
        float c1x = hmPos.x + halfSize, c1z = hmPos.z - halfSize;
        float c2x = hmPos.x - halfSize, c2z = hmPos.z + halfSize;
        float c3x = hmPos.x + halfSize, c3z = hmPos.z + halfSize;
        
        bool needsRebuild = false;
        Heightmap.Biome forcedBiome = playerEnvBox.ForcedBiome;
        
        // Check each corner and update to the forced biome if inside the env box
        if (playerEnvBox.ContainsPoint(new Vector3(c0x, hmPos.y, c0z)) && cornerBiomes[0] != forcedBiome)
        {
            cornerBiomes[0] = forcedBiome;
            needsRebuild = true;
        }
        
        if (playerEnvBox.ContainsPoint(new Vector3(c1x, hmPos.y, c1z)) && cornerBiomes[1] != forcedBiome)
        {
            cornerBiomes[1] = forcedBiome;
            needsRebuild = true;
        }
        
        if (playerEnvBox.ContainsPoint(new Vector3(c2x, hmPos.y, c2z)) && cornerBiomes[2] != forcedBiome)
        {
            cornerBiomes[2] = forcedBiome;
            needsRebuild = true;
        }
        
        if (playerEnvBox.ContainsPoint(new Vector3(c3x, hmPos.y, c3z)) && cornerBiomes[3] != forcedBiome)
        {
            cornerBiomes[3] = forcedBiome;
            needsRebuild = true;
        }
        
        if (needsRebuild)
        {
            _cornerBiomesField.SetValue(__instance, cornerBiomes);
            if (_rebuildRenderMeshMethod != null)
            {
                try { _rebuildRenderMeshMethod.Invoke(__instance, null); }
                catch { }
            }
        }
    }
    catch (Exception ex)
    {
        Debug.LogWarning($"[EnvironmentBoxPatches] Error in Heightmap_Regenerate_Postfix: {ex.Message}");
    }
}

#endregion
```

### 4. Verify OnPlayerEnter() Order (EnvironmentBoxController.cs)

**CURRENT ORDER (correct):**
1. Set `_hasAnyBiomeOverrideBoxes = true` via `SetHasBiomeOverrideBoxes(true)`
2. Apply environment
3. Apply skybox
4. Apply wind
5. Apply time
6. Apply biome shaders
7. Refresh terrain
8. Refresh clutter

This order is correct - the flag is set BEFORE refreshing terrain/clutter.

---

## Testing Checklist

After implementing changes:

1. [ ] Enter an Environment Box with ForcedBiome set to Mountain
   - Terrain should show snowy texture
   - Grass should be mountain-style (or absent)
   
2. [ ] Enter an Environment Box with ForcedBiome set to Swamp
   - Terrain should show swampy texture
   - Vegetation should be swamp-style
   
3. [ ] Exit the Environment Box
   - Terrain should restore to original biome
   - Vegetation should restore to original
   
4. [ ] Check that minimap is NOT affected (no ocean bands)

5. [ ] Test with multiple overlapping env boxes

---

## All Current Patches

| Patch | Purpose | Status |
|-------|---------|--------|
| `Heightmap.GetBiome` | General biome queries | ? |
| `Heightmap.FindBiomeClutter` | Clutter system biome lookups | ? |
| `ClutterSystem.GetPatchBiomes` | Clutter eligibility filter | ? |
| `ClutterSystem.GetGroundInfo` | Individual clutter placement | ? |
| `EnvMan.GetBiome` | Weather/environment | ? |
| `Heightmap.RebuildRenderMesh` PREFIX | Corner biomes for texture selection | ? |
| `Heightmap.RebuildRenderMesh` POSTFIX | Per-vertex biome colors | ? |

---

## Custom Clutter Spawner (Iteration 4)

### The Problem with Vanilla ClutterSystem

Even with all our patches, vanilla `ClutterSystem.GenerateVegPatch()` has **altitude checks**:

```csharp
float num7 = point.y - this.m_waterLevel;  // altitude above water (30m)
if ((double) num7 >= (double) clutter.m_minAltitude && (double) num7 <= (double) clutter.m_maxAltitude)
{
    // Only spawn if altitude is within range
}
```

Mountain clutter has `m_minAltitude ~100m`, so it won't spawn at low elevations even with correct biome.

### Solution: EnvironmentBoxClutterSpawner

We created `EnvironmentBoxClutterSpawner.cs` which:
1. Uses the same clutter prefabs as vanilla (from `ClutterSystem.m_clutter`)
2. Uses the same `InstanceRenderer` approach for instanced grass
3. **Skips altitude checks** - spawns biome clutter regardless of world height
4. Respects other constraints (tilt, water depth, scale)

### Biome Clutter Mappings

| Biome | Clutter Types |
|-------|---------------|
| Meadows | `instanced_meadows_grass`, `instanced_meadows_grass_short`, `instanced_meadows_flowers` |
| BlackForest | `instanced_forest_groundcover`, `instanced_forest_groundcover_brown`, `instanced_ormbunke` |
| Swamp | `instanced_swamp_grass`, `instanced_swamp_grass_bloom` |
| Mountain | `instanced_meadows_grass_short` (sparse) |
| Plains | `instanced_heathgrass`, `instanced_heathflowers` |
| Mistlands | `instanced_mistlands_grass_short` |
| AshLands | `instanced_ashlands_grass_short`, `instanced_ashlands_grass_long` |
| DeepNorth | `instanced_meadows_grass_short` (sparse) |
| Ocean | `instanced_vass`, `instanced_waterlilies` |

### Flow

1. Player enters env box with biome override
2. `EnvironmentBoxPatches.RefreshClutterInBounds()` clears vanilla clutter
3. `EnvironmentBoxClutterSpawner.SpawnClutterForBiome()` spawns correct biome clutter
4. Player exits ? `ClearAllClutter()` removes custom clutter
5. Vanilla clutter respawns naturally

---

## How Clutter Spawning Now Works

1. **Player enters env box**
   - `SetHasBiomeOverrideBoxes(true)` enables patches
   - `RefreshClutterInBounds()` clears vanilla clutter via `ResetGrass()`
   - `SpawnClutterForBiome()` spawns OUR clutter (bypasses altitude checks)
   
2. **Inside the box**
   - Custom clutter renders correctly
   - Terrain has correct biome texture (from RebuildRenderMesh patches)
   - Weather/environment matches biome
   
3. **Player exits env box**
   - `ClearAllClutter()` removes our custom clutter
   - `SetHasBiomeOverrideBoxes(false)` disables patches
   - Vanilla clutter respawns normally

---

## Testing Checklist

After implementing changes:

1. [ ] Enter an Environment Box with ForcedBiome set to Mountain
   - Terrain should show snowy/rocky texture
   - **Custom grass should spawn** (sparse meadows_grass_short)
   - Original meadows grass should be cleared
   
2. [ ] Enter an Environment Box with ForcedBiome set to Swamp
   - Terrain should show swampy texture
   - Swamp grass/blooms should spawn
   
3. [ ] Enter an Environment Box with ForcedBiome set to Plains
   - Terrain should show plains texture
   - Heath grass and flowers should spawn
   
4. [ ] Exit the Environment Box
   - Custom clutter disappears
   - Original biome clutter respawns
   - Terrain texture restored
   
5. [ ] Check that minimap is NOT affected (no ocean bands)

6. [ ] Terrain HEIGHT should remain unchanged

---

## Notes

- **Mountain/DeepNorth biomes have sparse grass** - this is intentional (snowy areas)
- **Custom clutter spawner bypasses altitude restrictions** - enables any biome at any height
- **Performance**: Custom spawner uses same InstanceRenderer as vanilla for efficiency

## Reference: Territory Module Key Methods

For reference, these are the key methods in TerritoryRuntime.Environment.cs:

- `EnsureReflectionCached()` - Lines 77-93
- `EnsureTerritoryCacheValid()` - Lines 150-240  
- `Heightmap_GetBiome_Postfix()` - Lines 550-585
- `WorldGenerator_GetBiome_Postfix()` - Lines 600-632
- `Heightmap_Regenerate_Postfix()` - Lines 635-720
- `GetForcedBiomeAtPoint()` - Lines 725-760
- `Heightmap_RebuildRenderMesh_Postfix()` - Lines 765-870
