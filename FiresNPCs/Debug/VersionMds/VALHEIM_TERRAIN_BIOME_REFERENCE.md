# Valheim Terrain Biome Visual System Reference

This document contains decompiled/documented information about how Valheim handles terrain biome visuals, snow overlays, lava effects, and related systems. Use this to implement proper biome visual overrides in Environment Boxes.

## Table of Contents
1. [Heightmap Biome Colors](#heightmap-biome-colors)
2. [Terrain Shader Properties](#terrain-shader-properties)
3. [Snow/Lava Overlay System](#snowlava-overlay-system)
4. [Paint Mask System](#paint-mask-system)
5. [Key Classes to Patch](#key-classes-to-patch)
6. [Implementation Strategy](#implementation-strategy)

---

## Heightmap Biome Colors

> ?? **CORRECTED 2025-07-15** — Previous version of this section had WRONG values for 6 out of 9 biomes.
> Values below are from decompiled `assembly_valheim.dll` `Heightmap.GetBiomeColor()`.

The terrain shader uses **vertex colors** to blend between biome textures. Each biome has a specific color that the shader interprets:

```csharp
// From DECOMPILED Heightmap.GetBiomeColor(Heightmap.Biome biome)
// These are Color32 values (0-255 per channel)
// Verified against assembly_valheim.dll — DO NOT modify these values!

public static Color32 GetBiomeColor(Biome biome)
{
    switch (biome)
    {
        case Biome.Swamp:       return new Color32(255, 0, 0, 0);      // Red channel
        case Biome.Mountain:    return new Color32(0, 255, 0, 0);      // Green channel (SNOW!)
        case Biome.BlackForest: return new Color32(0, 0, 255, 0);      // Blue channel
        case Biome.Plains:      return new Color32(0, 0, 0, 255);      // Alpha channel
        case Biome.AshLands:    return new Color32(255, 0, 0, 255);    // Red+Alpha (LAVA!)
        case Biome.DeepNorth:   return new Color32(0, 255, 0, 0);      // Green (same as Mountain)
        case Biome.Mistlands:   return new Color32(0, 0, 255, 255);    // Blue+Alpha
        default:                return new Color32(0, 0, 0, 0);        // Meadows, Ocean, None
    }
}
```

### Vertex Color Channel Usage
- **R** = Swamp, AshLands
- **G** = Mountain, DeepNorth
- **B** = BlackForest, Mistlands
- **A** = Plains, AshLands, Mistlands

### Key Facts
- **Meadows falls through to `default`** — it's `(0,0,0,0)`, NOT `(255,0,0,0)`!
- **DeepNorth is identical to Mountain** — `(0,255,0,0)`, NOT `(0,255,0,255)`
- **Vanilla passes these values straight through** to `mesh.SetColors()` — NO alpha modification
- The instance method `GetBiomeColor(float ix, float iy)` bilinearly interpolates between
  the 4 corner biome colors for smooth transitions at biome edges

---

## Terrain Shader Properties

### Global Shader Properties (Set via `Shader.SetGlobalFloat`)

```csharp
// Snow-related globals
"_GlobalSnowAmount"  // 0-1, controls global snow coverage
"_SnowAmount"        // 0-1, similar to above (some shaders use this)
"_AddSnow"           // 0-1, forces snow on pieces regardless of height

// These are typically set based on world Y position in vanilla:
// Snow appears above ~100m altitude in Mountain biome
// Our goal: bypass this altitude check

// Lava-related (Ashlands)
"_LavaAmount"        // Controls lava glow intensity
"_AshlandsGlow"      // Additional ashlands effect
```

### Per-Material Properties (Heightmap Material)

```csharp
// Paint mask texture - controls cultivated/paved/dirt appearance
"_ClearedMaskTex"    // Texture2D, RGBA paint mask

// Biome textures (set by Heightmap during generation)
"_DiffuseTex0"       // Meadows grass texture
"_DiffuseTex1"       // Mountain/snow texture  
"_DiffuseTex2"       // Swamp texture
"_DiffuseTex3"       // etc.

// The shader blends these based on vertex colors
```

---

## Snow/Lava Overlay System

### How Snow Works in Vanilla

1. **Heightmap.Regenerate()** calculates corner biomes from `WorldGenerator.GetBiome()`
2. **Heightmap.RebuildRenderMesh()** sets vertex colors based on biome at each vertex
3. Terrain shader reads vertex colors and blends appropriate textures
4. Global `_GlobalSnowAmount` shader property controls snow intensity
5. Piece shaders use `_AddSnow` which is calculated from world Y position

### How to Force Snow (Our Approach)

```csharp
// 1. Set global shader properties
Shader.SetGlobalFloat("_GlobalSnowAmount", 1f);
Shader.SetGlobalFloat("_SnowAmount", 1f);
Shader.SetGlobalFloat("_AddSnow", 1f); // Forces snow on pieces

// 2. Override vertex colors in Heightmap.RebuildRenderMesh postfix
// Set vertices inside our zone to Mountain biome color
tempColors.Add(Heightmap.GetBiomeColor(Heightmap.Biome.Mountain));

// 3. Override corner biomes in Heightmap.Regenerate postfix
// This ensures the shader has snow textures available
cornerBiomes[0] = Heightmap.Biome.Mountain;
```

### How Lava/Ashlands Works

1. Ashlands uses `Biome.AshLands` color (Red+Alpha = 255,0,0,255)
2. Additional shader properties control lava glow effects
3. The terrain has a special "char" texture that shows through

```csharp
// To force Ashlands visuals:
// 1. Set vertex colors to Ashlands biome
tempColors.Add(Heightmap.GetBiomeColor(Heightmap.Biome.AshLands));

// 2. Set corner biomes to include Ashlands
cornerBiomes[0] = Heightmap.Biome.AshLands;

// 3. Optionally set lava shader globals
Shader.SetGlobalFloat("_LavaAmount", 1f);
```

---

## Paint Mask System

### TerrainComp.PaintCleared

This is how vanilla applies terrain paint (cultivate/pave/dirt):

```csharp
// From TerrainComp.PaintCleared(Vector3 worldPos, float radius, PaintType paintType, ...)

// Paint mask colors (from decompiled Heightmap static fields):
public static Color m_paintMaskDirt = new Color(1f, 0f, 0f, 1f);              // Red with alpha=1
public static Color m_paintMaskCultivated = new Color(0f, 1f, 0f, 1f);        // Green with alpha=1
public static Color m_paintMaskPaved = new Color(0f, 0f, 1f, 1f);             // Blue with alpha=1
public static Color m_paintMaskNothing = new Color(0f, 0f, 0f, 1f);           // Black with alpha=1
public static Color m_paintMaskClearVegetation = new Color(0f, 0f, 0f, 0f);   // All zeros (alpha=0 clears vegetation mask)

// The paint is stored in m_paintMask Color[] array
// Applied via Heightmap.ApplyToHeightmap() which sets _ClearedMaskTex
```

### How Paint Affects Grass

```csharp
// ClutterSystem checks paint mask before spawning grass:
// In ClutterSystem.IsBlocked(Vector3 worldPos):

Color paintMask = heightmap.GetPaintMask(x, y);
// If paintMask is NOT black, grass is blocked

// Our implementation should mirror this:
bool ShouldSpawnGrass(Vector3 pos)
{
    Color paint = GetPaintAt(pos);
    return paint.r < 0.5f && paint.g < 0.5f && paint.b < 0.5f; // Black = grass OK
}
```

---

## Key Classes to Patch

### 1. Heightmap.GetBiome (Position-based biome query)
```csharp
[HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiome))]
[HarmonyPostfix]
static void GetBiome_Postfix(Vector3 point, ref Heightmap.Biome __result)
{
    var envBox = EnvironmentBoxController.GetEnvBoxAtPosition(point);
    if (envBox != null && envBox.ForcedBiome != Heightmap.Biome.None)
        __result = envBox.ForcedBiome;
}
```

### 2. Heightmap.Regenerate (Corner biomes for shader)
```csharp
[HarmonyPatch(typeof(Heightmap), nameof(Heightmap.Regenerate))]
[HarmonyPostfix]
static void Regenerate_Postfix(Heightmap __instance)
{
    // Get m_cornerBiomes via reflection
    // Set corners inside env box to forced biome
    // Call RebuildRenderMesh() to apply
}
```

### 3. Heightmap.RebuildRenderMesh (Per-vertex biome colors)
```csharp
[HarmonyPatch(typeof(Heightmap), "RebuildRenderMesh")]
[HarmonyPostfix]
static void RebuildRenderMesh_Postfix(Heightmap __instance)
{
    // Get m_renderMesh and s_tempColors via reflection
    // For each vertex inside env box:
    //   tempColors[i] = Heightmap.GetBiomeColor(forcedBiome);
    // Apply: renderMesh.SetColors(tempColors);
}
```

### 4. WorldGenerator.GetBiome (World generation biome)
```csharp
// CAUTION: This affects terrain HEIGHT generation, not just visuals!
// Only patch if you want actual biome terrain shapes
[HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome))]
[HarmonyPostfix]
static void WorldGenerator_GetBiome_Postfix(float wx, float wy, ref Heightmap.Biome __result)
{
    // Use sparingly - can cause terrain to regenerate with wrong heights
}
```

### 5. EnvMan.GetBiome (Environment/weather biome)
```csharp
[HarmonyPatch(typeof(EnvMan), "GetBiome")]
[HarmonyPostfix]
static void EnvMan_GetBiome_Postfix(ref Heightmap.Biome __result)
{
    var envBox = EnvironmentBoxController.GetPlayerEnvBox();
    if (envBox != null && envBox.ForcedBiome != Heightmap.Biome.None)
        __result = envBox.ForcedBiome;
}
```

---

## Implementation Strategy

### For Snow Overlay (Mountain/DeepNorth)

```csharp
public void ApplySnowOverlay()
{
    // 1. Set global snow shaders
    Shader.SetGlobalFloat("_GlobalSnowAmount", 1f);
    Shader.SetGlobalFloat("_SnowAmount", 1f);
    Shader.SetGlobalFloat("_AddSnow", 1f); // Pieces get snow too
    
    // 2. Mark biome override active (for patches)
    EnvironmentBoxPatches.SetHasBiomeOverrideBoxes(true);
    
    // 3. Refresh heightmaps to trigger our RebuildRenderMesh patch
    RefreshHeightmapsInBounds(GetWorldBounds());
}
```

### For Lava/Ashlands Overlay

```csharp
public void ApplyAshlandsOverlay()
{
    // 1. Set Ashlands-specific shader properties (if any exist)
    // Note: Most Ashlands visuals come from vertex colors, not global props
    
    // 2. Mark biome override active
    EnvironmentBoxPatches.SetHasBiomeOverrideBoxes(true);
    
    // 3. Refresh heightmaps - our patches will set Ashlands vertex colors
    RefreshHeightmapsInBounds(GetWorldBounds());
    
    // 4. Spawn Ashlands clutter (already working via ClutterSpawner)
}
```

### Reflection Fields Needed

```csharp
// Cache these once at startup:
private static FieldInfo _cornerBiomesField;      // Heightmap.m_cornerBiomes (Biome[4])
private static FieldInfo _renderMeshField;        // Heightmap.m_renderMesh (Mesh)
private static FieldInfo _tempColorsField;        // Heightmap.s_tempColors (static List<Color32>)
private static FieldInfo _isDistantLodField;      // Heightmap.m_isDistantLod (bool)
private static MethodInfo _rebuildRenderMeshMethod; // Heightmap.RebuildRenderMesh()

const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

_cornerBiomesField = typeof(Heightmap).GetField("m_cornerBiomes", instanceFlags);
_renderMeshField = typeof(Heightmap).GetField("m_renderMesh", instanceFlags);
_tempColorsField = typeof(Heightmap).GetField("s_tempColors", staticFlags);
_isDistantLodField = typeof(Heightmap).GetField("m_isDistantLod", instanceFlags);
_rebuildRenderMeshMethod = typeof(Heightmap).GetMethod("RebuildRenderMesh", instanceFlags);
```

---

## Current Implementation Status

### ? Working (Verified in EnvironmentBoxPatches.cs)
- Grass/clutter spawning for biome override ?
- Weather/environment override ?
- Footstep sounds ?
- Corner biome override (PREFIX patch) ?
- Per-vertex biome colors (POSTFIX patch) ?
- Paint mask integration with clutter ?

### ? What's Missing - Building Piece Snow/Lava

The terrain patches ARE working correctly. The issue is that **building pieces** (walls, floors, roofs) need separate handling:

1. **Global shader properties** for snow aren't being set:
   - `_GlobalSnowAmount` = 1f
   - `_SnowAmount` = 1f
   - `_AddSnow` = 1f (bypasses altitude check for pieces)

2. **TerritorySnowManager** approach for per-piece tinting isn't being used

### The Fix - Add to EnvironmentBoxController.ApplyBiomeShaders()

Your `ApplyBiomeShaders()` already sets snow shaders for Mountain/DeepNorth, but it may need:

```csharp
private void ApplyBiomeShaders()
{
    if (ForcedBiome == Heightmap.Biome.None) return;
    
    // Check if this biome requires snow shader
    if (BiomeRequiresSnowShader(ForcedBiome))
    {
        // Store original snow amount if not already stored
        if (!m_origSnowAmount.HasValue)
        {
            try { m_origSnowAmount = Shader.GetGlobalFloat("_GlobalSnowAmount"); }
            catch { m_origSnowAmount = 0f; }
        }
        
        // Apply snow shader globals - CRITICAL for building pieces
        Shader.SetGlobalFloat("_GlobalSnowAmount", 1f);
        Shader.SetGlobalFloat("_SnowAmount", 1f);
        Shader.SetGlobalFloat("_AddSnow", 1f); // Forces snow on pieces regardless of Y
        
        m_snowShaderActive = true;
    }
    
    // Check if this biome requires lava/ashlands shader
    if (ForcedBiome == Heightmap.Biome.AshLands)
    {
        // Ashlands-specific shader properties (if any)
        // Most Ashlands visuals come from vertex colors, but check for:
        // Shader.SetGlobalFloat("_LavaAmount", 1f);
        // Shader.SetGlobalFloat("_AshlandsGlow", 1f);
    }
}
```

### For Building Piece Snow Tinting

Use `TerritorySnowManager` pattern (already exists in your codebase):

```csharp
// In OnPlayerEnter(), after ApplyBiomeShaders():
if (BiomeRequiresSnowShader(ForcedBiome))
{
    // Apply snow tinting to building pieces inside the env box
    Bounds bounds = GetWorldBounds();
    Vector3 min = bounds.min;
    Vector3 max = bounds.max;
    TerritorySnowManager.ApplySnowToRectangle(GetInstanceID(), min, max, 0.5f);
}

// In OnPlayerExit():
TerritorySnowManager.RemoveSnowFromTerritory(GetInstanceID());
```

---

## Related Files in Our Codebase

- `TerritoryRuntime.Environment.cs` - Has working snow mask implementation
- `TerritorySnowManager.cs` - Handles snow on building pieces
- `MaterialVariantSystem.cs` - Creates snow-tinted material variants
- `EnvironmentBoxPatches.cs` - Our Harmony patches (terrain working!)
- `EnvironmentBoxClutterSpawner.cs` - Grass/clutter spawning (working!)
- `EnvironmentBoxController.cs` - Main controller with `ApplyBiomeShaders()`

---

## Debugging Terrain Visual Issues

### Check 1: Are corner biomes being set?
Add logging in `Heightmap_RebuildRenderMesh_Prefix`:
```csharp
Debug.Log($"[EnvBox] Setting corner biomes to {forcedBiome} for heightmap at {hmPos}");
```

### Check 2: Are vertex colors being applied?
Add logging in `Heightmap_RebuildRenderMesh_Postfix`:
```csharp
Debug.Log($"[EnvBox] Applied {tempColors.Count} vertex colors, {anyChanged} vertices changed");
```

### Check 3: Are shader globals being set?
Add logging in `ApplyBiomeShaders()`:
```csharp
Debug.Log($"[EnvBox] Set _GlobalSnowAmount={Shader.GetGlobalFloat("_GlobalSnowAmount")}");
Debug.Log($"[EnvBox] Set _AddSnow={Shader.GetGlobalFloat("_AddSnow")}");
```

### Check 4: Is the heightmap actually regenerating?
The `RefreshHeightmapsInBounds()` call in `OnPlayerEnter()` should trigger regeneration:
```csharp
Debug.Log($"[EnvBox] Regenerating heightmap: {hm.transform.position}");
```

### Common Issues

1. **Patches not running**: Check that `HasAnyBiomeOverrideBoxes()` returns true
2. **Wrong vertex count**: `tempColors.Count` should equal `(width+1) * (width+1)`
3. **Mesh not updated**: Ensure `renderMesh.SetColors(tempColors)` is called
4. **Corner biomes reverted**: The vanilla `RebuildRenderMesh` may reset them after our PREFIX

---

## Ashlands Lava - What We Know

Ashlands terrain uses:
1. **Biome color** `(255, 0, 0, 255)` - Red + Alpha channels
2. **Terrain textures** selected based on vertex colors
3. **Lava pools** are separate objects (not terrain shader)
4. **Char/ash texture** blended via the same vertex color system

To force Ashlands visuals:
1. Set vertex colors to `Heightmap.GetBiomeColor(Heightmap.Biome.AshLands)`
2. Set corner biomes to include `AshLands`
3. The terrain shader will automatically blend in char/ash textures

### Known Shader Properties for Ashlands
```csharp
// These may or may not exist - need to verify:
"_LavaAmount"      // May control lava glow intensity
"_AshAmount"       // May control ash overlay
"_CharAmount"      // May control char/burn texture
```

The actual lava pools are separate prefabs (`LavaPool`, etc.) and are NOT controlled by the terrain shader.
