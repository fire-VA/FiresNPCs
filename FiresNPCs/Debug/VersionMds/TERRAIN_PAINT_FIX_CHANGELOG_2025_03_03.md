# Terrain Paint System Fix Changelog - 2025-03-03/04

## Overview
This document tracks all changes made to fix the custom terrain paint and grass spawning system.
Changes are documented in detail to allow reverting if needed.

---

## Problem Statement

The custom terrain system had fundamental architectural issues:
1. Paint stored in THREE separate locations causing inconsistency
2. Visible seams at disk boundaries when paint is blended
3. Grass spawning ignoring biome overlay paint (grass in Ashlands)
4. No cleanup when overlapping disks are placed
5. Paint not persisting after zone reload
6. **CRITICAL**: Paint being cleared when new terrain is added (fixed 2025-03-04)

---

## Architecture Evolution

### v1 - Per-Disk Paint (Original)
- Each DirtFloorController had its own paint mask texture
- Seams at disk boundaries, difficult to persist

### v2 - Per-Combiner Paint (2025-03-03)
- Single paint mask per DirtFloorMeshCombiner
- Remapping on bounds change caused paint loss
- Still had seams at combiner boundaries

### v3 - Zone-Level Paint (2025-03-04) ? CURRENT
- Single paint mask per 64m zone (matches Valheim's TerrainComp zones)
- Custom terrain defines WHERE paint can appear (the "canvas")
- Paint operations write directly to zone mask
- Adding terrain extends canvas without affecting existing paint
- **No seams, no paint loss**

---

## Changes Made

### Change 1: Connect Biome Paint to Grass Spawning

**File:** `FiresNPCs\Modules\CustomTerrain\DirtFloorMeshCombiner.Grass.cs`

**Purpose:** Prevent grass from spawning in biome overlays that shouldn't have grass (Ashlands, Mountain, DeepNorth)

**Lines Added (after line 14):**
```csharp
/// <summary>
/// Determines if a biome should have grass.
/// Used to prevent grass spawning in biome overlay painted areas.
/// </summary>
private bool BiomeShouldHaveGrass(Heightmap.Biome biome)
{
    return biome switch
    {
        Heightmap.Biome.AshLands => false,   // Lava/ash, no grass
        Heightmap.Biome.DeepNorth => false,  // Snow, no grass
        Heightmap.Biome.Mountain => false,   // Snow/rock, no grass
        _ => true                            // Other biomes have grass
    };
}
```

**Lines Modified (in SpawnGrassOnMesh):**
Added biome paint check before spawning:
```csharp
// Check biome overlay paint - no grass in certain biomes (Ashlands, Mountain, etc.)
var biomePaint = BiomePaintPersistence.GetPaintAtPosition(worldPos.x, worldPos.z);
if (biomePaint != null && !BiomeShouldHaveGrass(biomePaint.Biome))
    continue;
```

**Revert Instructions:**
1. Remove the `BiomeShouldHaveGrass()` method
2. Remove the biome paint check in `SpawnGrassOnMesh()`

---

### Change 2: Add Combiner Paint Persistence to TerrainComp ZDO

**File:** `FiresNPCs\Modules\CustomTerrain\DirtFloorMeshCombiner.Paint.cs`

**Purpose:** Save/load combiner paint mask to TerrainComp ZDO for persistence across zone reloads

**Added Region (after #region Paint Mask State):**
```csharp
#region Paint Persistence

// ZDO key for combiner paint mask (stored in TerrainComp ZDO)
private static readonly int ZDO_COMBINER_PAINT_PREFIX = "va_combiner_paint_".GetStableHashCode();

/// Timer for batched paint saves.
private float _paintSaveTimer = 0f;
private const float PAINT_SAVE_DELAY = 2f;

/// <summary>
/// Saves the combiner's paint mask to TerrainComp ZDO.
/// </summary>
public void SavePaintMaskToTerrainComp()
{
    // ... full implementation
}

/// <summary>
/// Loads the combiner's paint mask from TerrainComp ZDO.
/// </summary>
public bool LoadPaintMaskFromTerrainComp()
{
    // ... full implementation
}

/// <summary>
/// Gets a unique identifier for this combiner based on its position.
/// </summary>
private string GetCombinerId()
{
    int x = Mathf.RoundToInt(_combinedBounds.center.x);
    int z = Mathf.RoundToInt(_combinedBounds.center.z);
    return $"{x}_{z}";
}

/// <summary>
/// Called during Update to check for pending paint saves.
/// </summary>
private void UpdatePaintSave()
{
    if (!_paintMaskDirty) return;
    
    _paintSaveTimer += Time.deltaTime;
    if (_paintSaveTimer >= PAINT_SAVE_DELAY)
    {
        _paintSaveTimer = 0f;
        SavePaintMaskToTerrainComp();
    }
}

/// <summary>
/// Marks the paint mask as dirty and queues a save.
/// </summary>
public void MarkPaintDirty()
{
    _paintMaskDirty = true;
    _paintSaveTimer = 0f;
}

#endregion
```

**Modified SetupMaterial() to load saved paint:**
```csharp
// Try to load saved paint from TerrainComp first
bool loadedFromSave = LoadPaintMaskFromTerrainComp();

if (!loadedFromSave)
{
    // No saved paint - try migration from disk masks or start fresh
    UpdateCombinedPaintMask();
}
```

**Modified PaintAtWorldPosition() to mark dirty:**
```csharp
// Mark for batched save
MarkPaintDirty();
```

**Revert Instructions:**
1. Remove the entire `#region Paint Persistence` section
2. Revert `SetupMaterial()` to not call `LoadPaintMaskFromTerrainComp()`
3. Revert `PaintAtWorldPosition()` to set `_paintMaskDirty = true` directly

---

### Change 3: Add Paint Clearing on Disk Overlap

**File:** `FiresNPCs\Modules\CustomTerrain\DirtFloorMeshCombiner.Paint.cs`

**Purpose:** Clear paint in overlap region when new disks are added to prevent old paint bleeding through

**Added Method (after ClearPaintAndRespawnGrass):**
```csharp
/// <summary>
/// Clears paint in a circular region (used when adding overlapping disks).
/// </summary>
public void ClearPaintInRegion(Vector3 center, float radius)
{
    // ... full implementation
}
```

**Revert Instructions:**
1. Remove the `ClearPaintInRegion()` method

---

### Change 4: ~~Call ClearPaintInRegion When Adding Overlapping Disks~~ REVERTED

**File:** `FiresNPCs\Modules\CustomTerrain\DirtFloorMeshCombiner.DiskManagement.cs`

**Status:** REVERTED on 2025-03-04

**Original Purpose:** Automatically clear paint in overlap region when disks overlap

**Why Reverted:** This was causing paint to be cleared every time the user clicked to add terrain,
which effectively removed all previously painted areas. The user would paint an area, then click
elsewhere to extend terrain, and all their paint would disappear.

**Current Behavior:** 
- Paint is PRESERVED when adding new disks
- `RemapPaintMaskToNewBounds()` handles UV remapping to keep paint in correct world positions
- No automatic clearing - paint only changes when user explicitly paints

**The `ClearPaintInRegion()` method still exists** for use in other scenarios where explicit
clearing is needed, but it's no longer called automatically on disk overlap.

---

### Change 5: Add UpdatePaintSave to Update Loop

**File:** `FiresNPCs\Modules\CustomTerrain\DirtFloorMeshCombiner.Lifecycle.cs`

**Purpose:** Check for pending paint saves during Update

**Added line in Update() method (after regeneration check):**
```csharp
// Check for pending paint saves (batched)
UpdatePaintSave();
```

**Revert Instructions:**
1. Remove the `UpdatePaintSave();` line from Update()

---

## Files Modified Summary

| File | Type of Change |
|------|----------------|
| `DirtFloorMeshCombiner.Grass.cs` | Added biome grass check |
| `DirtFloorMeshCombiner.Paint.cs` | Added persistence + overlap clearing |
| `DirtFloorMeshCombiner.DiskManagement.cs` | Call clear on overlap |
| `DirtFloorMeshCombiner.Lifecycle.cs` | Added save timer to Update |
| `DirtFloorTerrainCompStorage.cs` | Added combiner paint restore on zone load |

---

## Testing Checklist

- [ ] Paint biome overlay (Ashlands) - grass should NOT spawn
- [ ] Place overlapping disks - old paint should be cleared
- [ ] Paint paved stone - should persist after logout/login
- [ ] Zone reload - paint should be restored
- [ ] Multiplayer (host) - paint should sync to clients

---

## Known Limitations

1. **RPC not implemented** - Clients cannot save paint directly (ownership check fails)
2. **Per-disk paint still exists** - Not removed yet, causes some redundancy
3. **Combiner ID based on position** - Moving combiner would break paint restore

---

## Change 6: Add Combiner Paint Restore on Zone Load

**File:** `FiresNPCs\Modules\CustomTerrain\DirtFloorTerrainCompStorage.cs`

**Purpose:** Automatically restore combiner paint masks when a zone loads

**Added to TerrainComp_Load_Postfix:**
```csharp
// Also restore combiner paint masks for this zone
RestoreCombinerPaintMasks(__instance);
```

**Added new region (Combiner Paint Mask Restoration):**
- `RestoreCombinerPaintMasks()` - Queues paint restore for a TerrainComp
- `_pendingPaintRestores` - Queue for pending paint restores
- `ProcessPendingPaintRestores()` - Processes queued paint restores

**Modified DirtFloorRestoreHelper.Update():**
```csharp
DirtFloorTerrainCompStorage.ProcessPendingRestores();
DirtFloorTerrainCompStorage.ProcessPendingPaintRestores();  // Added
```

**Revert Instructions:**
1. Remove the `RestoreCombinerPaintMasks(__instance);` call from `TerrainComp_Load_Postfix`
2. Remove the entire `#region Combiner Paint Mask Restoration` section
3. Remove `ProcessPendingPaintRestores()` call from `DirtFloorRestoreHelper.Update()`

---

## Future Work

1. Add RPC support for multiplayer client paint saves
2. Remove per-disk paint storage from `DirtFloorController`
3. Add migration tool to convert old per-disk paint to combiner format
4. Consider zone-level paint mask instead of per-combiner

---

## Rollback Procedure

To fully revert all changes:

1. **DirtFloorMeshCombiner.Grass.cs**
   - Remove `BiomeShouldHaveGrass()` method
   - Remove biome paint check in `SpawnGrassOnMesh()`

2. **DirtFloorMeshCombiner.Paint.cs**
   - Remove `#region Paint Persistence` section
   - Remove `ClearPaintInRegion()` method
   - Revert `SetupMaterial()` initialization (remove `LoadPaintMaskFromTerrainComp()` call)
   - Revert `PaintAtWorldPosition()` to use `_paintMaskDirty = true`

3. **DirtFloorMeshCombiner.DiskManagement.cs**
   - Remove `isOverlapping` variable
   - Remove overlap clearing block in `AddDisk()`

4. **DirtFloorMeshCombiner.Lifecycle.cs**
   - Remove `UpdatePaintSave();` from Update()

5. **DirtFloorTerrainCompStorage.cs**
   - Remove `RestoreCombinerPaintMasks(__instance);` from `TerrainComp_Load_Postfix`
   - Remove `#region Combiner Paint Mask Restoration` section
   - Remove `ProcessPendingPaintRestores()` call from `DirtFloorRestoreHelper.Update()`

---

## Change 7: Zone-Level Paint System (2025-03-04)

**New File:** `FiresNPCs\Modules\CustomTerrain\ZonePaintMaskManager.cs`

**Purpose:** Single source of truth for paint data, one texture per 64m zone.
This is the **correct fix** for paint loss when adding terrain.

**Key Features:**
- `ZONE_SIZE = 64f` - matches Valheim's TerrainComp zones
- `PAINT_MASK_SIZE = 256` - 0.25m per pixel resolution
- `PaintAtWorldPosition()` - paints to zone mask, optionally only over terrain
- `GetPaintAtWorldPosition()` - samples paint color at world position
- `SaveDirtyZones()` / `TryLoadFromTerrainComp()` - persistence via TerrainComp ZDO
- Paint operations span multiple zones seamlessly

**Modified `DirtFloorTerrainManager.PaintArea()`:**
- Now calls `ZonePaintMaskManager.PaintAtWorldPosition()` instead of combiner methods
- Passes callback to check if position is over custom terrain
- Notifies combiners to refresh via `RefreshPaintFromZone()`

**Added `DirtFloorMeshCombiner.RefreshPaintFromZone()`:**
- Samples from zone paint mask to update combiner's material texture
- Only updates pixels that are over actual terrain

**Why This Architecture Works:**
```
BEFORE (per-combiner):
???????????   ???????????
?Combiner1?   ?Combiner2?  <- Each has own paint mask
? Paint A ?   ? Paint B ?  <- Bounds change = remap = loss
???????????   ???????????

AFTER (zone-level):
???????????????????????????
?     Zone Paint Mask     ?  <- Single source of truth (64m)
?  ???????     ???????    ?
?  ?Comb1?     ?Comb2?    ?  <- Combiners just sample from zone
?  ???????     ???????    ?
???????????????????????????
```

- Adding terrain = extending the "paintable area" (the canvas)
- Existing paint = preserved in zone mask (never touched by terrain ops)
- Combiners = just render what the zone mask says

**Files Added:**
- `ZonePaintMaskManager.cs` - Zone-level paint storage and operations

**Files Modified:**
- `DirtFloorTerrainManager.cs` - Route paint ops to zone manager
- `DirtFloorMeshCombiner.Paint.cs` - Added `RefreshPaintFromZone()`

**Revert Instructions:**
1. Delete `ZonePaintMaskManager.cs`
2. Revert `DirtFloorTerrainManager.PaintArea()` to call `DirtFloorMeshCombiner.PaintAllLayersAt()`
3. Remove `RefreshPaintFromZone()` from `DirtFloorMeshCombiner.Paint.cs`
