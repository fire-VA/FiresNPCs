# Clutter Paint Tool Implementation Guide

## Overview

This document details the implementation of the biome overlay and clutter painting system that integrates with Infinity Hammer. The system allows painting grass/vegetation and biome textures anywhere in the world, with full persistence across server restarts.

**Date:** Session completed successfully  
**Status:** ? WORKING

---

## What We Built

### 1. Biome Overlay Painter (`BiomeOverlayPainter.cs`)
- Paint terrain textures (snow, lava, swamp, forest, plains, mistlands, meadows)
- Works anywhere regardless of actual world biome
- Modifies heightmap vertex colors for visual effect

### 2. Clutter Paint Tool (`ClutterPaintTool.cs`)
- Paint any grass/vegetation type from Valheim's ClutterSystem
- Alt+Scroll cycling through clutter types
- Configurable radius and density
- 80m LOD distance for extended visibility

### 3. Persistence System (`BiomePaintPersistence.cs`)
- Stores paint data in TerrainComp ZDO
- Automatic restoration on zone load
- Deferred clutter restoration (waits for ClutterSystem)

### 4. Infinity Hammer Integration (`INFINITY_HAMMER_BIOME_TOOLS.yaml`)
- YAML tool definitions for Infinity Hammer's hoe menu
- Supports `<area>` parameter expansion
- Continuous mode, terrain grid snapping

---

## Key Technical Decisions

### Why TerrainComp ZDO for Storage?

TerrainComp is Valheim's terrain modification system. Each 64m zone has one TerrainComp that:
- Already persists in the world save
- Syncs automatically in multiplayer
- Loads when players enter the zone
- Has ZDO (ZNetView Data Object) for custom data

**Benefits:**
1. No custom save file needed
2. Automatic zone-based loading
3. Multiplayer sync for free
4. Data travels with the world

**ZDO Keys Used:**
```csharp
private static readonly int ZDO_BIOME_PAINTS = "va_biome_paints".GetStableHashCode();
private static readonly int ZDO_CLUTTER_PAINTS = "va_clutter_paints".GetStableHashCode();
```

### Data Format

Both biome and clutter paints use compressed ZPackage:

```csharp
// Biome Paint Data
public class BiomePaintData
{
    public float X, Y, Z;      // World position
    public float Radius;        // Paint radius
    public Heightmap.Biome Biome;  // Target biome
}

// Clutter Paint Data  
public class ClutterPaintData
{
    public float X, Y, Z;      // World position
    public float Radius;        // Paint radius
    public string ClutterType;  // e.g., "instanced_meadows_grass"
    public float Density;       // Spawn density multiplier
}
```

---

## Problems Solved

### Problem 1: Infinity Hammer `<area>` Parameter Expansion

**Issue:** Infinity Hammer expands `<area>` to:
```
from=<x>,<z>,<y> circle=<r>-<r2> angle=<a> rect=<w>-<w2>,<d> ignore=<ignore> id=<include>
```

This meant our command received many `key=value` arguments instead of simple `type radius` format.

**Solution:** Parse arguments intelligently:
```csharp
for (int i = 1; i < args.Length; i++)
{
    string arg = args[i];
    
    // Handle key=value style arguments from Infinity Hammer
    if (arg.Contains("="))
    {
        string[] parts = arg.Split(new char[] { '=' }, 2);
        string key = parts[0].ToLower();
        string value = parts.Length > 1 ? parts[1] : "";
        
        // Extract radius from circle=r-r2 format
        if (key == "circle" && !string.IsNullOrEmpty(value))
        {
            string[] radiusParts = value.Split('-');
            if (radiusParts.Length > 0 && float.TryParse(radiusParts[0], out float r))
            {
                radius = r;
            }
        }
        continue; // Skip other key=value params
    }
    
    // Try to parse as number (legacy radius format)
    if (float.TryParse(arg, out float numValue))
    {
        radius = numValue;
    }
    else
    {
        // Not a number and not key=value - must be clutter type
        clutterType = arg;
    }
}
```

### Problem 2: Clutter Not Visible After Server Restart

**Issue:** Grass was being "painted" (logs showed success) but wasn't visible after server restart.

**Root Cause:** `ClutterSystem.instance` was null or not fully initialized when `TerrainComp.Load` fired.

**Solution:** Deferred restoration using a MonoBehaviour that polls for ClutterSystem readiness:

```csharp
private class ClutterRestoreHelper : MonoBehaviour
{
    private static readonly Queue<(List<ClutterPaintData> paints, Vector3 position)> _pendingRestores;
    
    private void Update()
    {
        if (_pendingRestores.Count == 0) return;
        
        // Check if ClutterSystem is ready
        if (ClutterSystem.instance == null) return;
        if (ClutterSystem.instance.m_clutter == null || 
            ClutterSystem.instance.m_clutter.Count == 0) return;
        
        // Now safe to restore
        SetRestoring(true);
        try
        {
            while (_pendingRestores.Count > 0)
            {
                var (paints, position) = _pendingRestores.Dequeue();
                foreach (var paint in paints)
                {
                    ClutterPaintTool.PaintClutter(paint.Position, paint.Radius, 
                        paint.ClutterType, paint.Density);
                }
            }
        }
        finally
        {
            SetRestoring(false);
        }
    }
}
```

### Problem 3: Log Spam During Restoration

**Issue:** Hundreds of log lines when restoring clutter after zone load.

**Solution:** Check `IsRestoring` flag before logging:
```csharp
if (!BiomePaintPersistence.IsRestoring)
{
    Debug.Log($"[ClutterPaintTool] Painted {spawned} instances...");
}
```

### Problem 4: LOD Distance Too Short

**Issue:** Painted grass disappeared at ~40m (same as vanilla ClutterSystem).

**Solution:** Set explicit LOD distances for painted clutter:
```csharp
// Set LOD distances for painted clutter - we want high visibility range
instanceRenderer.m_lodMinDistance = 0f;   // Start rendering immediately
instanceRenderer.m_lodMaxDistance = 80f;  // Visible up to 80 meters
```

---

## Architecture

### Data Flow: Painting

```
User clicks with Infinity Hammer tool
    ?
Infinity Hammer expands <area> and runs command
    ?
paint_clutter instanced_meadows_grass from=x,z,y circle=r-r2 ...
    ?
ClutterPaintTool.PaintClutter()
    ??? Spawn InstanceRenderer with grass instances
    ??? BiomePaintPersistence.RecordClutterPaint()
            ?
        Find TerrainComp for position
            ?
        Load existing paints from ZDO
            ?
        Add new paint, save back to ZDO
```

### Data Flow: Restoration

```
Player enters zone / Server loads zone
    ?
TerrainComp.Load() fires
    ?
[HarmonyPostfix] RestoreFromTerrainComp()
    ??? Load biome paints ? Add to memory cache, Poke heightmap
    ??? Load clutter paints ? Queue for deferred restoration
            ?
ClutterRestoreHelper.Update() polls every 0.5s
    ?
ClutterSystem.instance ready?
    ? YES
Process all queued paints
    ?
ClutterPaintTool.PaintClutter() (with IsRestoring=true)
    ?
Grass visible!
```

### Harmony Patches

```csharp
[HarmonyPatch(typeof(TerrainComp), "Load")]
[HarmonyPostfix]
public static void TerrainComp_Load_Postfix(TerrainComp __instance, bool __result)
{
    if (!__result) return;
    RestoreFromTerrainComp(__instance);
}

[HarmonyPatch(typeof(TerrainComp), "OnDestroy")]
[HarmonyPostfix]
public static void TerrainComp_OnDestroy_Postfix(TerrainComp __instance)
{
    OnTerrainCompDestroyed(__instance);
}

[HarmonyPatch(typeof(ZNetScene), "OnDestroy")]
[HarmonyPrefix]
public static void ZNetScene_OnDestroy_Prefix()
{
    OnWorldUnload(); // Clear all caches
}
```

---

## Files Created/Modified

| File | Purpose |
|------|---------|
| `BiomeOverlayPainter.cs` | Console commands for biome painting |
| `ClutterPaintTool.cs` | Clutter spawning, cycling, console commands |
| `ClutterPaintInputHandler.cs` | Alt+Scroll input handling |
| `BiomePaintPersistence.cs` | ZDO save/load, Harmony patches, restoration |
| `INFINITY_HAMMER_BIOME_TOOLS.yaml` | Infinity Hammer tool definitions |

---

## Applying This Pattern to Custom Terrain

### Current Custom Terrain Save/Load

Currently, DirtFloor terrain uses:
- Individual ZDOs per disk piece
- Custom save manager with batched saves
- Zone-based tracking

### Proposed: TerrainComp ZDO Integration

We can piggyback on TerrainComp's ZDO system for custom terrain data:

```csharp
// ZDO keys for custom terrain
private static readonly int ZDO_CUSTOM_TERRAIN = "va_custom_terrain".GetStableHashCode();

// Data structure
public class CustomTerrainData
{
    public Vector3 Position;
    public float Radius;
    public float[] Heights;     // Height array
    public byte[] PaintMask;    // Compressed paint texture
    public string GrassType;    // Which grass to spawn
}
```

### Benefits for Custom Terrain

1. **Automatic Zone Loading** - TerrainComp loads when zone loads
2. **No Custom File I/O** - Data lives in world save
3. **Multiplayer Sync** - ZDO sync handles it
4. **Cleanup on Zone Unload** - TerrainComp.OnDestroy fires

### Implementation Plan

1. **Save custom terrain to nearest TerrainComp's ZDO**
   ```csharp
   TerrainComp tc = TerrainComp.FindTerrainCompiler(diskPosition);
   var zdo = tc.GetComponent<ZNetView>().GetZDO();
   zdo.Set(ZDO_CUSTOM_TERRAIN, CompressData(terrainData));
   ```

2. **Load on TerrainComp.Load postfix**
   ```csharp
   [HarmonyPostfix]
   static void OnTerrainCompLoad(TerrainComp __instance)
   {
       var customData = LoadCustomTerrainData(__instance);
       foreach (var disk in customData)
       {
           DirtFloorTerrainManager.RestoreDisk(disk);
       }
   }
   ```

3. **Handle zone boundaries**
   - Custom terrain near zone edges might span multiple TerrainComps
   - Store in the TerrainComp closest to disk center
   - Or store reference in adjacent TerrainComps

4. **Defer mesh generation**
   - Like clutter, wait for systems to be ready
   - Use similar polling approach if needed

---

## Testing Checklist

### Clutter Paint Tool
- [x] Paint clutter with console command
- [x] Paint clutter with Infinity Hammer tool
- [x] Alt+Scroll cycles clutter types
- [x] Clutter visible immediately after painting
- [x] Clutter persists after logout/login (server running)
- [x] Clutter persists after server restart
- [x] Clear clutter works
- [x] LOD distance is ~80m

### Biome Overlay
- [x] Paint biome overlay changes terrain texture
- [x] Multiple biome types work
- [x] Reset biome overlay works
- [x] Persists across zone reload

### Edge Cases
- [ ] Multiple zones with painted clutter
- [ ] Very large paint radius (spans zones)
- [ ] Rapid painting (many operations quickly)
- [ ] Paint, logout, different player loads zone

---

## Performance Considerations

### Current Approach
- Each paint operation = one list entry in ZDO
- Restoration spawns all clutter on zone load
- ~300 instances per paint operation (radius 5)

### Potential Optimizations
1. **Consolidate overlapping paints** - Merge paints that overlap significantly
2. **Lazy loading** - Only spawn clutter when player is close
3. **Instance batching** - Group nearby paints into single InstanceRenderer
4. **LOD streaming** - Reduce instance count at distance

### Memory Usage
- Each ClutterPaintData: ~50 bytes
- 100 paints per zone: ~5KB (compressed: ~2KB)
- InstanceRenderer overhead: Minimal (GPU instancing)

---

## Conclusion

The TerrainComp ZDO approach works excellently for:
- Zone-based data that should load/unload with terrain
- Data that needs multiplayer sync
- Avoiding custom save file management

This same pattern can be applied to custom terrain (DirtFloor) to:
- Simplify save/load code
- Get automatic zone management
- Improve multiplayer reliability

The key insight is that **TerrainComp's ZDO is our free storage container** - it already exists for every 64m zone, already persists, already syncs. We just add our data to it.
