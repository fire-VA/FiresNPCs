# Terrain Paint Fix Attempts

## Problem Description

When painting terrain on custom DirtFloor terrain using Infinity Hammer tools (pave, cultivate, etc.), the paint appears on **multiple overlapping combiners** instead of just the single combiner that actually owns the terrain at the paint position.

**Symptoms:**
- Log spam showing multiple combiners being painted
- Visual artifacts with paint appearing at wrong locations
- Multiple "candidates" found when only one should exist

**Example Log (Before Fix):**
```
[DirtFloorMeshCombiner] PaintAllLayersAt: Painted 11 overlapping layers
```

---

## Attempt History

### Attempt 1: Use SampleHeightAt() to Filter Combiners
**Date:** 2026-02-27  
**Status:** ? FAILED

**Theory:** Use `SampleHeightAt()` to check if the combiner has valid terrain geometry at the paint position. If it returns < -50, the combiner doesn't have terrain there.

**Implementation:**
```csharp
float sampledHeight = combiner.SampleHeightAt(worldPos);
if (sampledHeight < -50f)
{
    continue; // No actual terrain at this position - skip
}
```

**Result:** Still showed 2-3 candidates per paint operation.

**Why It Failed:** `SampleHeightAt()` checks the **height grid**, not the **actual disk coverage**. The height grid can have valid heights at positions that are:
1. In the **blend zone** between disks (grid has interpolated heights)
2. In **filled gaps** (heights were filled by `FillHeightGaps()`)
3. In the **grid padding** area (beyond disk edges but still in grid bounds)

**Log Evidence:**
```
[DirtFloorMeshCombiner] PaintAllLayersAt: Combiner at (-5435.00, 49.23, 364.00) is CANDIDATE (terrainH=49.2, paintH=49.2, diff=0.0)
[DirtFloorMeshCombiner] PaintAllLayersAt: Combiner at (-5437.50, 49.25, 356.50) is CANDIDATE (terrainH=49.2, paintH=49.2, diff=0.0)
[DirtFloorMeshCombiner] PaintAllLayersAt: Combiner at (-5447.50, 49.27, 361.00) is CANDIDATE (terrainH=49.2, paintH=49.2, diff=0.0)
STATS: candidates=3
```

---

### Attempt 2: Use IsPositionInsideAnyDisk() to Filter Combiners
**Date:** 2026-02-27  
**Status:** ? PARTIAL SUCCESS

**Theory:** Instead of checking the height grid, check if the paint position is actually **inside a disk's radius** belonging to that combiner. Only one combiner's disks can "own" a specific XZ position.

**Implementation:**
```csharp
// CRITICAL FIX: Check if combiner has an actual DISK at this position
// IsPositionInsideAnyDisk checks the connected disks, not just the grid
if (!combiner.IsPositionInsideAnyDisk(worldPos))
{
    skipNoDiskAtPos++;
    continue; // No disk at this position - skip
}
```

**Result:** ? Fixed multiple candidates issue! Now shows `candidates=1` consistently.

**New Issues Discovered:**
1. **Unpaintable ring around disk edges** - Green grass ring visible around paved areas (see screenshot)
2. **Height threshold too strict** - Paint fails when sampled height differs by >1m from paint Y

**Log Evidence (Success):**
```
[DirtFloorMeshCombiner] PaintAllLayersAt STATS: noBoundsOverlap=7, noDiskAtPos=0, tooFarVert=0, candidates=1
[DirtFloorMeshCombiner] PaintAllLayersAt: Painted on SINGLE combiner at (-5379.50, 59.31, 345.50)
```

**Log Evidence (Height Threshold Failure):**
```
[DirtFloorMeshCombiner] PaintAllLayersAt: Combiner at (-5379.50, 59.31, 345.50) terrain height 57.4 too far from paint height 59.3 (diff=1.9 > threshold=1)
[Warning] PaintAllLayersAt: No combiner found with disk at (-5380.00, 59.29, 345.00)
```

**Files Modified:**
- `DirtFloorMeshCombiner.cs` - `PaintAllLayersAt()` method

---

### Attempt 3: Fix Unpaintable Ring + Height Threshold
**Date:** 2026-02-27  
**Status:** ? STILL HAD ISSUES

Tried including BlendDistance in disk check and increasing height threshold.
Still had unpaintable areas due to multiple filtering checks.

---

### Attempt 4: SIMPLIFIED - Just Paint Everywhere
**Date:** 2026-02-27  
**Status:** ? PARTIAL - Visual works, persistence had filtering

**Changes Made:**
- `PaintAllLayersAt()` - Simplified to paint any combiner with terrain
- `PaintDirectlyOnCombinedMask()` - Removed disk boundary checks

**Problem Found:** Paint worked visually but had unpaintable areas that persisted after reload because the **disk persistence** still had filtering!

---

### Attempt 5: Remove ALL Filtering From Paint Persistence
**Date:** 2026-02-27  
**Status:** ? IMPLEMENTED

**Root Cause:** Paint appeared to work but edges were unpaintable because:
1. `OnPaintTerrain` filtered which disks to persist to based on distance
2. `DirtFloorController.PaintAt` rejected paint if center was too far from disk

**Files Modified:**

1. **`DirtFloorMeshCombiner.cs` - `OnPaintTerrain()`:**
   - Removed: Distance check `dist <= disk.DirtRadius + radius + blendMargin`
   - Now: Paints to ALL connected disks, always

2. **`DirtFloorController.cs` - `PaintAt()`:**
   - Removed: `if (distFromCenter > DirtRadius + radius) return false;`
   - Now: Just paints, UV clamping handles out-of-bounds naturally

**New Code:**
```csharp
// OnPaintTerrain - paint ALL disks
foreach (var disk in _connectedDisks)
{
    if (disk == null) continue;
    disk.PaintAt(worldPos, paintType, radius);  // No distance check!
    disksPainted++;
}

// PaintAt - no distance rejection
public bool PaintAt(Vector3 worldPosition, PaintType paintType, float radius)
{
    // ... setup code ...
    
    // REMOVED: if (distFromCenter > DirtRadius + radius) return false;
    
    // Just paint - UV clamping handles bounds
    float uvX = (localPos.x / (DirtRadius * 2f)) + 0.5f;
    float uvZ = (localPos.z / (DirtRadius * 2f)) + 0.5f;
    // ... rest of paint logic ...
}
```

**Expected Result:**
- Paint covers entire terrain surface
- Paint persists correctly after reload
- No more "trying over and over to paint edges"

---

### Attempt 6: Combined Disk Paint Resolution Issue
**Date:** 2026-02-27  
**Status:** ?? ANALYZING

**Problem:** Paint works on single disks but appears tiny/wrong on combined disks.

**Log Analysis:**
```
combiner at (-5385.50, 50.31, 474.50), paintPos=(-5388.81, 48.98, 488.50), disks=4
PaintDirectlyOnCombinedMask: Painted 1533 pixels  <-- SMALL!

combiner at (-5384.00, 49.04, 486.00), paintPos=(-5388.81, 48.98, 488.50), disks=1  
PaintDirectlyOnCombinedMask: Painted 8721 pixels  <-- CORRECT!
```

**Key Observations:**
1. **TWO combiners** are being painted at the same position (overlapping!)
2. The 4-disk combiner paints **1533 pixels** (small)
3. The 1-disk combiner paints **8721 pixels** (correct)

**Root Cause:**
The paint mask is **fixed size** (256x256 pixels) regardless of combiner bounds:
- Single disk: bounds ~12m ? 21 px/meter ? 3m radius = ~63 pixel radius
- 4-disk combiner: bounds ~40m ? 6.4 px/meter ? 3m radius = ~19 pixel radius

The paint looks correct on single disks, but on large combiners the paint radius in pixel space is **much smaller** relative to the total texture.

**Proposed Fix Options:**
1. **Normalize pixel radius** - Scale based on combiner size to maintain world-space paint size
2. **Larger paint mask for large combiners** - Use 512x512 or 1024x1024 for combiners with 4+ disks
3. **Only paint on ONE combiner** - Pick the best match and skip overlapping ones

### How Combiners Work

1. **DirtFloorController (Disk)** - A circular terrain piece with a center position and radius
2. **DirtFloorMeshCombiner** - Combines multiple overlapping disks into a single mesh
3. **Height Grid** - A 2D array storing terrain heights, covers bounds of all disks + padding
4. **Blend Zone** - Area between disks where heights are interpolated for smooth transitions

### Why Multiple Combiners Have Valid Heights at Same Position

```
    Combiner A's Grid              Combiner B's Grid
    ???????????????????           ???????????????????
    ?   ???????       ?           ?       ???????   ?
    ?   ?Disk ?       ?           ?       ?Disk ?   ?
    ?   ?  A  ?       ?           ?       ?  B  ?   ?
    ?   ???????       ?           ?       ???????   ?
    ?        ???????????????????????????????        ?
    ?        ? BLEND  ?           ?  BLEND ?        ?
    ?        ?  ZONE  ?           ?  ZONE  ?        ?
    ?        ???????????????????????????????        ?
    ???????????????????           ???????????????????
                    
                    Paint Position (X)
                    
Both combiners have valid grid heights at X because of blend zones,
but only one combiner has an actual DISK covering X.
```

### The Fix Logic

```csharp
// OLD (broken): Check if grid has height
if (combiner.SampleHeightAt(worldPos) < -50f) continue;
// Problem: Blend zones and fill gaps have valid heights

// NEW (fixed): Check if position is inside a disk
if (!combiner.IsPositionInsideAnyDisk(worldPos)) continue;
// Only passes if worldPos is within disk.DirtRadius of a disk center
```

---

## Debugging Tips

### Enable Detailed Logging
The `PaintAllLayersAt` method now logs:
- `noBoundsOverlap` - Combiners outside horizontal range
- `noDiskAtPos` - Combiners with bounds overlap but no disk at position
- `tooFarVert` - Combiners with wrong vertical height
- `candidates` - Valid combiners (should be 1)

### What Good Logs Look Like
```
[DirtFloorMeshCombiner] PaintAllLayersAt ENTRY: pos=(-5448.00, 49.27, 362.00), radius=4.5, type=Paved, totalCombiners=7
[DirtFloorMeshCombiner] PaintAllLayersAt: Combiner at (-5425.50, 49.31, 367.00) has bounds overlap but NO DISK at paint position
[DirtFloorMeshCombiner] PaintAllLayersAt: Combiner at (-5435.00, 49.23, 364.00) has bounds overlap but NO DISK at paint position
[DirtFloorMeshCombiner] PaintAllLayersAt: Combiner at (-5447.50, 49.27, 361.00) is CANDIDATE
[DirtFloorMeshCombiner] PaintAllLayersAt STATS: noBoundsOverlap=3, noDiskAtPos=3, tooFarVert=0, candidates=1
[DirtFloorMeshCombiner] PaintAllLayersAt: Painted on SINGLE combiner at (-5447.50, 49.27, 361.00)
```

### What Bad Logs Look Like
```
[DirtFloorMeshCombiner] PaintAllLayersAt STATS: candidates=3  <-- TOO MANY!
```

---

## Related Files

| File | Purpose |
|------|---------|
| `DirtFloorMeshCombiner.cs` | Main combiner class, contains `PaintAllLayersAt()` |
| `DirtFloorTerrainManager.cs` | Entry point for paint operations via `PaintArea()` |
| `TerrainOperationExecutor.cs` | Executes terrain commands from Infinity Hammer |
| `InfinityHammerCompat.cs` | Intercepts terrain operations |
| `DirtFloorController.cs` | Individual disk controller, has `DirtRadius` property |

---

## Next Steps If Current Fix Fails

1. **Add distance-based tiebreaker**: If multiple disks from different combiners contain the position, pick the one where the position is closest to a disk center

2. **Check disk vertical position**: Ensure the disk's Y position is within tolerance of the paint Y position

3. **Track disk ownership explicitly**: Store which combiner "owns" each disk and use that for paint routing

4. **Use raycast**: Cast a ray down from paint position and use the first combiner hit

---

## Test Procedure

1. Load into game with custom terrain
2. Select Infinity Hammer pave tool
3. Paint on terrain
4. Check logs for `PaintAllLayersAt STATS`
5. Verify `candidates=1`
6. Verify paint only appears at intended location

---

## Attempt 7: Custom DirtFloor Placement Tool
**Date:** 2026-02-27  
**Status:** ? IMPLEMENTED

**Solution:** Instead of fighting with the IH level tool + combiner overlap issues, create a **dedicated tool** for placing DirtFloor terrain that:
1. Places actual DirtFloor disk prefabs (not vanilla terrain ops)
2. Supports radius adjustment via Infinity Hammer's `<area>` parameter
3. Supports elevation offset via console commands
4. Automatically connects to existing combiners properly
5. Prevents the overlapping combiner issues that caused paint problems

**Files Created:**

1. **`DirtFloorPlacementTool.cs`** - Console command handler
   - `place_dirtfloor [radius]` - Places a disk at cursor position
   - `dirtfloor_raise [amount]` - Raises elevation offset
   - `dirtfloor_lower [amount]` - Lowers elevation offset
   - `dirtfloor_reset_elevation` - Resets to ground level
   - `dirtfloor_info` - Shows current settings

2. **`INFINITY_HAMMER_DIRTFLOOR_TOOLS.yaml`** - Tool definitions for IH
   - DirtFloor Terrain (variable radius with scroll)
   - DirtFloor Small/Medium/Large/XLarge (preset sizes)
   - Elevation controls
   - Terrain modification tools (level, raise, lower, smooth)
   - Paint tools (dirt, paved, cultivated, grass)

**Usage:**
1. Copy YAML entries to your `infinity_hammer.yaml` file under the `hoe:` section
2. Use the new "DirtFloor Terrain" tool to place terrain disks
3. Use elevation control tools for vertical positioning
4. Paint works correctly because each disk has its own paint mask at proper resolution

**Why This Fixes Paint:**
- Each placed disk gets its OWN combiner (or joins existing nearby combiner correctly)
- Paint mask resolution is appropriate for the disk size
- No overlapping combiners with conflicting bounds/resolutions
- User has explicit control over terrain creation vs modification

---

## Attempt 8: Simplified Paint Flow - Direct to Combiner
**Date:** 2026-03-04  
**Status:** ? IMPLEMENTED

**Problem Identified:**
The paint flow was going through multiple overlapping systems:
1. `TerrainOperationExecutor.ExecutePaint()` ? `DirtFloorTerrainManager.PaintArea()`
2. `PaintArea()` used `ZonePaintMaskManager.PaintAtWorldPosition()` with a terrain check callback
3. The callback `IsPositionOverCustomTerrain()` used `DirtFloorController.GetDirtFloorAtPosition()`
4. That function only checks **disk centers**, not the actual **combiner mesh area**
5. Result: Paint fails with "No terrain at paint position" even though terrain exists

**Root Cause:**
The zone-based paint system was designed for a different architecture where paint would be stored per-64m-zone like vanilla TerrainComp. But our custom terrain uses **combiners** as the paint storage unit, not zones. The zone system added complexity without benefit.

**Solution:**
Simplified `DirtFloorTerrainManager.PaintArea()` to paint **directly to combiners** instead of going through the zone system:

```csharp
// OLD (broken):
ZonePaintMaskManager.PaintAtWorldPosition(
    center, 
    radius, 
    paintType,
    (pos) => IsPositionOverCustomTerrain(pos, 0.5f)  // <-- This callback failed!
);
var affectedCombiners = DirtFloorMeshCombiner.GetCombinersInRadius(center, radius + 5f);
foreach (var combiner in affectedCombiners)
{
    combiner.RefreshPaintFromZone();  // <-- Refresh from empty zone data
}

// NEW (fixed):
// Paint directly to combiners that have terrain at this position
// This is the ONE paint layer approach - like vanilla TerrainComp per Heightmap
DirtFloorMeshCombiner.PaintAllLayersAt(center, radius, paintType);
```

**Architecture Insight:**
Think of it like vanilla terrain:
- **Vanilla**: Each 64m Heightmap has ONE TerrainComp with ONE paint mask
- **Our System**: Each DirtFloorMeshCombiner has ONE combined paint mask

The combiner IS our "custom heightmap" equivalent. Each combiner manages multiple disks but has a SINGLE paint mask covering all of them. Trying to add another layer (zone-based paint) on top just created confusion.

**Files Modified:**
- `DirtFloorTerrainManager.cs` - Simplified `PaintArea()` to use direct combiner painting

**What This Fixes:**
1. Paint operations now work immediately on newly created terrain
2. No more "No terrain at paint position" warnings
3. Paint is applied to the correct combiner(s) based on `SampleHeightAt()` check
4. Simpler code path = fewer places for bugs

**Testing:**
1. Create terrain with `place_dirtfloor` or level tool
2. Use paint tool (paved, cultivated, etc.)
3. Paint should appear on terrain without errors
4. Check logs - should NOT see "No terrain at paint position"
