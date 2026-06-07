# WaterVolume Class Reference

## Overview
The `WaterVolume` class in Valheim handles water surface calculation, wave simulation, and interaction with objects in water. This reference documents the key methods and properties for use in custom water/clutter projects.

---

## Key Static Properties

### Global Wind Data
```csharp
private static Vector4 s_globalWind1 = new Vector4(1f, 0.0f, 0.0f, 0.0f);
private static Vector4 s_globalWind2 = new Vector4(1f, 0.0f, 0.0f, 0.0f);
private static float s_globalWindAlpha = 0.0f;
```
- Wind vectors control wave direction
- `s_globalWindAlpha` blends between two wind states

### Water Time
```csharp
private static float s_waterTime = 0.0f;
private static float s_wrappedDayTimeSeconds = 0.0f;
```
- Synced across network via `ZNet.instance.GetWrappedDayTimeSeconds()`
- Used for wave animation timing

### Static Instances List
```csharp
public static List<WaterVolume> Instances { get; } = new List<WaterVolume>();
```
- All active WaterVolume instances registered here
- Use to find nearby water volumes

---

## Key Instance Properties

```csharp
public MeshRenderer m_waterSurface;     // The water surface mesh renderer
public Heightmap m_heightmap;            // Associated heightmap for depth
public float m_forceDepth = -1f;         // Force specific depth (-1 = use heightmap)
public float m_surfaceOffset;            // Vertical offset for water surface
public bool m_useGlobalWind = true;      // Use global wind for waves
```

---

## Critical Methods

### GetWaterSurface - THE KEY METHOD FOR WATER HEIGHT
```csharp
public float GetWaterSurface(Vector3 point, float waveFactor = 1f)
{
    float num = 0.0f;
    if (this.m_useGlobalWind)
    {
        float wrappedDayTimeSeconds = WaterVolume.s_wrappedDayTimeSeconds;
        float depth = this.Depth(point);
        num = (double) depth == 0.0 ? 0.0f : this.CalcWave(point, depth, wrappedDayTimeSeconds, waveFactor);
    }
    float waterSurface = this.transform.position.y + num + this.m_surfaceOffset;
    
    // Edge of world drops water level
    if ((double) this.m_forceDepth < 0.0 && (double) Utils.LengthXZ(point) > 10500.0)
        waterSurface -= 100f;
    
    return waterSurface;
}
```

**Usage for Water Lilies:**
```csharp
// Get water surface height at a position
float waterY = waterVolume.GetWaterSurface(position, waveFactor: 0.5f);
// Use lower waveFactor for calmer water (lilies should bob gently)
```

### Depth - Get Normalized Water Depth
```csharp
public float Depth(Vector3 point)
{
    if ((double) this.m_oneDepth > -1.0)
        return this.m_oneDepth;
    
    Vector3 vector3 = this.transform.InverseTransformPoint(point);
    Vector3 size = this.m_collider.bounds.size;
    float t1 = (vector3.x + size.x / 2f) / size.x;
    float t2 = (vector3.z + size.z / 2f) / size.z;
    
    return Mathf.Lerp(
        Mathf.Lerp(this.m_normalizedDepth[3], this.m_normalizedDepth[2], t1), 
        Mathf.Lerp(this.m_normalizedDepth[0], this.m_normalizedDepth[1], t1), 
        t2
    );
}
```
- Returns 0-1 normalized depth
- 0 = shore/shallow, 1 = deep water
- Depth affects wave amplitude

### StaticUpdate - Called Each Frame
```csharp
public static void StaticUpdate()
{
    WaterVolume.UpdateWaterTime(Time.deltaTime);
    if (!(bool) (Object) EnvMan.instance)
        return;
    EnvMan.instance.GetWindData(out s_globalWind1, out s_globalWind2, out s_globalWindAlpha);
}
```

---

## Wave Calculation

### CalcWave - Main Wave Function
```csharp
public float CalcWave(Vector3 worldPos, float depth, float waterTime, float waveFactor)
{
    return (double) s_globalWindAlpha == 0.0 
        ? this.CalcWave(worldPos, depth, s_globalWind1, waterTime, waveFactor) 
        : Mathf.LerpUnclamped(
            this.CalcWave(worldPos, depth, s_globalWind1, waterTime, waveFactor), 
            this.CalcWave(worldPos, depth, s_globalWind2, waterTime, waveFactor), 
            s_globalWindAlpha
        );
}
```

### Wave Parameters (from CreateWave)
- **waveSpeed**: 10f to 56f (faster = more rapid waves)
- **waveLength**: 0.04f to 1.5f (higher = longer wavelength)
- **waveHeight**: 0.2f to 8f (amplitude)
- **sharpness**: 0.5f to 0.9f (wave shape)

---

## How to Find Water at a Position

### Method 1: Use Static Instances List
```csharp
public static float GetWaterLevelAt(Vector3 position)
{
    foreach (var waterVolume in WaterVolume.Instances)
    {
        if (waterVolume == null) continue;
        
        // Check if position is within water volume bounds
        Collider collider = waterVolume.GetComponent<Collider>();
        if (collider != null && collider.bounds.Contains(position))
        {
            return waterVolume.GetWaterSurface(position, waveFactor: 1f);
        }
    }
    
    // Fallback: check global water level (sea level = 30)
    return ZoneSystem.instance?.m_waterLevel ?? 30f;
}
```

### Method 2: Use ZoneSystem Water Level
```csharp
// Global sea level constant
float seaLevel = ZoneSystem.instance.m_waterLevel; // Usually 30f

// For ocean water, this is the base level
// WaterVolume adds waves on top of this
```

### Method 3: Raycast for Water
```csharp
public static bool TryGetWaterSurface(Vector3 position, out float waterY)
{
    waterY = 0f;
    
    // Cast down from high above
    int waterMask = LayerMask.GetMask("Water");
    if (Physics.Raycast(position + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f, waterMask))
    {
        WaterVolume water = hit.collider.GetComponent<WaterVolume>();
        if (water != null)
        {
            waterY = water.GetWaterSurface(position);
            return true;
        }
    }
    
    return false;
}
```

---

## Water Lilies Implementation Strategy

### For Custom Clutter System
```csharp
public static float GetClutterHeightForWaterLilies(Vector3 position)
{
    // 1. Check if we're near water
    float seaLevel = ZoneSystem.instance?.m_waterLevel ?? 30f;
    
    // 2. If position is below sea level + margin, check for actual water
    if (position.y < seaLevel + 5f)
    {
        // Find water volume at this position
        foreach (var waterVolume in WaterVolume.Instances)
        {
            if (waterVolume == null) continue;
            
            Bounds bounds = waterVolume.GetComponent<Collider>()?.bounds ?? default;
            if (bounds.size == Vector3.zero) continue;
            
            // Expand bounds check vertically
            bounds.Expand(new Vector3(0, 100f, 0));
            
            if (bounds.Contains(position))
            {
                // Get water surface with reduced wave factor for calmer lilies
                return waterVolume.GetWaterSurface(position, waveFactor: 0.3f);
            }
        }
        
        // No water volume found, use sea level
        return seaLevel;
    }
    
    // Not in water area, return original position
    return position.y;
}
```

### Detecting If Position Is Over Water
```csharp
public static bool IsOverWater(Vector3 position)
{
    float groundHeight = ZoneSystem.instance?.GetGroundHeight(position) ?? 0f;
    float seaLevel = ZoneSystem.instance?.m_waterLevel ?? 30f;
    
    // If ground is below sea level, this position is over water
    return groundHeight < seaLevel;
}
```

---

## Shader Properties

```csharp
private static readonly int s_shaderWaterTime = Shader.PropertyToID("_WaterTime");
private static readonly int s_shaderDepth = Shader.PropertyToID("_depth");
private static readonly int s_shaderUseGlobalWind = Shader.PropertyToID("_UseGlobalWind");
```

---

## IWaterInteractable Interface

Objects that interact with water implement this:
```csharp
public interface IWaterInteractable
{
    void Increment(LiquidType type);
    int Decrement(LiquidType type);
    void SetLiquidLevel(float level, LiquidType type, Component source);
    Transform GetTransform();
}
```

---

## Important Constants

| Constant | Value | Description |
|----------|-------|-------------|
| Sea Level | ~30f | `ZoneSystem.instance.m_waterLevel` |
| World Edge | 10500f | Beyond this, water drops 100m |
| Wave Time Scale | 1/20 | `time = waterTime / 20f` |
| Max Wave Height | ~8f | Largest wave amplitude |

---

## Usage Examples

### Place Water Lily at Water Surface
```csharp
Vector3 lilyPosition = new Vector3(x, 0, z);

// Get water surface height
float waterY = GetWaterLevelAt(lilyPosition);

// Place lily slightly above water surface
lilyPosition.y = waterY + 0.05f; // Small offset to float on surface
```

### Animate Floating Object
```csharp
void Update()
{
    if (waterVolume != null)
    {
        float waterY = waterVolume.GetWaterSurface(transform.position, waveFactor: 0.5f);
        transform.position = new Vector3(
            transform.position.x,
            waterY + floatOffset,
            transform.position.z
        );
    }
}
```

---

## Notes for Custom Water Project

1. **Wave Factor**: Use 0.3-0.5 for calmer water (ponds, lily placement)
2. **Depth**: Affects wave amplitude - shallow water has smaller waves
3. **Global Wind**: Controls wave direction, updates from EnvMan
4. **Surface Offset**: Added to base water level for fine-tuning
5. **Instances List**: Best way to find all active water volumes

---

## Related Classes

- `Heightmap` - Provides ocean depth data
- `ZoneSystem` - Has `m_waterLevel` for global sea level
- `EnvMan` - Provides wind data for waves
- `IWaterInteractable` - Interface for objects in water
- `LiquidType` - Enum: Water, Tar, etc.

---

## Implementation Status

### ? Water Lily Water Surface Snapping (IMPLEMENTED)

The custom `ClutterPaintTool` now detects water lily clutter types and automatically snaps them to the water surface:

**File:** `FiresNPCs\Modules\CustomTerrain\Clutter\ClutterPaintTool.cs`

**Key Methods Added:**
- `IsWaterLilyClutter(string clutterName)` - Detects lily types by name
- `GetWaterSurfaceAt(Vector3 position)` - Uses WaterVolume for dynamic water height

**Behavior:**
- Water lilies **only spawn where water exists** (water level > ground level)
- Uses `WaterVolume.GetWaterSurface(position, waveFactor: 0.3f)` for gentle bobbing
- Falls back to `ZoneSystem.instance.m_waterLevel` if no WaterVolume found
- Reeds (`instanced_vass`) and other water plants still attach to ground (no water snapping)

**Detection Patterns:**
```csharp
// Names that trigger water surface snapping:
- "waterlil" (waterlilies)
- "water_lil"
- "lily"
- "lilies"
```
