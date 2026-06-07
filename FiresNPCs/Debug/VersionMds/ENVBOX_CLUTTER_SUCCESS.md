# Environment Box Clutter System - Implementation Documentation

## Problem Statement
Environment boxes needed biome-appropriate grass/clutter that would render regardless of altitude restrictions (e.g., Mountain grass at sea level, Ashlands grass in Meadows).

## What Made It Work

### Key Discovery: InstanceRenderer Uses World Positions
The `InstanceRenderer.AddInstance(position, rotation, scale)` method expects **WORLD positions**, not local positions relative to the container.

**Wrong approach (didn't render):**
```csharp
Vector3 localPos = container.transform.InverseTransformPoint(worldPos);
instanceRenderer.AddInstance(localPos, rotation, scale);
```

**Correct approach (works!):**
```csharp
instanceRenderer.AddInstance(worldPos, rotation, scale);
```

This matches how `GrassOverlayController.SpawnInstancedGrass()` works - it uses `centerPos + offset` which are world coordinates.

### LOD Culling Fix
The vanilla `InstanceRenderer` has LOD settings that can cull grass when the camera is too close:
- `m_lodMinDistance = 15` (grass fades in at 15m)
- `m_useLod = true`

We disable these via reflection:
```csharp
var useLodField = irType.GetField("m_useLod", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
useLodField?.SetValue(instanceRenderer, false);

var lodMinField = irType.GetField("m_lodMinDistance", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
lodMinField?.SetValue(instanceRenderer, 0f);
```

### Instance Limit
`InstanceRenderer` has a hard cap of **1023 instances** per container. For larger areas, multiple containers are needed.

## Current Implementation

### Files Modified
- `FiresNPCs\Modules\EnvironmentBoxes\EnvironmentBoxClutterSpawner.cs` - Spawns clutter + paint mask
- `FiresNPCs\Modules\EnvironmentBoxes\EnvironmentBoxPatches.cs` - Routes paint ops to spawner
- `FiresNPCs\Modules\EnvironmentBoxes\EnvironmentBoxController.cs` - Hosts the spawner component

### Paint Mask System (Implemented)
The `EnvironmentBoxClutterSpawner` now has a paint mask system that:
1. Tracks painted areas via `_paintMask` texture (64x64 resolution)
2. Uses `ShouldSpawnGrassAtPosition()` to skip clutter on painted areas
3. Integrates with vanilla terrain painting via `TerrainComp_DoOperation_Postfix` patch

**Paint Colors (matching vanilla `Heightmap`):**
- Black (0,0,0,1) = Grass/default - clutter spawns here
- Red (1,0,0,1) = Dirt - no clutter
- Green (0,1,0,1) = Cultivated - no clutter
- Blue (0,0,1,1) = Paved - no clutter

### How Paint-to-Grass Integration Works

1. **Vanilla Paint Operation** (`TerrainComp.DoOperation`) triggers
2. Our **Harmony postfix** checks if position is inside an env box
3. If inside, we call `EnvironmentBoxClutterSpawner.PaintAt()`
4. Paint mask texture is updated
5. Clutter respawns with new paint mask applied
6. Grass is excluded from painted (non-black) areas

```csharp
// In SpawnClutterType(), before adding an instance:
if (!ShouldSpawnGrassAtPosition(spawnPos))
{
    continue; // Skip - this area is painted
}
```

## Remaining Work

### 1. Multiple Containers for Large Areas
For env boxes larger than ~16x16m, we need to split into multiple InstanceRenderer containers:
```csharp
// TODO: Split spawn area into cells
const int MAX_INSTANCES_PER_CELL = 800; // Leave headroom below 1023 cap
int cellsX = Mathf.CeilToInt(bounds.size.x / 8f); // 8m per cell
int cellsZ = Mathf.CeilToInt(bounds.size.z / 8f);
```

### 2. Biome-Specific Terrain Material
Apply biome-appropriate material to vanilla terrain inside env box:
- Use `TerrainMaterialCache` to get biome materials
- Override `_ClearedMaskTex` on heightmap material
- Show Ashlands lava, Swamp mud, etc.

### 3. Paint Mask Persistence
Save/load paint mask in ZDO:
```csharp
// Save
byte[] bytes = _paintMask.EncodeToPNG();
zdo.Set(ZDO_PAINT_MASK, bytes);

// Load  
byte[] bytes = zdo.GetByteArray(ZDO_PAINT_MASK);
_paintMask.LoadImage(bytes);
```

## Reference: Vanilla Systems

### TerrainComp Paint Flow
```
Player uses hoe/cultivator
    ? TerrainOp.Awake() ? TerrainComp.ApplyOperation()
        ? TerrainComp.DoOperation() ? TerrainComp.PaintCleared()
            ? Our postfix ? EnvironmentBoxClutterSpawner.PaintAt()
```

### ClutterSystem Spawn Flow
```
ClutterSystem.LateUpdate()
    ? GetPatchBiomes() [Our patch returns forced biome]
    ? For each Clutter with matching biome:
        ? GetGroundInfo() [Our patch returns forced biome]
        ? SpawnClutter at valid positions
```

## Testing Checklist
- [x] Grass spawns for forced biome (e.g., Ashlands grass in Meadows)
- [x] Paint operations remove grass from env box
- [ ] Large env boxes handle multiple containers
- [ ] Paint persists across save/load
- [ ] Infinity Hammer paint tools work inside env box
- [ ] Biome terrain material changes (lava/mud/etc)
