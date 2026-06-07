# Valheim Shader Reference

Generated: 2026-03-03

## Overview

This document catalogs all shaders available in Valheim for mod development purposes.

---

## Water-Related Shaders

These are the key shaders for underwater bubble effects:

| Shader Name | Purpose | Notes |
|-------------|---------|-------|
| `Custom/Water` | Main water surface shader | Handles reflections, refractions, waves |
| `Custom/WaterBottom` | Underwater floor rendering | Used for seafloor appearance |
| `Custom/WaterMask` | Water masking/cutout | Can be used to "cut holes" in water |
| `Custom/Flow` | Flowing liquid effects | Rivers, streams |
| `Custom/FlowOpaque` | Opaque flowing surfaces | |

### Recommended for Bubble Dome

For the underwater bubble dome effect, consider:
- **`Custom/WaterMask`** - Could create the "air pocket" effect by masking water
- **`Custom/Water`** - For the bubble surface to look like an inverted water surface
- **`Sprites/Default`** or **`UI/Default`** - For simple transparent dome visualization

---

## All Available Shaders (123 total)

### Custom/Valheim Shaders

These are Valheim's proprietary shaders:

```
Custom/AlphaParticle
Custom/Blob
Custom/Bonemass
Custom/Clouds
Custom/Creature
Custom/Decal
Custom/Distortion
Custom/Flow
Custom/FlowOpaque
Custom/Gradient Mapped Particle (Unlit)
Custom/Grass
Custom/Heightmap
Custom/icon
Custom/LitGui
Custom/LitParticles
Custom/mapshader
Custom/Mesh Flipbook Particle
Custom/OverlayWithHole
Custom/Particle (Unlit)
Custom/ParticleDecal
Custom/Piece
Custom/Player
Custom/Rug
Custom/ShadowBlob
Custom/SkyboxProcedural
Custom/SkyObject
Custom/StaticRock
Custom/Tar
Custom/Trilinearmap
Custom/UI/AntiAliasedCircle
Custom/UI_BGBlur
Custom/Vegetation
Custom/Water
Custom/WaterBottom
Custom/WaterMask
Custom/Yggdrasil
Custom/Yggdrasil_root
```

### Standard/Built-in Shaders

```
Legacy Shaders/Decal
Legacy Shaders/Diffuse
Legacy Shaders/Particles/~Additive-Multiply
Legacy Shaders/Particles/Additive
Legacy Shaders/Particles/Additive (Soft)
Legacy Shaders/Particles/Alpha Blended
Legacy Shaders/Particles/Alpha Blended Premultiply
Legacy Shaders/Particles/Anim Alpha Blended
Legacy Shaders/Reflective/Bumped Specular
Legacy Shaders/Transparent/Diffuse
Standard
Standard (Double Sided)
Standard (Specular setup)
Standard Double Sided
Standard TwoSided
StandardDoubleSide
Standard-DoubleSided
Unlit/BeaconBeam
Unlit/Color
Unlit/Invis
Unlit/Lighting
Unlit/WeaponGlow
```

### UI/Sprites Shaders

```
Sprites/Default
Sprites/Diffuse
Sprites/Mask
UI/Default
UI/Default Font
UI/Lava Indicator
UI/Unlit/Detail
```

### Particles/Effects Shaders

```
Custom/AlphaParticle
Custom/Gradient Mapped Particle (Unlit)
Custom/LitParticles
Custom/Mesh Flipbook Particle
Custom/Particle (Unlit)
Custom/ParticleDecal
Legacy Shaders/Particles/~Additive-Multiply
Legacy Shaders/Particles/Additive
Legacy Shaders/Particles/Additive (Soft)
Legacy Shaders/Particles/Alpha Blended
Legacy Shaders/Particles/Alpha Blended Premultiply
Legacy Shaders/Particles/Anim Alpha Blended
Lux Lit Particles/ Bumped
Lux Lit Particles/ Tess Bumped
Particles/Standard Surface
Particles/Standard Surface2
Particles/Standard Unlit
Particles/Standard Unlit2
```

### Skybox Shaders

```
Custom/SkyboxProcedural
Skybox/Procedural
```

### Third-Party/Mod Shaders

```
ASE/ASE_Standart
ASE/ASE_StandartCutout2SidedSimple
Autodesk Interactive
Balrond/ Bumped
Balrond/Creature
Balrond/Piece
Balrond/Player
Balrond/ScrollingTex
Balrond/StaticRock
Balrond/Vegetation
BalrondDiffuseDoubleSide
DLNK Shaders/ASE/Nature/LeavesAnim
JVLmock_Custom/Piece
JVLmock_Custom/StaticRock
MK4/Lava Glow
```

### Hidden/Internal Shaders

```
GUI/Text Shader
Hidden/BlitCopy
Hidden/CameraHeatDistort
Hidden/CubeBlend
Hidden/CubeBlur
Hidden/CubeCopy
Hidden/Internal-CombineDepthNormals
Hidden/Internal-DeferredReflections
Hidden/InternalErrorShader
Hidden/Internal-Flare
Hidden/Internal-GUIRoundedRect
Hidden/Internal-GUIRoundedRectWithColorPerBorder
Hidden/Internal-GUITexture
Hidden/Internal-GUITextureClip
Hidden/Internal-MotionVectors
Hidden/Internal-ScreenSpaceShadows
Hidden/Internal-StencilWrite
Hidden/Post FX/Ambient Occlusion
Hidden/Post FX/Blit
Hidden/Post FX/Fog
Hidden/Post FX/Lut Generator
Hidden/Post FX/Temporal Anti-aliasing
Hidden/Post FX/Uber Shader
Hidden/RadialCutoutShader
Hidden/RadialSegementShader
Hidden/ShieldDomePass
Hidden/SimpleClear
Hidden/SunShaftsComposite
Hidden/TextCore/Distance Field SSD
TextMeshPro/Distance Field
TextMeshPro/Distance Field (Surface)
TextMeshPro/Mobile/Distance Field
TextMeshPro/Sprite
ToonDeferredShading2017
```

---

## Shader Support Status

Shaders marked as `[SUPPORTED]` can be accessed via `Shader.Find()`:
- GUI/Text Shader
- Hidden/BlitCopy
- Hidden/CameraHeatDistort
- Legacy Shaders/Diffuse
- Skybox/Procedural
- Sprites/Default
- Sprites/Mask
- TextMeshPro/Distance Field
- TextMeshPro/Mobile/Distance Field
- TextMeshPro/Sprite
- ToonDeferredShading2017
- UI/Default

Shaders marked as `[NOT SUPPORTED]` may need to be obtained from existing materials in the game.

---

## Recommended Usage

### For Transparent Overlays (EnvBox, Bubbles)
```csharp
Shader.Find("Sprites/Default")      // Best compatibility
Shader.Find("UI/Default")           // Alternative
Shader.Find("Legacy Shaders/Transparent/Diffuse")
```

### For Terrain Materials
```csharp
// Get from existing terrain
var terrainMat = Heightmap.instance?.m_material;
// Shader: Custom/Heightmap
```

### For Building Pieces
```csharp
// Get from existing piece
var pieceMat = existingPiece.GetComponent<MeshRenderer>().material;
// Shader: Custom/Piece or Custom/StaticRock
```

### For Water Effects
```csharp
// Get from existing water volume
var waterMat = WaterVolume.instance?.GetComponent<MeshRenderer>().material;
// Shader: Custom/Water
```

---

## Accessing Valheim Custom Shaders

Since many Custom/* shaders are marked `[NOT SUPPORTED]` for `Shader.Find()`, you need to extract them from existing game objects:

```csharp
public static class ShaderCache
{
    private static Dictionary<string, Shader> _cache = new Dictionary<string, Shader>();
    
    public static void Initialize()
    {
        // Cache shaders from existing materials
        foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (mat?.shader != null && !_cache.ContainsKey(mat.shader.name))
            {
                _cache[mat.shader.name] = mat.shader;
            }
        }
    }
    
    public static Shader GetShader(string name)
    {
        if (_cache.TryGetValue(name, out var shader))
            return shader;
        
        // Fallback to Shader.Find
        return Shader.Find(name);
    }
}
```

---

## Water Shader Properties (Custom/Water)

Based on observation, the Custom/Water shader likely uses these properties:

| Property | Type | Description |
|----------|------|-------------|
| `_Color` | Color | Base water tint |
| `_WaveHeight` | Float | Wave amplitude |
| `_WaveSpeed` | Float | Wave animation speed |
| `_ReflectionTex` | Texture | Environment reflection |
| `_RefractionTex` | Texture | Underwater refraction |
| `_NormalMap` | Texture | Water surface normals |
| `_Fresnel` | Float | Edge reflection intensity |
| `_Transparency` | Float | Water transparency |

---

## Notes

- Custom shaders are compiled into Valheim's asset bundles
- Some shaders may have platform-specific variants
- Shader properties can vary between Valheim versions
- Always test shader compatibility after Valheim updates
