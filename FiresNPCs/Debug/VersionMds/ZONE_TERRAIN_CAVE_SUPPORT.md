# Zone-Based Terrain: Cave Support & Multi-Layer Implementation

## ✅ ZONE SYSTEM ENABLED AND FIXED

The zone-based terrain system (`UseZoneBasedTerrain = true`) has been fixed with the following improvements:

### Fixes Applied

1. **Performance** - Added regeneration cooldown (0.1s) to prevent mesh rebuilding every frame
2. **Pickaxe Support** - Added `WearNTear` component to zones for pickaxe collision detection
3. **Slope Tool** - Added `SlopeArea()` to zones with proper direction vector validation
4. **Smooth Tool** - Added `SmoothArea()` to zones with neighbor averaging
5. **Height Layer Logic** - Fixed to CREATE terrain at any height, only BLEND within 8m
6. **Zero Vector Fix** - Slope direction now defaults to `Vector3.forward` if zero
7. **Grass Strips Fix** - Changed to world-space float iteration instead of grid steps
8. **Tool Detection** - Updated `DirtFloorPatches` to detect zone terrain for operations
9. **Height Queries** - `TryGetCustomTerrainHeight` now checks zone terrain first

### Remaining Work

1. ⬜ **Persistence** - Zones need ZDO save/load for terrain data
2. ⬜ **Multi-Layer** - Single height array can't represent floor + ceiling yet
3. ⬜ **Hoe/Cultivator UI** - `_hoveredDirtFloor` doesn't track zones yet

---

## Overview

This document outlines the plan to add proper cave support to our zone-based custom terrain system (`CustomTerrainZone`). The goal is to replicate the functionality we had in the disk-based system while preparing for future cave-creation tools.

## Current Issues

### 1. Missing UnderWorldCheck Bypass
The disk-based system had logic in `DirtFloorPatches.cs` that bypassed Valheim's `Character.UnderWorldCheck` when players were near custom terrain. This allowed players to be **under** our terrain without being teleported up.

**Current disk-based implementation:**
```csharp
[HarmonyPatch(typeof(Character), "UnderWorldCheck")]
[HarmonyPrefix]
private static bool UnderWorldCheck_Prefix(Character __instance)
{
    Vector3 charPos = __instance.transform.position;
    
    // Check if we're anywhere near custom terrain (horizontally)
    if (IsNearCustomTerrain(charPos, 10f))
    {
        // Near custom terrain - completely skip the underworld check
        return false;
    }
    
    // Not near custom terrain - let vanilla proceed
    return true;
}
```

**Problem:** This only checks for disk-based terrain, not zone-based terrain.

### 2. No Height Difference Threshold for Stretching
The disk-based combiner had a `MAX_COMBINER_VERTICAL_SPAN = 20f` constant that prevented disks at vastly different heights from merging, which would cause ugly terrain stretching.

**Zone-based system lacks this:** Currently, `CustomTerrainZone.LevelArea()` will happily stretch terrain between any two heights, which would create problems for cave systems where you have a floor at Y=50 and a ceiling at Y=58.

### 3. No Terrain Deletion Tool
To create proper caves, we need:
1. A tool to **delete** custom terrain in a radius (punch holes)
2. Support for **multiple terrain layers** at different heights in the same XZ area
3. Proper collision on both layers (floor and ceiling)

---

## Implementation Plan

### Phase 1: UnderWorldCheck for Zone System (Priority: HIGH)

**Goal:** Players can walk under zone-based terrain without being teleported up.

**Changes to `DirtFloorPatches.cs`:**

```csharp
private static bool IsNearCustomTerrain(Vector3 worldPos, float radius)
{
    // Check zone-based terrain first (new system)
    if (DirtFloorTerrainManager.UseZoneBasedTerrain)
    {
        if (CustomTerrainZoneManager.HasTerrainAt(worldPos))
            return true;
            
        // Also check nearby zones
        var zones = CustomTerrainZoneManager.GetZonesInRadius(worldPos, radius);
        if (zones.Count > 0)
            return true;
    }
    
    // Fall back to disk-based check
    var dirtFloor = DirtFloorController.GetDirtFloorAtPosition(worldPos, radius);
    if (dirtFloor != null)
        return true;
    
    var combiner = DirtFloorMeshCombiner.GetCombinerAt(worldPos);
    if (combiner != null)
        return true;
    
    return false;
}
```

### Phase 2: Height Threshold for Zone Stretching (Priority: HIGH)

**Goal:** Terrain operations at heights > 8m apart don't stretch to connect.

**Concept:** Each zone can have **multiple height layers**. When leveling terrain:
- If the target height is within 8m of existing terrain, blend/connect
- If the target height is > 8m from existing terrain, create a **new layer**

**Data Structure Change in `CustomTerrainZone.cs`:**

```csharp
/// <summary>
/// Height layers for cave support. Each layer is independent terrain.
/// Key = base height (rounded to nearest 8m), Value = height data for that layer
/// </summary>
private Dictionary<int, float[]> _heightLayers;

/// <summary>
/// Which layer each vertex belongs to (for mesh generation).
/// </summary>
private int[] _vertexLayer;

/// <summary>
/// Maximum vertical distance to connect terrain.
/// Beyond this, create separate layers.
/// </summary>
private const float MAX_LAYER_VERTICAL_DISTANCE = 8f;
```

**New Methods:**

```csharp
/// <summary>
/// Gets the layer index for a given height.
/// Heights within MAX_LAYER_VERTICAL_DISTANCE are the same layer.
/// </summary>
private int GetLayerForHeight(float height)
{
    return Mathf.RoundToInt(height / MAX_LAYER_VERTICAL_DISTANCE);
}

/// <summary>
/// Applies terrain to a specific layer based on target height.
/// </summary>
public void LevelAreaLayered(Vector3 worldCenter, float radius, float targetHeight)
{
    int targetLayer = GetLayerForHeight(targetHeight);
    
    // Get or create height array for this layer
    if (!_heightLayers.TryGetValue(targetLayer, out float[] layerHeights))
    {
        layerHeights = new float[GRID_SIZE * GRID_SIZE];
        for (int i = 0; i < layerHeights.Length; i++)
            layerHeights[i] = float.MinValue;
        _heightLayers[targetLayer] = layerHeights;
    }
    
    // Apply level operation to this layer only
    // ... (similar to current LevelArea but using layerHeights)
}
```

### Phase 3: Multi-Layer Mesh Generation (Priority: MEDIUM)

**Goal:** Generate separate meshes for each height layer, each with its own collision.

**Changes to `RegenerateMesh()`:**

```csharp
public void RegenerateMesh()
{
    // Clear old meshes
    ClearAllMeshes();
    
    // Generate mesh for each layer
    foreach (var kvp in _heightLayers)
    {
        int layerIndex = kvp.Key;
        float[] layerHeights = kvp.Value;
        
        // Skip empty layers
        if (!HasValidTerrain(layerHeights))
            continue;
        
        // Create mesh for this layer
        var layerMesh = GenerateLayerMesh(layerIndex, layerHeights);
        
        // Create collision for this layer
        var layerCollider = CreateLayerCollider(layerIndex, layerMesh);
        
        _layerMeshes[layerIndex] = layerMesh;
        _layerColliders[layerIndex] = layerCollider;
    }
}
```

### Phase 4: Terrain Deletion Tool (Priority: MEDIUM)

**Goal:** Console command/tool to delete custom terrain in a radius.

**New Command:** `terrain_delete <radius>`

**Implementation in `TerrainOperationExecutor.cs`:**

```csharp
case "delete":
case "remove":
case "clear":
    if (DirtFloorTerrainManager.UseZoneBasedTerrain)
    {
        CustomTerrainZoneManager.DeleteArea(pars.Position, pars.Size);
    }
    else
    {
        // Legacy: Remove disks in radius
        var disksToRemove = DirtFloorTerrainManager.GetDisksFullyInsideRadius(pars.Position, pars.Size);
        foreach (var disk in disksToRemove)
        {
            DirtFloorTerrainManager.RemoveDisk(disk);
        }
    }
    return true;
```

**New Method in `CustomTerrainZoneManager.cs`:**

```csharp
/// <summary>
/// Deletes terrain in a radius across all layers.
/// This creates holes for cave entrances.
/// </summary>
public static void DeleteArea(Vector3 center, float radius)
{
    var zones = GetZonesInRadius(center, radius);
    
    foreach (var zone in zones)
    {
        zone.DeleteArea(center, radius);
    }
    
    Debug.Log($"[CustomTerrainZoneManager] Deleted terrain at {center}, radius={radius} in {zones.Count} zone(s)");
}
```

**New Method in `CustomTerrainZone.cs`:**

```csharp
/// <summary>
/// Deletes terrain in a radius from all layers.
/// Sets height to float.MinValue (no terrain).
/// </summary>
public void DeleteArea(Vector3 worldCenter, float radius)
{
    float radiusSq = radius * radius;
    
    // Delete from all layers
    foreach (var kvp in _heightLayers)
    {
        float[] layerHeights = kvp.Value;
        
        for (int gz = 0; gz < GRID_SIZE; gz++)
        {
            for (int gx = 0; gx < GRID_SIZE; gx++)
            {
                Vector3 worldPos = GridToWorld(gx, gz);
                
                float dx = worldPos.x - worldCenter.x;
                float dz = worldPos.z - worldCenter.z;
                float distSq = dx * dx + dz * dz;
                
                if (distSq <= radiusSq)
                {
                    int idx = gz * GRID_SIZE + gx;
                    layerHeights[idx] = float.MinValue;
                    _hasTerrainAt[idx] = false;
                }
            }
        }
    }
    
    _isDirty = true;
}
```

### Phase 5: GetGroundHeight for Layered Terrain (Priority: HIGH)

**Goal:** `ZoneSystem.GetGroundHeight` returns the correct layer based on query height.

**Problem:** If player is at Y=52 (inside cave), we need to return Y=50 (floor), not Y=58 (ceiling).

**Solution:** Check all layers and return the one **closest below** the query position.

```csharp
public static float GetHeightAt(Vector3 worldPos)
{
    var zone = GetZoneAt(worldPos);
    if (zone == null) return float.MinValue;
    
    return zone.GetHeightAtLayered(worldPos);
}

// In CustomTerrainZone:
public float GetHeightAtLayered(Vector3 worldPos)
{
    WorldToGrid(worldPos, out int gx, out int gz);
    
    if (gx < 0 || gx >= GRID_SIZE || gz < 0 || gz >= GRID_SIZE)
        return float.MinValue;
    
    int idx = gz * GRID_SIZE + gx;
    
    // Find the layer closest BELOW the query position
    float bestHeight = float.MinValue;
    float queryY = worldPos.y;
    
    foreach (var kvp in _heightLayers)
    {
        float layerHeight = kvp.Value[idx];
        
        if (layerHeight > float.MinValue + 1f)
        {
            // Only consider layers below or at the query position
            if (layerHeight <= queryY + 0.5f)
            {
                if (layerHeight > bestHeight)
                {
                    bestHeight = layerHeight;
                }
            }
        }
    }
    
    return bestHeight;
}
```

---

## Implementation Order

### Sprint 1: Core Cave Support (This Session)
1. ? Update `IsNearCustomTerrain()` to check zone-based terrain
2. ? Add `MAX_VERTICAL_STRETCH_DISTANCE` constant (8m)
3. ? Update `LevelArea` to CREATE terrain at any height, but only BLEND if within 8m
4. ? Add `DeleteArea` method to `CustomTerrainZone`
5. ? Add `DeleteArea` method to `CustomTerrainZoneManager`
6. ? Add `Delete` property to `TerrainOperationParams`
7. ? Add `ExecuteDelete` to `TerrainOperationExecutor`
8. ? Add `GetHeightAtPreferBelow` for cave-aware height queries
9. ? Add `SlopeArea` to `CustomTerrainZone` and `CustomTerrainZoneManager`
10. ? Fix zero direction vector in SlopeArea (caused "Look rotation viewing vector is zero" spam)
11. ? Fix grass spawning strips (use world-space iteration instead of grid steps)

### Sprint 2: Terrain Deletion Tool
1. ? Add `DeleteArea()` to `CustomTerrainZone` - DONE
2. ? Add `DeleteArea()` to `CustomTerrainZoneManager` - DONE
3. ? Add "delete" operation to `TerrainOperationExecutor` - DONE
4. ? Test cave creation workflow

### Sprint 3: Polish & Edge Cases
1. ? Grass spawning per layer (when multi-layer implemented)
2. ? Paint mask per layer (when multi-layer implemented)
3. ? Edge stitching between layers
4. ? Save/load layer data
5. ? Fix BiomePaintPersistence loading repeatedly (performance issue)

---

## Testing Checklist

### Cave Creation Test
1. Create terrain at Y=50 (floor)
2. Create terrain at Y=58 (ceiling) - should NOT connect
3. Walk under ceiling - should NOT be teleported up
4. Delete terrain in a circle (cave entrance)
5. Walk through entrance into cave
6. Verify floor and ceiling have independent collision

### Layer Independence Test
1. Paint dirt on floor (Y=50)
2. Paint paved on ceiling (Y=58)
3. Verify paint doesn't bleed between layers
4. Verify grass spawns correctly on each layer

---

## Related Files

- `CustomTerrainZone.cs` - Zone terrain data and mesh generation
- `CustomTerrainZoneManager.cs` - Zone management and cross-zone operations
- `DirtFloorPatches.cs` - Harmony patches including UnderWorldCheck
- `InfinityHammerCompat.cs` - ZoneSystem.GetGroundHeight patches
- `TerrainOperationExecutor.cs` - Console command execution

---

## Notes

### Why Layers Instead of Separate Zones?
- Zones are 64x64m aligned to world grid
- A cave might span multiple zones
- Layers within a zone keep data co-located
- Easier to query "what's below me" within a zone

### Maximum Cave Height
With `MAX_LAYER_VERTICAL_DISTANCE = 8m`, caves can be up to ~7m tall (floor to ceiling).
This is sufficient for most cave designs. Taller caves would need layer 0 (floor) and layer 2 (ceiling), leaving layer 1 empty.

### Future: Cave Generation Tool
Once this infrastructure is in place, we can create a proper cave generation tool that:
1. Creates floor terrain
2. Creates ceiling terrain (8m+ above)
3. Deletes a circle for the entrance
4. Optionally adds wall meshes for vertical surfaces
