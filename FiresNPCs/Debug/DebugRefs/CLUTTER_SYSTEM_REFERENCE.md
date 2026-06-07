# ClutterSystem Class Reference

## Overview
The `ClutterSystem` class in Valheim handles procedural vegetation/grass placement using GPU instancing. It generates clutter in patches around the player based on biome, altitude, terrain tilt, and other conditions.

---

## Key Static Instance

```csharp
public static ClutterSystem instance => ClutterSystem.m_instance;
```

---

## Configuration Properties

### Distance & Patch Size
```csharp
public float m_grassPatchSize = 8f;    // Size of each grass patch (8x8 meters)
public float m_distance = 40f;          // Maximum render distance for clutter
public float m_waterLevel = 27f;        // Reference water level for altitude checks
public float m_playerPushFade = 0.05f;  // How fast grass "pushes" back after player passes
public float m_amountScale = 1f;        // Global multiplier for clutter density
```

### Quality Levels
```csharp
public enum Quality
{
    Off,    // No clutter
    Low,    // 1/4 density
    Med,    // 1/2 density  
    High    // Full density
}
```

---

## Clutter Definition Class

```csharp
[Serializable]
public class Clutter
{
    public string m_name = "";
    public bool m_enabled = true;
    
    // Biome filter (bitmask)
    [BitMask(typeof (Heightmap.Biome))]
    public Heightmap.Biome m_biome;
    
    // GPU instancing (true for grass, false for larger objects)
    public bool m_instanced;
    public GameObject m_prefab;
    
    // Density
    public int m_amount = 80;  // Instances per patch
    
    // Terrain clearing filter
    public bool m_onUncleared = true;  // Spawn on natural terrain
    public bool m_onCleared;            // Spawn on cleared/cultivated terrain
    
    // Vegetation mask filter (from heightmap)
    public float m_minVegetation;
    public float m_maxVegetation;
    
    // Scale randomization
    public float m_scaleMin = 1f;
    public float m_scaleMax = 1f;
    
    // Terrain tilt filter (degrees)
    public float m_maxTilt = 18f;  // Max slope angle
    public float m_minTilt;         // Min slope angle
    
    // Altitude filter (relative to water level)
    public float m_maxAlt = 1000f;
    public float m_minAlt = 27f;
    
    // Water snapping - KEY FOR WATER LILIES!
    public bool m_snapToWater;      // Snap Y position to water surface
    public bool m_terrainTilt;      // Rotate to match terrain normal
    public float m_randomOffset;    // Random Y offset
    
    // Ocean depth filter
    public float m_minOceanDepth;
    public float m_maxOceanDepth;
    
    // Forest filter (0-1, where 1 = deep forest)
    public bool m_inForest;
    public float m_forestTresholdMin;
    public float m_forestTresholdMax = 1f;
    
    // Fractal noise placement
    public float m_fractalScale;
    public float m_fractalOffset;
    public float m_fractalTresholdMin = 0.5f;
    public float m_fractalTresholdMax = 1f;
}
```

---

## Key Methods

### GetVegPatch - Convert World Position to Patch ID
```csharp
public Vector2Int GetVegPatch(Vector3 point)
{
    return new Vector2Int(
        Mathf.FloorToInt((point.x + m_grassPatchSize / 2f) / m_grassPatchSize), 
        Mathf.FloorToInt((point.z + m_grassPatchSize / 2f) / m_grassPatchSize)
    );
}
```

### GetVegPatchCenter - Get World Position of Patch Center
```csharp
public Vector3 GetVegPatchCenter(Vector2Int p)
{
    return new Vector3(
        (float) p.x * m_grassPatchSize, 
        0.0f, 
        (float) p.y * m_grassPatchSize
    );
}
```

### GetGroundInfo - Raycast to Get Terrain Data
```csharp
public bool GetGroundInfo(
    Vector3 p,
    out Vector3 point,      // Hit position
    out Vector3 normal,     // Surface normal
    out Heightmap hmap,     // Heightmap component
    out Heightmap.Biome biome)  // Biome at position
{
    RaycastHit hitInfo;
    if (Physics.Raycast(p + Vector3.up * 500f, Vector3.down, out hitInfo, 1000f, m_placeRayMask))
    {
        point = hitInfo.point;
        normal = hitInfo.normal;
        hmap = hitInfo.collider.GetComponent<Heightmap>();
        biome = hmap.GetBiome(point);
        return true;
    }
    // fallback values...
    return false;
}
```

### ResetGrass - Force Rebuild in Area
```csharp
public void ResetGrass(Vector3 center, float radius)
{
    // Marks patches within radius for rebuild
    // Useful after terrain modifications
}
```

### ClearAll - Remove All Clutter
```csharp
public void ClearAll()
{
    // Destroys all patch objects and forces rebuild
}
```

---

## Water Lily Placement Logic (KEY!)

In `GenerateVegPatch`, the water snapping happens here:

```csharp
vector3_2 = point;  // Start at ground position

// THIS IS THE KEY FOR WATER LILIES!
if (clutter.m_snapToWater)
    vector3_2.y = this.m_waterLevel;  // Snap to water surface

if ((double) clutter.m_randomOffset != 0.0)
    vector3_2.y += UnityEngine.Random.Range(-clutter.m_randomOffset, clutter.m_randomOffset);
```

**Important**: Vanilla uses a CONSTANT `m_waterLevel` (27f), NOT dynamic water surface!

For proper water lilies that float on waves, you need to use `WaterVolume.GetWaterSurface()` instead.

---

## Patch Generation Flow

1. **LateUpdate** ? calls `UpdateGrass()`
2. **UpdateGrass** ? calls `GeneratePatches()` and `TimeoutPatches()`
3. **GeneratePatches** ? iterates outward from player position
4. **GeneratePatch** ? checks if patch needs generation
5. **GenerateVegPatch** ? creates actual clutter instances

### Patch Lifecycle
```csharp
private class PatchData
{
    public Vector3 center;              // World center of patch
    public List<GameObject> m_objects;  // Instanced renderers or objects
    public float m_timer;               // Time since last "touched"
    public bool m_reset;                // Flag to force rebuild
}
```

- Patches are pooled for reuse (`m_freePatches` stack)
- Patches timeout after 2 seconds if not "touched" (player moved away)
- `m_reset = true` forces rebuild on next frame

---

## Biome Filter

```csharp
Heightmap.Biome patchBiomes = GetPatchBiomes(vegPatchCenter, halfSize);
if (patchBiomes == Heightmap.Biome.None)
    return null;  // Skip if no valid biome

// Later, per-clutter check:
if ((clutter.m_biome & biome) != Heightmap.Biome.None)
{
    // This clutter can spawn in this biome
}
```

---

## Instanced vs Non-Instanced

### Instanced Clutter (Grass)
```csharp
if (clutter.m_instanced)
{
    // Create ONE InstanceRenderer per clutter type per patch
    instanceRenderer = gameObject.GetComponent<InstanceRenderer>();
    
    // Add instances to the renderer
    instanceRenderer.AddInstance(vector3_2, quaternion, scale);
}
```

### Non-Instanced Clutter (Larger Objects)
```csharp
else
{
    // Create individual GameObjects
    GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(
        clutter.m_prefab, vector3_2, quaternion, m_grassRoot.transform);
    vegPatch.m_objects.Add(gameObject);
}
```

---

## Quality Scaling

```csharp
int num3;
switch (this.m_quality)
{
    case Quality.Low:
        num3 = clutter.m_amount / 4;  // 25% density
        break;
    case Quality.Med:
        num3 = clutter.m_amount / 2;  // 50% density
        break;
    default:
        num3 = clutter.m_amount;       // 100% density
        break;
}
int num4 = (int) ((double) num3 * (double) this.m_amountScale);
```

---

## Player Push Effect

```csharp
// In LateUpdate:
m_oldPlayerPos = Vector3.Lerp(m_oldPlayerPos, localPlayer.transform.position, m_playerPushFade);
Shader.SetGlobalVector("_PlayerPosition", localPlayer.transform.position);
Shader.SetGlobalVector("_PlayerOldPosition", m_oldPlayerPos);
```

The grass shader uses these to "push" grass away from the player.

---

## Vanilla Water Lily Settings (for reference)

Based on typical Valheim clutter config for water lilies:
```csharp
// instanced_waterlilies
m_name = "waterlilies";
m_enabled = true;
m_biome = Heightmap.Biome.Meadows | Heightmap.Biome.Swamp;
m_instanced = true;
m_snapToWater = true;      // KEY! Snaps to water level
m_minAlt = -1000f;         // Can spawn below water level
m_maxAlt = 0f;             // But only in water (below surface)
m_minOceanDepth = 0f;      // Shallow water
m_maxOceanDepth = 2f;      // Not too deep
```

---

## Integration Points for Custom Clutter

### 1. Hook into ResetGrass
When custom terrain changes, call:
```csharp
ClutterSystem.instance?.ResetGrass(position, radius);
```

### 2. Access Clutter Definitions
```csharp
List<ClutterSystem.Clutter> clutters = ClutterSystem.instance.m_clutter;
ClutterSystem.Clutter lilyClutter = clutters.Find(c => c.m_name.Contains("waterlilies"));
```

### 3. Force Full Rebuild
```csharp
ClutterSystem.instance.ClearAll();  // Destroys everything
// Patches will regenerate on next LateUpdate
```

---

## Custom Clutter Height for Water Lilies

For your custom system, replace the simple `m_waterLevel` check with proper water surface:

```csharp
public static float GetClutterHeight(Vector3 position, ClutterSystem.Clutter clutter, float groundY)
{
    if (clutter.m_snapToWater && IsWaterLilyClutter(clutter.m_name))
    {
        // Use WaterVolume for dynamic water surface
        float waterY = GetWaterSurfaceAt(position);
        if (waterY > groundY)
        {
            return waterY + 0.02f; // Slight offset to float on surface
        }
    }
    else if (clutter.m_snapToWater)
    {
        // Other water-snapping clutter uses static water level
        return ClutterSystem.instance.m_waterLevel;
    }
    
    return groundY;
}

private static bool IsWaterLilyClutter(string name)
{
    return name.IndexOf("waterlil", StringComparison.OrdinalIgnoreCase) >= 0;
}

private static float GetWaterSurfaceAt(Vector3 position)
{
    foreach (var waterVolume in WaterVolume.Instances)
    {
        if (waterVolume == null) continue;
        
        Collider collider = waterVolume.GetComponent<Collider>();
        if (collider != null)
        {
            Bounds bounds = collider.bounds;
            bounds.Expand(new Vector3(0, 100f, 0));
            if (bounds.Contains(position))
            {
                // Use reduced wave factor for calmer lily placement
                return waterVolume.GetWaterSurface(position, waveFactor: 0.3f);
            }
        }
    }
    
    // Fallback to global sea level
    return ZoneSystem.instance?.m_waterLevel ?? 30f;
}
```

---

## Related Classes

- `InstanceRenderer` - GPU instancing for grass
- `Heightmap` - Terrain data, biome info
- `WaterVolume` - Dynamic water surface (see WATER_VOLUME_REFERENCE.md)
- `WorldGenerator` - Forest factor calculation
- `RenderGroupSystem` - Handles overworld vs dungeon rendering

---

## Notes for Custom Implementation

1. **Water Lilies**: Vanilla uses constant `m_waterLevel`, not dynamic waves. For floating lilies, use `WaterVolume.GetWaterSurface()`

2. **Reeds (vass)**: Should NOT snap to water - they grow from the ground up into the water

3. **Ocean Depth Check**: Uses `Heightmap.GetOceanDepth()` to determine how deep the water is at that point

4. **Patch Pooling**: Reuse `PatchData` objects to reduce GC pressure

5. **Quality Settings**: Honor the user's vegetation quality setting

6. **Cleared Terrain**: `hmap.IsCleared(point)` checks if player has modified terrain (cultivated, etc.)
