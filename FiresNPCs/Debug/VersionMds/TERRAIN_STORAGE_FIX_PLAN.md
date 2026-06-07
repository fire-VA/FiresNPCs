# Custom Terrain - Storage Architecture Fix Plan

## Date: 2025-03-03

## Executive Summary

The custom terrain system has **THREE separate paint storage systems** that don't communicate properly, causing visible seams at disk boundaries and inconsistent grass spawning.

---

## Current Broken Architecture

### Storage Location 1: Per-Disk ZDO (PRIMARY)

**File:** `DirtFloorController.cs`
**ZDO Key:** `dirtfloor_paintmask`
**Storage:** Each disk has its own 32x32 paint mask texture stored as RGBA bytes

```csharp
// DirtFloorController.SaveToZDO() - Line ~300
_nview.GetZDO().Set(ZDO_PaintMask, bytes); // Per-disk!
```

**Problem:** When disks overlap, their paint masks don't blend. Each disk has its own UV space.

### Storage Location 2: TerrainComp ZDO (REDUNDANT)

**File:** `DirtFloorTerrainCompStorage.cs`
**ZDO Key:** `va_custom_disks`
**Storage:** Disk catalog including paint mask bytes, stored in TerrainComp ZDO

```csharp
// Called from DirtFloorController.SaveToZDO()
DirtFloorTerrainCompStorage.SaveDisk(this);
```

**Problem:** This is a copy of per-disk data, not a unified paint mask.

### Storage Location 3: Combiner Paint Mask (RUNTIME ONLY)

**File:** `DirtFloorMeshCombiner.Paint.cs`
**Storage:** `_combinedPaintMask` - 256x256 texture covering combiner bounds

**Problem:** Not persisted! Lost on zone reload. Tries to composite from per-disk masks.

### Storage Location 4: Biome Paint (SEPARATE SYSTEM)

**File:** `BiomePaintPersistence.cs`
**ZDO Key:** `va_biome_paints`
**Storage:** List of circles in TerrainComp ZDO

**Problem:** NOT connected to grass spawning! Grass still spawns even in Ashlands overlay.

---

## The Correct Architecture

### Single Paint Storage: Combiner-Level in TerrainComp ZDO

```
TerrainComp ZDO (per 64m zone)
??? va_biome_paints      ? Biome overlays (existing, working)
??? va_clutter_paints    ? Clutter spawns (existing, working)
??? va_custom_disks      ? Disk catalog (position, radius, grass type)
??? va_combiner_paint    ? NEW: Combined paint mask for all disks in zone
```

### Remove Per-Disk Paint Storage

1. **Remove** `DirtFloorController._paintMask`
2. **Remove** `DirtFloorController.SaveToZDO(ZDO_PaintMask)` 
3. **Remove** `DirtFloorController.LoadPaintMaskFromZDO()`
4. **Keep** `DirtFloorController` for other properties (position, radius, grass type)

### Paint Flow After Fix

```
User paints terrain
    ?
DirtFloorMeshCombiner.PaintAtWorldPosition(worldPos, radius, paintType)
    ?
Write directly to _combinedPaintMask (world-space UV)
    ?
Queue save to TerrainComp ZDO
    ?
On zone load: Load _combinedPaintMask from TerrainComp ZDO
```

---

## Implementation Steps

### Step 1: Add Combiner Paint Persistence (NEW)

**File:** `DirtFloorMeshCombiner.Paint.cs`

```csharp
// New ZDO key
private static readonly int ZDO_COMBINER_PAINT = "va_combiner_paint".GetStableHashCode();

public void SavePaintMaskToTerrainComp()
{
    if (_combinedPaintMask == null) return;
    
    // Find TerrainComp for our center
    TerrainComp tc = TerrainComp.FindTerrainCompiler(_combinedBounds.center);
    if (tc == null) return;
    
    var nview = tc.GetComponent<ZNetView>();
    if (nview == null || !nview.IsOwner()) return;
    
    // Save as PNG
    byte[] pngData = _combinedPaintMask.EncodeToPNG();
    
    // Also save bounds for UV mapping
    var pkg = new ZPackage();
    pkg.Write(1); // Version
    pkg.Write(_combinedBounds.center);
    pkg.Write(_combinedBounds.size);
    pkg.Write(pngData);
    
    nview.GetZDO().Set(ZDO_COMBINER_PAINT, Utils.Compress(pkg.GetArray()));
}

public void LoadPaintMaskFromTerrainComp()
{
    TerrainComp tc = TerrainComp.FindTerrainCompiler(_combinedBounds.center);
    if (tc == null) return;
    
    var nview = tc.GetComponent<ZNetView>();
    if (nview == null) return;
    
    byte[] data = nview.GetZDO().GetByteArray(ZDO_COMBINER_PAINT);
    if (data == null) return;
    
    byte[] decompressed = Utils.Decompress(data);
    var pkg = new ZPackage(decompressed);
    
    int version = pkg.ReadInt();
    Vector3 savedCenter = pkg.ReadVector3();
    Vector3 savedSize = pkg.ReadVector3();
    byte[] pngData = pkg.ReadByteArray();
    
    // Recreate paint mask
    if (_combinedPaintMask == null)
        InitializeDirectPaintMask();
    
    _combinedPaintMask.LoadImage(pngData);
}
```

### Step 2: Remove Per-Disk Paint Storage

**File:** `DirtFloorController.cs`

Remove:
- `private Texture2D _paintMask;`
- `private static readonly int ZDO_PaintMask = "dirtfloor_paintmask".GetStableHashCode();`
- `SaveToZDO()` paint mask saving
- `LoadPaintMaskFromZDO()`
- `EnsurePaintMaskExists()`
- All references to `_paintMask`

Keep:
- `DirtRadius`, `GrassType`, `HeightOffset` - these are per-disk properties
- Basic state save/load for non-paint data

### Step 3: Route Paint Through Combiner

**File:** `DirtFloorController.cs`

```csharp
// OLD:
public bool PaintAt(Vector3 worldPosition, PaintType paintType, float radius)
{
    EnsurePaintMaskExists();
    // ... paint to _paintMask
}

// NEW:
public bool PaintAt(Vector3 worldPosition, PaintType paintType, float radius)
{
    var combiner = DirtFloorMeshCombiner.GetCombinerContaining(this);
    if (combiner != null)
    {
        combiner.PaintAtWorldPosition(worldPosition, radius, paintType, 1f);
        return true;
    }
    return false;
}
```

### Step 4: Connect Biome Paint to Grass Spawning

**File:** `DirtFloorMeshCombiner.Grass.cs`

```csharp
// In SpawnGrassOnMesh(), before spawning:
var biomePaint = BiomePaintPersistence.GetPaintAtPosition(worldPos.x, worldPos.z);
if (biomePaint != null)
{
    // Check if this biome should have grass
    if (!BiomeShouldHaveGrass(biomePaint.Biome))
        continue; // Skip grass spawn
}
```

Add helper:
```csharp
private bool BiomeShouldHaveGrass(Heightmap.Biome biome)
{
    return biome switch
    {
        Heightmap.Biome.AshLands => false,   // Lava, no grass
        Heightmap.Biome.DeepNorth => false,  // Snow, no grass
        Heightmap.Biome.Mountain => false,   // Snow, no grass
        _ => true                            // Other biomes have grass
    };
}
```

### Step 5: Clear Paint on Disk Overlap

**File:** `DirtFloorMeshCombiner.DiskManagement.cs`

In `AddDisk()`, after bounds update:
```csharp
// Clear paint in overlap region
ClearPaintInRegion(newDisk.transform.position, newDisk.DirtRadius);

// Clear and respawn grass
ClearGrass();
SpawnGrassOnMesh();
```

---

## Migration Path

### Phase 1: Keep Both Systems Temporarily
- Add combiner paint persistence
- Keep per-disk paint for backwards compatibility
- On load: prefer combiner paint, fall back to per-disk

### Phase 2: Migrate Existing Data
- On zone load with per-disk paint but no combiner paint:
  - Composite per-disk paints into combiner paint
  - Save new combiner paint
  - Mark migration complete

### Phase 3: Remove Per-Disk Paint
- After sufficient migration time
- Remove per-disk paint code
- Clean up `DirtFloorController`

---

## Testing Checklist

- [ ] Paint persists after logout/login
- [ ] No seams at disk boundaries
- [ ] Grass respects biome overlay (no grass in Ashlands)
- [ ] Paint clears when disks overlap
- [ ] Multiplayer: paint syncs to other players
- [ ] Zone reload preserves all paint

---

## Files to Modify

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.Paint.cs` | Add `Save/LoadPaintMaskFromTerrainComp()` |
| `DirtFloorMeshCombiner.DiskManagement.cs` | Clear paint on disk overlap |
| `DirtFloorMeshCombiner.Grass.cs` | Check biome paint before spawning |
| `DirtFloorController.cs` | Route paint to combiner, remove local paint mask |
| `DirtFloorTerrainCompStorage.cs` | Update to use combiner paint |
