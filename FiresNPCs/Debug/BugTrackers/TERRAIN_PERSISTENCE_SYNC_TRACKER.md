# Terrain & Vegetation Persistence/Sync Bug Tracker

## Overview

This document tracks issues and fixes needed to ensure all custom terrain edits, biome paints, and vegetation/clutter data properly persist and sync in multiplayer.

**Created:** 2025-01-XX  
**Status:** In Progress

---

## ?? CRITICAL ISSUE: Zone-Based Terrain Has NO Persistence!

**Root Cause Found:** The new zone-based terrain system (`CustomTerrainZone`) has **ZERO persistence**. 
- `DirtFloorTerrainManager.UseZoneBasedTerrain = true` routes all Infinity Hammer operations to `CustomTerrainZoneManager`
- `CustomTerrainZone` objects are created dynamically in memory
- **There is NO ZDO, NO saving to TerrainComp, NO persistence at all!**
- When you log out or the zone unloads, ALL terrain changes are LOST

**Why Other Terrain Works:**
- Disk-based terrain (`DirtFloorController`) has its own `ZNetView` and persists as a placed piece
- Zone-based terrain was intended to replace this but persistence was never implemented

**Immediate Fix Options:**
1. **Quick Fix:** Set `DirtFloorTerrainManager.UseZoneBasedTerrain = false` to use disk-based system
2. **Proper Fix:** Implement persistence for `CustomTerrainZone` using TerrainComp ZDO (like BiomePaintPersistence)

---

## Current Architecture Summary

### 0. CustomTerrainZone System (`CustomTerrainZone.cs`) - ?? BROKEN

**Storage Method:** NONE! (Memory only)  
**ZDO Keys:** None defined

**Current Status:** ? NO PERSISTENCE

| Feature | Status | Notes |
|---------|--------|-------|
| Zone creation | ? Working | Creates zones in memory |
| Height operations | ? Working | Level, raise, lower, smooth, slope all work |
| Paint operations | ? Working | Paint mask updates |
| **Save to ZDO** | ? **NOT IMPLEMENTED** | Zones are memory-only |
| **Load from ZDO** | ? **NOT IMPLEMENTED** | Nothing to load |
| **Multiplayer sync** | ? **NOT IMPLEMENTED** | No ZDO = no sync |
| Zone unload handling | ? **NOT IMPLEMENTED** | Data lost on unload |

**Required Implementation:**
```csharp
// Each CustomTerrainZone needs to save/load:
// 1. ZoneId (Vector2i) - for identification
// 2. Heights (float[65*65]) - compressed
// 3. HasTerrainAt (bool[65*65]) - as bitmask
// 4. PaintMask (128x128 texture) - PNG compressed
// 5. BaseHeight (float)
```

### 1. BiomePaintPersistence System (`BiomePaintPersistence.cs`)

**Storage Method:** TerrainComp ZDO  
**ZDO Keys:**
- `va_biome_paints` - Compressed biome overlay data
- `va_clutter_paints` - Compressed clutter paint data

**Current Status:** ?? Partial Implementation

| Feature | Status | Notes |
|---------|--------|-------|
| Biome paint save to ZDO | ? Working | Saves to TerrainComp ZDO |
| Biome paint load from ZDO | ? Working | Restores on TerrainComp.Load |
| Clutter paint save to ZDO | ? Working | Saves to TerrainComp ZDO |
| Clutter paint restore | ?? Partial | Uses coroutine, may have timing issues |
| Multiplayer sync (host?client) | ? Untested | Relies on vanilla ZDO sync |
| Multiplayer sync (client?host) | ? Issue | Ownership check may block client saves |
| World reload persistence | ? Working | Data survives logout/login |
| Zone unload/reload | ? Working | _restoredZones tracking |

**Known Issues:**
1. **Ownership blocking client saves** - `SaveBiomePaintsToTerrainComp` checks `nview.IsOwner()` which returns false for clients
2. **In-memory cache not synced** - `_activePaints` is local only, not synced between clients
3. **Clutter restore timing** - ClutterRestoreHelper may miss paints if ClutterSystem not ready

### 2. DirtFloorTerrainCompStorage System (`DirtFloorTerrainCompStorage.cs`)

**Storage Method:** TerrainComp ZDO  
**ZDO Keys:**
- `va_custom_disks` - Compressed disk data (position, radius, paint mask, grass type)

**Current Status:** ?? Partial Implementation

| Feature | Status | Notes |
|---------|--------|-------|
| Disk save to ZDO | ? Working | Saves on disk creation/modification |
| Disk load from ZDO | ?? Partial | Only updates existing disks, doesn't create new |
| Paint mask persistence | ? Working | PNG-encoded in ZDO |
| Height data persistence | ? Skipped | Comment says "skip for now" |
| Multiplayer sync (host?client) | ? Untested | Relies on vanilla ZDO sync |
| Multiplayer sync (client?host) | ? Issue | Ownership check blocks client saves |
| Disk creation on restore | ? Not Implemented | `CreateDiskFromData` returns null |

**Known Issues:**
1. **Ownership blocking** - Same as BiomePaintPersistence
2. **No disk creation** - If disk prefab doesn't exist, restore fails
3. **Heights not persisted** - Relies on CustomHeightmap's own ZDO which may not be synced
4. **Zone duplicate detection** - Vanilla TerrainComp duplicate detection can cause issues

### 3. DirtFloorController/CustomHeightmap System

**Storage Method:** Individual piece ZDO (not TerrainComp)  
**ZDO Keys:** (on each DirtFloor piece)
- Height data stored per-piece
- Paint mask stored per-piece (duplicated with TerrainCompStorage)

**Current Status:** ?? Mixed

| Feature | Status | Notes |
|---------|--------|-------|
| Per-piece ZDO storage | ? Working | Each disk has own ZNetView |
| Height data save | ? Working | Stored in disk's ZDO |
| Height data load | ? Working | LoadStateFromZDO() |
| Multiplayer sync | ? Working | Standard piece sync |
| Paint mask sync | ?? Duplicate | Stored both in piece and TerrainComp |

### 4. Underwater Bubble System (`UnderwaterBubbleGenerator.cs`)

**Storage Method:** Individual piece ZDO  
**ZDO Keys:**
- `va_bubble_fuel` - Fuel amount
- `va_bubble_radius` - Current radius
- `va_bubble_active` - Active state
- `va_bubble_env` - Environment override index

**Current Status:** ? Working

| Feature | Status | Notes |
|---------|--------|-------|
| State save to ZDO | ? Working | RPC-based sync |
| State load from ZDO | ? Working | On Awake/Start |
| Multiplayer sync | ? Working | Uses RPC system |
| Environment persistence | ? Working | Stored in ZDO |

---

## Issues to Fix

### HIGH PRIORITY

#### Issue 1: Ownership Blocking Client Edits
**Affected Systems:** BiomePaintPersistence, DirtFloorTerrainCompStorage  
**Problem:** Both systems check `nview.IsOwner()` before saving, which fails for clients  
**Impact:** Clients cannot persist their terrain/biome edits

**Solution Options:**
1. **RPC approach** - Client sends edit request to server via RPC, server applies and saves
2. **Claim ownership** - Temporarily claim TerrainComp ownership before editing
3. **Queue system** - Client queues edits, syncs when they become owner

**Recommended:** Option 1 (RPC) for proper multiplayer behavior

```csharp
// Example RPC pattern
private void RPC_ApplyBiomePaint(long sender, Vector3 center, float radius, int biome)
{
    if (!m_nview.IsOwner()) return; // Only server/owner applies
    
    // Apply the paint
    var paint = new BiomePaintData { X = center.x, ... };
    SaveBiomePaintsToTerrainComp(terrainComp, paints);
    
    // Sync to all clients via ZDO (automatic)
}

public void RequestBiomePaint(Vector3 center, float radius, Heightmap.Biome biome)
{
    // Client calls this, sends RPC to owner
    m_nview.InvokeRPC("ApplyBiomePaint", center, radius, (int)biome);
}
```

#### Issue 2: In-Memory Cache Not Synced
**Affected Systems:** BiomePaintPersistence._activePaints  
**Problem:** Paint cache is local-only, other clients don't see it  
**Impact:** Visual inconsistency between clients

**Solution:**
- Rebuild cache from ZDO on zone load for each client
- Current implementation already does this in `RestoreFromTerrainComp` but may have timing issues

**Fix:**
```csharp
// In TerrainComp_Load_Postfix, ensure EVERY client loads paints
[HarmonyPatch(typeof(TerrainComp), "Load")]
[HarmonyPostfix]
public static void TerrainComp_Load_Postfix(TerrainComp __instance, bool __result)
{
    // This should run on ALL clients, not just owner
    RestoreFromTerrainComp(__instance);
}
```

#### Issue 3: DirtFloor Disk Creation Not Implemented
**Affected Systems:** DirtFloorTerrainCompStorage  
**Problem:** `CreateDiskFromData` returns null  
**Impact:** If disk prefab isn't already placed, restore fails

**Solution:**
```csharp
private static DirtFloorController CreateDiskFromData(DiskData data)
{
    // Find the DirtFloor prefab
    var prefab = ZNetScene.instance?.GetPrefab("Piece_DirtFloor_vad");
    if (prefab == null) return null;
    
    // Instantiate at saved position
    var instance = UnityEngine.Object.Instantiate(prefab, data.Position, Quaternion.identity);
    var disk = instance.GetComponent<DirtFloorController>();
    
    if (disk != null)
    {
        // Apply saved data
        ApplyDiskData(disk, data);
    }
    
    return disk;
}
```

### MEDIUM PRIORITY

#### Issue 4: Height Data Not Persisted in TerrainCompStorage
**Affected Systems:** DirtFloorTerrainCompStorage  
**Problem:** Comment says heights are skipped, relies on CustomHeightmap  
**Impact:** Potential data loss if CustomHeightmap ZDO is lost

**Solution:**
- Either fully implement height compression/save in TerrainCompStorage
- Or ensure CustomHeightmap ZDO is always synced with TerrainComp ZDO

#### Issue 5: Duplicate Data Storage
**Affected Systems:** DirtFloorController, DirtFloorTerrainCompStorage  
**Problem:** Paint mask stored in BOTH per-piece ZDO AND TerrainComp ZDO  
**Impact:** Data inconsistency, wasted bandwidth

**Solution:**
- Pick ONE authoritative storage location
- Recommend: TerrainComp ZDO as primary (zone-based, automatic sync)
- Per-piece ZDO as backup/individual override

#### Issue 6: Clutter Restore Timing Issues
**Affected Systems:** BiomePaintPersistence.ClutterRestoreHelper  
**Problem:** ClutterSystem may not be ready when TerrainComp loads  
**Impact:** Clutter may not spawn correctly on zone load

**Solution:**
- Current coroutine approach is correct but needs better wait logic
- Add fallback for when ClutterSystem never becomes ready

### LOW PRIORITY

#### Issue 7: Zone Duplicate Detection Conflicts
**Affected Systems:** DirtFloorTerrainCompStorage  
**Problem:** Vanilla TerrainComp duplicate detection can delete our restored data  
**Impact:** Potential data loss on rapid zone load/unload

**Current Mitigation:** _restoredZones HashSet prevents double-restore  
**Additional Fix:** Add frame-based debouncing (already partially implemented)

---

## Implementation Plan

### Phase 1: Fix Ownership Issues (HIGH PRIORITY)

1. **Add RPC methods to BiomePaintPersistence**
   - `RPC_ApplyBiomePaint` - Apply biome overlay
   - `RPC_ApplyClutterPaint` - Apply clutter paint
   - `RPC_ResetBiomePaint` - Reset biome overlay
   - Register RPCs on TerrainComp ZNetView

2. **Add RPC methods to DirtFloorTerrainCompStorage**
   - `RPC_SaveDisk` - Save disk data
   - `RPC_RemoveDisk` - Remove disk data

3. **Update public API to use RPCs**
   - `PaintBiomeOverlay()` ? calls RPC if not owner
   - `SaveDisk()` ? calls RPC if not owner

### Phase 2: Fix Cache Sync (HIGH PRIORITY)

1. **Ensure all clients load paint cache on zone load**
   - Verify `RestoreFromTerrainComp` runs on all clients
   - Add debug logging to confirm

2. **Add cache invalidation when ZDO changes**
   - Hook into ZDO update notifications
   - Rebuild local cache when paint data changes

### Phase 3: Implement Disk Creation (MEDIUM PRIORITY)

1. **Complete `CreateDiskFromData` implementation**
   - Find/spawn DirtFloor prefab
   - Apply position, radius, paint mask, grass type

2. **Handle prefab not found gracefully**
   - Log warning
   - Queue for retry when prefab becomes available

### Phase 4: Clean Up Duplicate Storage (LOW PRIORITY)

1. **Consolidate storage locations**
   - Document which system is authoritative
   - Add migration path for old data

2. **Remove redundant saves**
   - Per-piece paint mask if using TerrainComp
   - Or vice versa

---

## Testing Checklist

### Single Player
- [ ] Biome paint persists after logout/login
- [ ] Clutter paint persists after logout/login
- [ ] DirtFloor disks persist after logout/login
- [ ] Zone unload/reload preserves all data
- [ ] Data survives game restart

### Multiplayer (as Host)
- [ ] Host's biome paints visible to joining clients
- [ ] Host's clutter paints visible to joining clients
- [ ] Host's DirtFloor disks visible to joining clients
- [ ] Changes sync to clients in real-time

### Multiplayer (as Client)
- [ ] Client can paint biomes (syncs to host)
- [ ] Client can paint clutter (syncs to host)
- [ ] Client can place/edit DirtFloors (syncs to host)
- [ ] Client changes persist after disconnect/reconnect
- [ ] Other clients see client's changes

### Edge Cases
- [ ] Rapid zone load/unload doesn't lose data
- [ ] Multiple clients editing same area simultaneously
- [ ] Host disconnect/reconnect preserves client changes
- [ ] Very large paint operations don't cause lag
- [ ] ZDO data size limits not exceeded

---

## Files to Modify

| File | Changes Needed |
|------|----------------|
| `BiomePaintPersistence.cs` | Add RPC methods, fix ownership |
| `DirtFloorTerrainCompStorage.cs` | Add RPC methods, implement disk creation |
| `ClutterPaintTool.cs` | Use RPC for paint operations |
| `BiomeOverlayPainter.cs` | Use RPC for paint operations |
| `DirtFloorController.cs` | Ensure ZDO save triggers TerrainCompStorage |
| `CustomHeightmap.cs` | Verify height sync with TerrainComp |

---

## Related Documentation

- `DIRTFLOOR_TERRAINCOMP_MIGRATION.md` - TerrainComp storage migration notes
- `BIOME_PAINT_CHANGELOG.md` - Biome paint system history
- `CLUTTER_PAINT_IMPLEMENTATION.md` - Clutter paint implementation
- `ZONE_TERRAIN_CAVE_SUPPORT.md` - Zone-based terrain notes

---

## Notes

### Why TerrainComp ZDO?
- Automatic zone-based loading/unloading
- Vanilla handles sync automatically
- Matches how Valheim stores terrain modifications
- One ZDO per 64m zone (efficient)

### Why Not Per-Piece ZDO?
- Each piece needs its own ZNetView (overhead)
- Doesn't automatically unload with zone
- More complex to manage for area-based operations
- But: useful for individual piece properties

### Hybrid Approach (Current)
- Zone-level data (biome paints, disk catalog) ? TerrainComp ZDO
- Piece-level data (individual disk heights) ? Per-piece ZDO
- This is reasonable but needs clear documentation

---

## ?? CRITICAL: CustomTerrainZone Persistence Implementation

### Overview

The new `CustomTerrainZone` system has NO persistence. This section outlines how to implement it properly.

### Storage Strategy

**Use TerrainComp ZDO** - The zone-based system aligns with Valheim's 64m zones, so we should store zone data in the corresponding TerrainComp's ZDO.

### ZDO Keys for CustomTerrainZone

```csharp
// ZDO keys (stable hash codes)
private static readonly int ZDO_ZONE_VERSION = "va_zone_version".GetStableHashCode();
private static readonly int ZDO_ZONE_BASE_HEIGHT = "va_zone_base_height".GetStableHashCode();
private static readonly int ZDO_ZONE_HEIGHTS = "va_zone_heights".GetStableHashCode();      // Compressed float[]
private static readonly int ZDO_ZONE_TERRAIN_MASK = "va_zone_terrain_mask".GetStableHashCode();  // Bit-packed bool[]
private static readonly int ZDO_ZONE_PAINT_MASK = "va_zone_paint_mask".GetStableHashCode();  // PNG compressed
```

### Data Format

```
Version: int (1)
BaseHeight: float (4 bytes)
Heights: compressed float[65*65] (4225 floats ? ~17KB ? compressed to ~3-6KB)
TerrainMask: bit-packed bool[65*65] (4225 bits ? 528 bytes)
PaintMask: PNG compressed 128x128 RGBA (~2-8KB)

Total estimated: 6-15KB per zone (acceptable)
```

### Height Compression Strategy

```csharp
// Delta-encode heights relative to base height
// Then quantize to 16-bit values (0.01m precision, ±327m range)
private static byte[] CompressHeights(float[] heights, float baseHeight)
{
    var pkg = new ZPackage();
    pkg.Write(heights.Length);
    
    foreach (float h in heights)
    {
        if (h <= float.MinValue + 1f)
        {
            // No terrain marker
            pkg.Write(short.MinValue);
        }
        else
        {
            float delta = h - baseHeight;
            short encoded = (short)Mathf.Clamp(delta * 100f, short.MinValue + 1, short.MaxValue);
            pkg.Write(encoded);
        }
    }
    
    return Utils.Compress(pkg.GetArray());
}
```

### Implementation Steps

#### Step 1: Add Save Method to CustomTerrainZone

```csharp
public void SaveToTerrainComp()
{
    if (!_isInitialized) return;
    
    // Find TerrainComp for this zone
    Vector3 zoneCenter = ZoneOrigin + new Vector3(ZONE_SIZE * 0.5f, 0, ZONE_SIZE * 0.5f);
    TerrainComp tc = TerrainComp.FindTerrainCompiler(zoneCenter);
    
    if (tc == null)
    {
        Heightmap hmap = Heightmap.FindHeightmap(zoneCenter);
        if (hmap != null)
        {
            tc = hmap.GetAndCreateTerrainCompiler();
        }
    }
    
    if (tc == null)
    {
        Debug.LogWarning($"[CustomTerrainZone] Cannot save zone {ZoneId} - no TerrainComp found");
        return;
    }
    
    var nview = tc.GetComponent<ZNetView>();
    if (nview == null || !nview.IsValid() || !nview.IsOwner())
    {
        // Need RPC to save on owner
        return;
    }
    
    var zdo = nview.GetZDO();
    if (zdo == null) return;
    
    // Save data
    zdo.Set(ZDO_ZONE_VERSION, 1);
    zdo.Set(ZDO_ZONE_BASE_HEIGHT, _baseHeight);
    zdo.Set(ZDO_ZONE_HEIGHTS, CompressHeights(_heights, _baseHeight));
    zdo.Set(ZDO_ZONE_TERRAIN_MASK, PackTerrainMask(_hasTerrainAt));
    zdo.Set(ZDO_ZONE_PAINT_MASK, CompressPaintMask(_paintMask));
    
    Debug.Log($"[CustomTerrainZone] Saved zone {ZoneId} to TerrainComp");
}
```

#### Step 2: Add Load Method

```csharp
public static CustomTerrainZone LoadFromTerrainComp(TerrainComp tc, Vector2i zoneId)
{
    var nview = tc.GetComponent<ZNetView>();
    if (nview == null || !nview.IsValid()) return null;
    
    var zdo = nview.GetZDO();
    if (zdo == null) return null;
    
    int version = zdo.GetInt(ZDO_ZONE_VERSION, 0);
    if (version == 0) return null; // No zone data
    
    // Create zone
    var zoneObj = new GameObject($"CustomTerrainZone_{zoneId.x}_{zoneId.y}");
    var zone = zoneObj.AddComponent<CustomTerrainZone>();
    zone.InitializeFromSave(zoneId, zdo);
    
    return zone;
}

private void InitializeFromSave(Vector2i zoneId, ZDO zdo)
{
    ZoneId = zoneId;
    ZoneOrigin = ZoneIdToWorld(zoneId);
    
    _baseHeight = zdo.GetFloat(ZDO_ZONE_BASE_HEIGHT, 0f);
    
    byte[] heightData = zdo.GetByteArray(ZDO_ZONE_HEIGHTS);
    if (heightData != null)
    {
        _heights = DecompressHeights(heightData, _baseHeight);
    }
    
    byte[] maskData = zdo.GetByteArray(ZDO_ZONE_TERRAIN_MASK);
    if (maskData != null)
    {
        _hasTerrainAt = UnpackTerrainMask(maskData);
    }
    
    byte[] paintData = zdo.GetByteArray(ZDO_ZONE_PAINT_MASK);
    if (paintData != null)
    {
        _paintMask = DecompressPaintMask(paintData);
    }
    
    // Setup mesh and other components
    SetupMeshComponents();
    RegenerateMesh();
    
    _isInitialized = true;
    s_allZones[ZoneId] = this;
}
```

#### Step 3: Add Harmony Patches

```csharp
[HarmonyPatch(typeof(TerrainComp), "Load")]
[HarmonyPostfix]
public static void TerrainComp_Load_Postfix(TerrainComp __instance, bool __result)
{
    if (!__result) return;
    
    Vector2i zoneId = CustomTerrainZone.WorldToZoneId(__instance.transform.position);
    
    // Check if zone data exists and load it
    CustomTerrainZone.LoadFromTerrainComp(__instance, zoneId);
}
```

#### Step 4: Add Save Triggers

Call `SaveToTerrainComp()` after any terrain modification:
- `LevelArea()` 
- `RaiseArea()`
- `SmoothArea()`
- `SlopeArea()`
- `PaintArea()`
- `DeleteArea()`

Add cooldown to batch rapid changes:
```csharp
private float _lastSaveTime;
private const float SAVE_COOLDOWN = 1f;

private void MarkDirtyAndQueueSave()
{
    _isDirty = true;
    
    // Queue save after cooldown
    if (Time.time - _lastSaveTime > SAVE_COOLDOWN)
    {
        SaveToTerrainComp();
        _lastSaveTime = Time.time;
    }
}
```

### Testing Checklist

- [ ] Create terrain with Infinity Hammer ? logout ? login ? terrain still exists
- [ ] Paint terrain ? logout ? login ? paint still exists  
- [ ] Level terrain ? logout ? login ? heights still correct
- [ ] Zone unload (walk away) ? zone reload (walk back) ? terrain intact
- [ ] Multiplayer: Host creates terrain ? client sees it
- [ ] Multiplayer: Client creates terrain ? host and other clients see it
- [ ] Multiple zones ? all persist independently
- [ ] Large zones ? compression keeps ZDO size reasonable

---

## ?? PAINT SEAM ISSUE: Per-Disk Paint Masks Create Visible Boundaries

### Problem Description

When painting custom terrain, visible seams appear at disk boundaries because:
1. Each `DirtFloorController` has its own circular paint mask texture
2. Disks have different local UV coordinate spaces
3. When the combiner blends them in `UpdateCombinedPaintMask()`, boundaries are visible
4. Paint operations hit individual disk masks which don't align properly

**Visual Evidence:** Paved stone shows clean edges within a disk but grass/default shows disk boundaries

### Root Cause

In `DirtFloorMeshCombiner.Paint.cs`:
```csharp
private void UpdateCombinedPaintMask()
{
    // For each pixel in combined mask...
    foreach (var disk in _connectedDisks)
    {
        // Sample from each disk's individual paint mask
        Color diskColor = SampleDiskPaintColor(disk, worldPos);
        blendedColor += diskColor * weight;
        totalWeight += weight;
    }
    // Result: Blended but still shows disk boundaries
}
```

The problem is that each disk has:
- Its own `Texture2D _paintMask` (typically 64x64 or 128x128)
- Its own local coordinate space (centered on disk position)
- Paint applied to the disk's circular area only

### Solution: Combiner-Level Paint Mask

Instead of per-disk paint masks, use **ONE paint mask per combiner** that:
1. Covers the entire combiner's world bounds
2. Uses consistent world-space UV mapping
3. Paint operations directly modify this single texture
4. No blending needed - single source of truth

### Implementation Plan

#### Step 1: Add Combiner-Level Paint Storage

```csharp
// In DirtFloorMeshCombiner
private Texture2D _combinerPaintMask;  // Single paint mask for entire combiner
private const int COMBINER_PAINT_RESOLUTION = 256;  // Higher res for larger areas

// World-space bounds for UV mapping
private Bounds _paintBounds;

public void InitializePaintMask()
{
    _combinerPaintMask = new Texture2D(
        COMBINER_PAINT_RESOLUTION, 
        COMBINER_PAINT_RESOLUTION, 
        TextureFormat.RGBA32, 
        false
    );
    _combinerPaintMask.wrapMode = TextureWrapMode.Clamp;
    _combinerPaintMask.filterMode = FilterMode.Bilinear;
    
    // Initialize to "no paint" (grass)
    Color[] pixels = new Color[COMBINER_PAINT_RESOLUTION * COMBINER_PAINT_RESOLUTION];
    for (int i = 0; i < pixels.Length; i++)
        pixels[i] = Heightmap.m_paintMaskNothing;
    _combinerPaintMask.SetPixels(pixels);
    _combinerPaintMask.Apply();
    
    _paintBounds = _combinedBounds;
}
```

#### Step 2: Direct Paint Operations

```csharp
/// <summary>
/// Paints directly to the combiner's paint mask.
/// No per-disk masks involved.
/// </summary>
public void PaintAtWorldPosition(Vector3 worldPos, float radius, DirtFloorController.PaintType paintType)
{
    if (_combinerPaintMask == null) return;
    
    Color paintColor = GetPaintColor(paintType);
    
    // Convert world position to texture UV
    float u = (worldPos.x - _paintBounds.min.x) / _paintBounds.size.x;
    float v = (worldPos.z - _paintBounds.min.z) / _paintBounds.size.z;
    
    // Convert to pixel coordinates
    int centerX = Mathf.RoundToInt(u * COMBINER_PAINT_RESOLUTION);
    int centerY = Mathf.RoundToInt(v * COMBINER_PAINT_RESOLUTION);
    
    // Convert radius to pixels
    float pixelRadius = (radius / _paintBounds.size.x) * COMBINER_PAINT_RESOLUTION;
    
    // Paint circular area
    int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - pixelRadius));
    int maxX = Mathf.Min(COMBINER_PAINT_RESOLUTION - 1, Mathf.CeilToInt(centerX + pixelRadius));
    int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - pixelRadius));
    int maxY = Mathf.Min(COMBINER_PAINT_RESOLUTION - 1, Mathf.CeilToInt(centerY + pixelRadius));
    
    Color[] pixels = _combinerPaintMask.GetPixels();
    
    for (int y = minY; y <= maxY; y++)
    {
        for (int x = minX; x <= maxX; x++)
        {
            float dist = Mathf.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
            if (dist <= pixelRadius)
            {
                // Check if this pixel is over actual terrain (not a gap)
                Vector3 pixelWorldPos = new Vector3(
                    _paintBounds.min.x + (x / (float)COMBINER_PAINT_RESOLUTION) * _paintBounds.size.x,
                    worldPos.y,
                    _paintBounds.min.z + (y / (float)COMBINER_PAINT_RESOLUTION) * _paintBounds.size.z
                );
                
                if (IsOverTerrain(pixelWorldPos))
                {
                    pixels[y * COMBINER_PAINT_RESOLUTION + x] = paintColor;
                }
            }
        }
    }
    
    _combinerPaintMask.SetPixels(pixels);
    _combinerPaintMask.Apply();
    
    // Update material
    if (_terrainMaterial != null)
        _terrainMaterial.SetTexture(s_ClearedMaskTexProperty, _combinerPaintMask);
}

private bool IsOverTerrain(Vector3 worldPos)
{
    // Check height grid to see if there's terrain here
    WorldToGrid(worldPos, out int gx, out int gz);
    if (gx < 0 || gx >= _gridWidth || gz < 0 || gz >= _gridHeight)
        return false;
    
    int idx = gz * _gridWidth + gx;
    return idx >= 0 && idx < _heights.Length && _heights[idx] > -50f;
}
```

#### Step 3: Update Paint Routing

```csharp
// In DirtFloorTerrainManager.PaintArea()
public static void PaintArea(Vector3 center, float radius, DirtFloorController.PaintType paintType)
{
    // Find all combiners that overlap this paint area
    var combiners = DirtFloorMeshCombiner.GetCombinersInRadius(center, radius);
    
    foreach (var combiner in combiners)
    {
        // Paint directly to combiner's mask - no per-disk operations!
        combiner.PaintAtWorldPosition(center, radius, paintType);
    }
}
```

#### Step 4: Handle Bounds Changes

When disks are added/removed from a combiner, the paint mask may need resizing:

```csharp
public void OnBoundsChanged(Bounds newBounds)
{
    if (_combinerPaintMask == null)
    {
        _paintBounds = newBounds;
        InitializePaintMask();
        return;
    }
    
    // If bounds expanded significantly, need to resize paint mask
    if (!_paintBounds.Contains(newBounds.min) || !_paintBounds.Contains(newBounds.max))
    {
        // Create new larger mask
        Texture2D newMask = new Texture2D(COMBINER_PAINT_RESOLUTION, COMBINER_PAINT_RESOLUTION, TextureFormat.RGBA32, false);
        
        // Copy old paint data to new mask (with UV remapping)
        CopyPaintMaskWithRemap(_combinerPaintMask, _paintBounds, newMask, newBounds);
        
        // Swap
        UnityEngine.Object.Destroy(_combinerPaintMask);
        _combinerPaintMask = newMask;
        _paintBounds = newBounds;
    }
}
```

#### Step 5: Persistence

Save the combiner paint mask to TerrainComp ZDO:

```csharp
// ZDO key for combiner paint mask
private static readonly int ZDO_COMBINER_PAINT = "va_combiner_paint".GetStableHashCode();

public byte[] GetPaintMaskData()
{
    if (_combinerPaintMask == null) return null;
    return _combinerPaintMask.EncodeToPNG();
}

public void LoadPaintMaskData(byte[] data)
{
    if (data == null || data.Length == 0) return;
    
    if (_combinerPaintMask == null)
        InitializePaintMask();
    
    _combinerPaintMask.LoadImage(data);
}
```

### Benefits of This Approach

1. **No seams** - Single texture = no blending artifacts
2. **Consistent resolution** - Same detail level across entire combiner
3. **Simpler code** - No need for complex multi-disk sampling
4. **Better performance** - One texture lookup instead of N disk lookups
5. **Cleaner persistence** - One PNG per combiner instead of per disk

### Migration Path

1. Keep per-disk paint masks for backwards compatibility
2. On combiner creation, merge disk paint masks into combiner mask
3. Paint operations go to combiner mask only
4. Eventually deprecate per-disk paint storage

### Testing Checklist

- [ ] Paint paved stone ? no visible seams at disk boundaries
- [ ] Paint cultivated ? smooth gradient across disks
- [ ] Paint dirt path ? consistent appearance
- [ ] Paint across multiple combiners ? seams only at combiner boundaries (acceptable)
- [ ] Add new disk to combiner ? existing paint preserved
- [ ] Remove disk from combiner ? paint outside removed area preserved
- [ ] Persistence ? paint survives logout/login



