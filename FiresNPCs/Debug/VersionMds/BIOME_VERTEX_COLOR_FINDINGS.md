# Biome Vertex Color Findings — Custom Terrain

**Date:** 2025-07-14 (updated 2025-07-15)  
**Status:** ? RESOLVED — use exact vanilla `GetBiomeColor()` values, no alpha modification  
**Files Affected:** `DirtFloorMeshCombiner.Paint.cs`, `DirtFloorMeshCombiner.MeshGen.cs`, `DirtFloorAsyncMeshBuilder.cs`

---

## The Problem

Our custom terrain uses the vanilla `Custom/Heightmap` shader by cloning the material from a nearby
`Heightmap` component. This shader uses **vertex colors** (`mesh.colors32`) to select which biome
textures to blend. We call `Heightmap.GetBiomeColor(biome)` to get the correct color per vertex.

Some biomes (AshLands, Mistlands) were rendering incorrectly on our custom terrain. The root cause
was that our old reference document `VALHEIM_TERRAIN_BIOME_REFERENCE.md` had **completely wrong
biome color values**, and we were also incorrectly modifying the alpha channel.

---

## Vanilla Biome Color Values (CORRECTED — from decompiled source)

?? **The old `VALHEIM_TERRAIN_BIOME_REFERENCE.md` had WRONG values!** See correction table below.

From decompiled `Heightmap.GetBiomeColor(Biome biome)` (switch statement):

| Biome | Color32 (R, G, B, A) | Old Doc Said (WRONG) | Notes |
|-------|----------------------|---------------------|-------|
| **Meadows** (default) | `(0, 0, 0, 0)` | `(255, 0, 0, 0)` ? | **All zeros!** Falls through to `default` case |
| **Swamp** | `(255, 0, 0, 0)` | `(0, 0, 255, 0)` ? | **R channel** (old doc had B) |
| **Mountain** | `(0, 255, 0, 0)` | `(0, 255, 0, 0)` ? | G channel = snow |
| **BlackForest** | `(0, 0, 255, 0)` | `(0, 0, 0, 255)` ? | **B channel** (old doc had A) |
| **Plains** | `(0, 0, 0, 255)` | `(255, 255, 0, 0)` ? | **A channel only** |
| **AshLands** | `(255, 0, 0, 255)` | `(255, 0, 0, 255)` ? | R+A = lava |
| **DeepNorth** | `(0, 255, 0, 0)` | `(0, 255, 0, 255)` ? | **Same as Mountain!** No alpha |
| **Mistlands** | `(0, 0, 255, 255)` | `(255, 255, 255, 0)` ? | **B+A** (not RGB white) |
| **Ocean** (default) | `(0, 0, 0, 0)` | n/a | Falls through to `default` case |

### Key Corrections from Old Doc
1. **Meadows is NOT red** — it's `(0,0,0,0)` (the default case in the switch)
2. **Swamp is red**, not blue
3. **BlackForest is blue**, not alpha-only
4. **Plains is alpha-only**, not red+green
5. **DeepNorth has NO alpha** — it's identical to Mountain `(0,255,0,0)`
6. **Mistlands is blue+alpha**, not RGB white

### Vertex Color Channel Mapping (shader biome blend weights)
- **R** = Swamp, AshLands
- **G** = Mountain, DeepNorth  
- **B** = BlackForest, Mistlands
- **A** = Plains, AshLands, Mistlands

---

## How Vanilla `RebuildRenderMesh()` Works

From decompiled source — the key section:

```csharp
private void RebuildRenderMesh()
{
    // ...
    for (int index1 = 0; index1 < num; ++index1)
    {
        float iy = DUtils.SmoothStep(0f, 1f, (float)index1 / (float)m_width);
        for (int index2 = 0; index2 < num; ++index2)
        {
            float ix = DUtils.SmoothStep(0f, 1f, (float)index2 / (float)m_width);
            s_tempUVs.Add(new Vector2((float)index2 / (float)m_width, (float)index1 / (float)m_width));
            
            if (m_isDistantLod)
            {
                // Distant LOD: sample biome directly from WorldGenerator
                Heightmap.Biome biome = instance.GetBiome(wx, wy);
                s_tempColors.Add(Heightmap.GetBiomeColor(biome));
            }
            else
            {
                // Normal: bilinear interpolate between 4 corner biome colors
                s_tempColors.Add((Color32)this.GetBiomeColor(ix, iy));
            }
        }
    }
    // ...
    m_renderMesh.SetColors(s_tempColors);  // ? DIRECT pass-through, NO alpha modification
}
```

**Critical findings:**
1. Vanilla passes `GetBiomeColor()` values **straight through** to the mesh — **NO alpha modification**
2. For non-distant-LOD, it uses the **instance method** `GetBiomeColor(float ix, float iy)` which does
   `Color32.Lerp` between the 4 corner biome colors — still no alpha modification
3. `SetColors(List<Color32>)` is used, not `SetColors(List<Color>)` — byte-range values

---

## The Fix (Current State)

**Pass `Heightmap.GetBiomeColor()` values through unmodified.** No alpha forcing, no height encoding.

### Code paths — all now use exact vanilla values:

| File | Method | What It Does |
|------|--------|-------------|
| `DirtFloorMeshCombiner.Paint.cs` | `GetBiomeColorForPosition()` | Returns `Heightmap.GetBiomeColor(biome)` directly |
| `DirtFloorMeshCombiner.Paint.cs` | `PaintBiomeOverlayVertexColors()` | Uses `Heightmap.GetBiomeColor(biome)` directly |
| `DirtFloorMeshCombiner.Paint.cs` | `ResetBiomeOverlayVertexColors()` | Uses `Heightmap.GetBiomeColor(biome)` directly |
| `DirtFloorMeshCombiner.Paint.cs` | `RefreshBiomeColors()` | Via `GetBiomeColorForPosition()` |
| `DirtFloorMeshCombiner.MeshGen.cs` | `BuildMesh()` | Via `GetBiomeColorForPosition()` |
| `DirtFloorMeshCombiner.MeshGen.cs` | `BuildMeshAsync()` | Via `GetBiomeColorForPosition()` |
| `DirtFloorMeshCombiner.MeshGen.cs` | `PrecomputeBiomeColorGrid()` | Via `GetBiomeColorForPosition()` |
| `DirtFloorMeshCombiner.MeshGen.cs` | `ApplyMeshJobResult()` | `mesh.colors32 = result.Colors` |
| `DirtFloorAsyncMeshBuilder.cs` | `ProcessMeshJob()` | Passes pre-computed grid values through, fallback = `(0,0,0,0)` |

---

## What NOT to Do (Lessons Learned)

1. **Do NOT force alpha=255** — this was our first fix attempt. It "worked" for AshLands (which
   already has alpha=255) but would break Meadows, Swamp, Mountain by giving them wrong blend weights.

2. **Do NOT use `Color` (float) instead of `Color32`** — `mesh.colors` and `mesh.colors32` are 
   different code paths. Using float `Color` with values like `(1, 0, 0, worldY)` broke everything
   because the shader expects byte-range vertex colors via the `COLOR` semantic.

3. **Do NOT encode world Y height in alpha** — this theory (from shader decompilation of
   `fragment_input_3.w`) was incorrect. The `fragment_input_3` likely maps to a different semantic
   than `COLOR`, or the shader handles it differently than assumed. Vanilla never puts height in alpha.

4. **Do NOT trust the old `VALHEIM_TERRAIN_BIOME_REFERENCE.md`** — it had 6 out of 9 biome colors
   completely wrong. Always verify against decompiled source.

---

## Vanilla Note: Instance `GetBiomeColor(float ix, float iy)` — Bilinear Blending

Vanilla doesn't just use `GetBiomeColor(biome)` for a single biome per vertex. It uses an
**instance method** that bilinearly interpolates between the 4 corner biome colors:

```csharp
private Color GetBiomeColor(float ix, float iy)
{
    if (all 4 corners same biome)
        return (Color)GetBiomeColor(m_cornerBiomes[0]);  // Fast path
    
    Color32 c0 = GetBiomeColor(m_cornerBiomes[0]);  // bottom-left
    Color32 c1 = GetBiomeColor(m_cornerBiomes[1]);  // bottom-right
    Color32 c2 = GetBiomeColor(m_cornerBiomes[2]);  // top-left
    Color32 c3 = GetBiomeColor(m_cornerBiomes[3]);  // top-right
    
    return Color32.Lerp(Color32.Lerp(c0, c1, ix), Color32.Lerp(c2, c3, ix), iy);
}
```

Where `ix` and `iy` are `DUtils.SmoothStep(0, 1, vertexPos / width)`.

**Our custom terrain doesn't do this blending** — we sample the biome per-vertex from
`WorldGenerator.GetBiome()` and use the raw color. This means we get hard biome edges instead
of smooth transitions at biome boundaries. This is acceptable for now since our terrain is
player-placed and usually within a single biome.

---

## Files Changed

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.Paint.cs` | `GetBiomeColorForPosition()` — removed all `.a = 255` forcing, returns `Heightmap.GetBiomeColor()` directly. Same for `PaintBiomeOverlayVertexColors()` and `ResetBiomeOverlayVertexColors()`. |
| `DirtFloorAsyncMeshBuilder.cs` | `ProcessMeshJob()` — removed `.a = 255` forcing, passes grid colors through unmodified. Fixed Meadows fallback from `(255,0,0,255)` to correct `(0,0,0,0)`. |
