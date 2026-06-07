# Terrain Paint Debug - Disk Combination Flow

## Date: 2026-03-05
## Status: ?? TARGETED LOGGING ADDED

---

## Problem Summary

- Paint works **perfectly** on a single disk
- When disks **combine**, paint breaks:
  - Paint gets wiped/lost
  - Paint doesn't apply correctly to combined mesh
  - Visual artifacts when painting over combined terrain

---

## Logging Added

Three log prefixes to trace the flow:

### `[COMBINER]` - Disk addition flow
Traces when disks are added and combined:
```
[COMBINER] AddDisk START: disk at (x,y,z), radius=R. Combiner has N disks, bounds=..., hasPaint=true/false
[COMBINER] AddDisk after RegenerateMesh: bounds=..., grid=WxH, heights=count
[COMBINER] AddDisk END: now has N disks
```

### `[COMBINER REGEN]` - Mesh regeneration flow
Traces what happens during mesh rebuild:
```
[COMBINER REGEN] RegenerateMesh START: disks=N, restoredFromCache=true/false
[COMBINER REGEN] SKIPPING - restored from cache  <-- THIS IS LIKELY THE BUG!
-- or --
[COMBINER REGEN] After CalculateCombinedBounds: bounds=...
[COMBINER REGEN] After SetupGrid: grid=WxH, cellSize=C, gridOrigin=...
[COMBINER REGEN] After SampleHeights: X/Y valid height values
[COMBINER REGEN] After BuildMesh: vertices=N
[COMBINER REGEN] COMPLETE
```

### `[COMBINER PAINT]` - Paint application flow
Traces what happens when paint is applied:
```
[COMBINER PAINT] PaintAtWorldPosition: pos=..., radius=R, type=Paved, bounds=..., disks=N
[COMBINER PAINT] Applied paint mask to material
-- or --
[COMBINER PAINT] No pixels were modified!
```

---

## Fix Applied

In `AddDisk()`, we now clear cache flags to force full regeneration:

```csharp
_connectedDisks.Add(disk);

// IMPORTANT: Clear cache flags to force full regeneration
_restoredFromCache = false;
_isFinalized = false;

RegenerateMesh();  // Now this won't skip
```

---

## Expected Log Output - WORKING

```
[COMBINER] AddDisk START: disk at (1647, 37, -114), radius=5. Combiner has 1 disks, bounds=(center...), hasPaint=true
[COMBINER REGEN] RegenerateMesh START: disks=2, restoredFromCache=false
[COMBINER REGEN] After CalculateCombinedBounds: bounds=(expanded...)
[COMBINER REGEN] After SetupGrid: grid=50x45, cellSize=0.5, gridOrigin=(...)
[COMBINER REGEN] After SampleHeights: 2000/2250 valid height values
[COMBINER REGEN] After BuildMesh: vertices=1800
[COMBINER REGEN] COMPLETE
[COMBINER] AddDisk after RegenerateMesh: bounds=(expanded...), grid=50x45, heights=2250
[COMBINER] AddDisk END: now has 2 disks

... later, when painting ...

[COMBINER PAINT] PaintAtWorldPosition: pos=(1645, 37, -115), radius=4.5, type=Paved, bounds=(expanded...), disks=2
[COMBINER PAINT] Applied paint mask to material
```

## Expected Log Output - BROKEN (before fix)

```
[COMBINER] AddDisk START: disk at (1647, 37, -114), radius=5. Combiner has 1 disks, bounds=(old...), hasPaint=true
[COMBINER REGEN] RegenerateMesh START: disks=2, restoredFromCache=true  <-- BUG!
[COMBINER REGEN] SKIPPING - restored from cache  <-- Grid not updated!
[COMBINER] AddDisk after RegenerateMesh: bounds=(old...), grid=23x23, heights=529  <-- Still old size!
[COMBINER] AddDisk END: now has 2 disks

... later, when painting ...

[DirtFloorMeshCombiner] Paint FAILED at (1645, 37, -115): grid=(-2,5), gridSize=(23,23)  <-- Out of bounds!
```

---

## Files Modified

| File | Changes |
|------|---------|
| `DirtFloorMeshCombiner.DiskManagement.cs` | Added `[COMBINER]` logging to `AddDisk`, clears cache flags |
| `DirtFloorMeshCombiner.MeshGen.cs` | Added `[COMBINER REGEN]` logging to `RegenerateMesh` |
| `DirtFloorMeshCombiner.Paint.cs` | Added `[COMBINER PAINT]` logging to `PaintAtWorldPosition` |
| `DirtFloorMeshCombiner.Utilities.cs` | Cleaned up spam logging, only warns on failure |
| `DirtFloorMeshCombiner.Static.cs` | Cleaned up spam logging, only warns on failure |

---

## Testing Instructions

1. Load game
2. Place first disk with `place_dirtfloor 10`
3. Paint on it (should work)
4. Check logs for `[COMBINER]` entries
5. Place second overlapping disk with `place_dirtfloor 5`
6. Check logs - should see:
   - `[COMBINER] AddDisk START`
   - `[COMBINER REGEN] RegenerateMesh START` with `restoredFromCache=false`
   - Full regeneration logs (not SKIPPING)
7. Paint on combined terrain
8. Check logs for `[COMBINER PAINT]` success
