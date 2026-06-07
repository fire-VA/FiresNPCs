# DirtFloor TerrainComp ZDO Migration Plan

## Overview

This document outlines the plan to migrate DirtFloor (custom terrain) persistence from the current per-disk ZDO system to using TerrainComp ZDO storage, similar to how we implemented clutter paint persistence.

**Goal:** Simplify save/load, get automatic zone management, improve reliability.

**Status:** Phase 2 COMPLETE ? | ClutterSystem Integration FIXED ? | Multi-Layer Paint FIXED ?

---

## Recent Fixes (2026-02-27)

### Multi-Layer Paint Fix ? COMPLETE

**Problem:** When painting terrain (pave/cultivate/etc.), the paint operation was being applied to **ALL** combiners whose bounds overlapped horizontally with the paint area, not just the combiner that actually had terrain at that position. This caused:
- Multiple overlapping paint strokes
- Visual artifacts with paint showing through at different heights
- Log spam showing `[DirtFloorMeshCombiner] PaintAllLayersAt: Painted 11 overlapping layers`

**Root Cause Analysis:**
The `PaintAllLayersAt()` method in `DirtFloorMeshCombiner.cs` was iterating through ALL combiners and checking if their bounds contained the paint position (using `_combinedBounds.Contains()`). However:
1. Bounds overlap check didn't verify if the combiner actually had **terrain geometry** at the paint position
2. Multiple overlapping combiners at the same horizontal position but with terrain at different heights were all painted
3. The height threshold check was comparing combiner transform position, not actual terrain surface height

**Solution Implemented:**
Modified `PaintAllLayersAt()` to:
1. Use `SampleHeightAt()` to verify the combiner has **actual terrain geometry** at the paint position (returns < -50 if no terrain)
2. Only paint on the **single best combiner** - the one with terrain closest to the paint height
3. Remove the multi-layer iteration - paint goes to ONE combiner only

**Before:**
```csharp
// Painted ALL combiners with any horizontal bounds overlap
foreach (var combiner in s_allCombiners)
{
    if (combiner._combinedBounds.Contains(testPoint))
    {
        combiner.OnPaintTerrain(worldPos, radius, paintType);
        affectedCount++;
    }
}
// Result: "Painted 11 overlapping layers" - incorrect!
```

**After:**
```csharp
// Only paint on the combiner that has actual terrain at the position
DirtFloorMeshCombiner bestCombiner = null;
float bestHeightDiff = float.MaxValue;

foreach (var combiner in s_allCombiners)
{
    float sampledHeight = combiner.SampleHeightAt(worldPos);
    if (sampledHeight < -50f) continue; // No terrain at this position
    
    float heightDiff = Mathf.Abs(sampledHeight - paintHeight);
    if (heightDiff <= heightThreshold && heightDiff < bestHeightDiff)
    {
        bestHeightDiff = heightDiff;
        bestCombiner = combiner;
    }
}

if (bestCombiner != null)
{
    bestCombiner.OnPaintTerrain(worldPos, radius, paintType);
}
// Result: Painted on 1 combiner - correct!
```

**Files Modified:**
- `FiresNPCs/Modules/CustomTerrain/DirtFloorMeshCombiner.cs` - Modified `PaintAllLayersAt()` method

**Result:**
- ? Paint operations only affect the single correct combiner
- ? No more multiple overlapping paint strokes
- ? Visual artifacts eliminated
- ? Log output now shows: `Painted on combiner at height X.X`
- ? Performance improved (1 paint operation instead of 11)

### ClutterSystem NullReferenceException Fix ? COMPLETE

**Problem:** When custom terrain was on the "terrain" layer to allow vanilla clutter spawning, `ClutterSystem.GetGroundInfo` would throw NREs because our `CustomHeightmap` doesn't have a vanilla `Heightmap` component.

**Root Cause Analysis:**
1. `ClutterSystem.GetGroundInfo` raycasts on "terrain" layer
2. When it hits our custom terrain, it calls `hitInfo.collider.GetComponent<Heightmap>()` ? returns `null`
3. Then it calls `hmap.GetBiome(point)` ? NRE!
4. Additionally, `GenerateVegPatch` uses `hmap` for more checks after `GetGroundInfo`:
   - `hmap.GetOceanDepth()` - ocean depth filtering
   - `hmap.GetVegetationMask()` - vegetation mask filtering
   - `hmap.IsCleared()` - cleared/uncleared filtering

**Solution Implemented:**
Added a **prefix patch** to `ClutterSystem.GetGroundInfo` in `EnvironmentBoxPatches.cs`:

```csharp
[HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.GetGroundInfo))]
[HarmonyPrefix]
[HarmonyPriority(Priority.First)]
private static bool ClutterSystem_GetGroundInfo_Prefix(...)
{
    // 1. Do raycast ourselves
    // 2. If hit custom terrain (CustomHeightmap, DirtFloorController, DirtFloorMeshCombiner):
    //    - Return point/normal from our terrain surface
    //    - Find vanilla Heightmap at position for ocean/vegetation/cleared checks
    //    - Return biome from WorldGenerator (with env box override support)
    // 3. If hit vanilla terrain: handle normally
    // 4. If no hit: return false
}
```

**Key Insight:** We return the vanilla `Heightmap` that exists underneath our custom terrain. This allows `GenerateVegPatch` to use it for ocean depth, vegetation mask, and cleared state checks - which are reasonable defaults since our custom terrain sits on top of the world terrain.

**Files Modified:**
- `FiresNPCs/Modules/EnvironmentBoxes/EnvironmentBoxPatches.cs` - Added prefix patch
- `FiresNPCs/Modules/CustomTerrain/CustomHeightmap.cs` - Changed to use "terrain" layer

**Result:**
- ? No more NRE spam
- ? Vanilla clutter CAN spawn on custom terrain
- ? Custom terrain operations work correctly (paint, level, etc.)
- ? Infinity Hammer integration works: `[InfinityHammerCompat] Intercepting terrain command...`

### TerrainComp Duplicate Detection Loop Fix ? COMPLETE

**Problem:** Vanilla Valheim's `TerrainComp.Awake()` checks for duplicate TerrainComps in the same zone and destroys them. Our storage system was triggering this, causing an infinite loop:

```
[DirtFloorStorage] Loaded 12 disk(s) from TerrainComp at (-5440.00, 0.00, 384.00)
[Warning] Found another terrain compiler in this area, removing it
[DirtFloorStorage] Loaded 12 disk(s) from TerrainComp at (-5440.00, 0.00, 384.00)
[Warning] Found another terrain compiler in this area, removing it
... (infinite loop)
```

**Root Cause Analysis:**
Looking at vanilla `TerrainComp.Awake()`:
```csharp
TerrainComp terrainCompiler = TerrainComp.FindTerrainCompiler(this.transform.position);
if ((bool) (UnityEngine.Object) terrainCompiler)
{
    ZLog.LogWarning((object) "Found another terrain compiler in this area, removing it");
    ZNetScene.instance.Destroy(terrainCompiler.gameObject);
}
```

1. Our `FindTerrainCompForPosition()` was calling `hmap.GetAndCreateTerrainCompiler()` which could create NEW TerrainComps
2. We were tracking by instance ID, but destroyed TerrainComps get new IDs when recreated
3. Each `TerrainComp.Load` postfix fired, triggering more restores

**Solution Implemented:**
1. **Never create TerrainComps** - Only use `TerrainComp.FindTerrainCompiler()`, never `GetAndCreateTerrainCompiler()`
2. **Track by zone, not instance ID** - Use `Vector2Int` zone keys based on 64m grid
3. **Don't clear zone tracking on destroy** - Once a zone is restored, don't re-restore even if TerrainComp is recreated
4. **Per-frame debouncing** - Prevent multiple processing of same TerrainComp in one frame

**Files Modified:**
- `FiresNPCs/Modules/CustomTerrain/DirtFloorTerrainCompStorage.cs`

**Key Changes:**
```csharp
// OLD: Track by instance ID (problematic)
private static readonly HashSet<int> _restoredTerrainComps = new HashSet<int>();

// NEW: Track by zone key (stable)
private static readonly HashSet<Vector2Int> _restoredZones = new HashSet<Vector2Int>();

// Zone key calculation (64m zones like vanilla)
private static Vector2Int GetZoneKey(Vector3 position)
{
    return new Vector2Int(
        Mathf.FloorToInt(position.x / 64f),
        Mathf.FloorToInt(position.z / 64f)
    );
}
```

**Result:**
- ? No more duplicate detection loop
- ? Each zone restored exactly once per session
- ? TerrainComp recreation doesn't trigger re-restoration

---

## Implementation Progress

### Phase 1: Read/Write Infrastructure ? COMPLETE

Created `DirtFloorTerrainCompStorage.cs` with:
- `SaveDisk()` - Saves disk data to TerrainComp ZDO
- `RemoveDisk()` - Removes disk from storage
- `RestoreFromTerrainComp()` - Loads disks from ZDO
- `DirtFloorRestoreHelper` - Deferred restoration MonoBehaviour
- Harmony patches for TerrainComp.Load and OnDestroy

Added helper methods to `DirtFloorController.cs`:
- `GetPaintMaskBytes()` - Serialize paint mask for storage
- `LoadPaintMaskFromBytes()` - Load paint mask from storage
- `GetGrassType()` / `SetGrassType()` - Grass type persistence
- `GetCombinerGroupId()` - Combiner tracking
- `ForceRegenerate()` - Trigger mesh rebuild after restoration

Added helper method to `GrassOverlaySystem.cs`:
- `SetGrassTypeByName()` - Set grass type by name string

### Phase 2: Integration ? COMPLETE

Wired up the storage system:
1. ? `DirtFloorController.SaveToZDO()` now also saves to TerrainComp storage
2. ? `DirtFloorController.OnDestroy()` removes disk from TerrainComp storage
3. ? Harmony patches auto-restore disks when TerrainComp loads
4. ? Deferred restoration ensures systems are ready before restoring
5. ? ClutterSystem integration fixed - custom terrain on "terrain" layer works

**Current Storage Strategy: DUAL WRITE**
- Primary: Disk's own ZDO (existing system, reliable)
- Secondary: TerrainComp ZDO (new system, zone-based)

This allows testing the new system without breaking existing functionality.

**Verification:**
To verify TerrainComp storage is working:
1. Enable verbose logging: `DirtFloorTerrainCompStorage.VerboseLogging = true;`
2. Modify a disk (paint, etc.) - should see `[DirtFloorStorage] Updated disk at...`
3. Leave zone and return - should see `[DirtFloorStorage] Loaded X disk(s) from TerrainComp...`

**Observed Behavior (from logs):**
- Combiner cache saves correctly: `[DirtFloorCombinerCache] Saved cache: 111 groups`
- Paint operations work: `[DirtFloorMeshCombiner] PaintAllLayersAt: Painted 4 overlapping layers`
- InfinityHammer integration works: `[InfinityHammerCompat] Intercepting terrain command: TerrainOp: circle=5...`
- No ClutterSystem NREs during gameplay

**Debug Commands:**
- `dirtfloorinfo` - Shows DirtFloor storage status
- `dirtfloorinfo verbose` - Toggles verbose logging for TerrainComp storage

### Phase 3: Migration (TODO)

Migrate existing disk data:
1. Detect old-format data in disk's own ZDO
2. Migrate to TerrainComp storage
3. Clear old data (or leave for compatibility)

**Tasks:**
- [ ] Add `HasLegacyZDOData()` method to `DirtFloorController`
- [ ] Add `MigrateToTerrainCompStorage()` method
- [ ] Call migration on disk load
- [ ] Add logging for migration progress

### Phase 4: Cleanup (TODO)

Remove legacy save system:
1. Remove per-disk ZDO writes
2. Simplify or remove DirtFloorSaveManager
3. Update documentation

**Tasks:**
- [ ] Remove ZDO writes from `DirtFloorController.SaveToZDO()`
- [ ] Remove ZDO writes from `CustomHeightmap.SaveToZDO()`
- [ ] Evaluate if `DirtFloorSaveManager` can be removed entirely
- [ ] Update `DirtFloorCombinerCache` to use TerrainComp storage
- [ ] Clean up unused code

---

## Current System Analysis

### How DirtFloor Currently Saves/Loads

Each `DirtFloorController` (disk piece) has its own ZNetView/ZDO:
- Heights array saved to disk's own ZDO
- Paint mask saved to disk's own ZDO
- Grass type saved to disk's own ZDO
- `DirtFloorSaveManager` batches saves to reduce network traffic

### Current Files Involved
| File | Purpose |
|------|---------|
| `DirtFloorController.cs` | Main controller, owns ZNetView |
| `DirtFloorSaveManager.cs` | Batched save manager |
| `DirtFloorMeshCombiner.cs` | Combines multiple disks into one mesh |

### Current Pain Points
1. **Many ZDOs** - Each disk is its own network object
2. **Complex save coordination** - Need batching to avoid spam
3. **Zone edge issues** - Disks spanning zone boundaries need special handling
4. **Mesh combiner state** - Combiner mesh/paint not directly persisted

---

## Proposed System: TerrainComp ZDO Storage

### Architecture

```
???????????????????????????????????????????????????????????
?                    TerrainComp ZDO                       ?
?                   (one per 64m zone)                     ?
???????????????????????????????????????????????????????????
?  va_custom_disks      = List<DiskData>                  ?
?  va_combiner_state    = CombinerData (optional)         ?
???????????????????????????????????????????????????????????
                           ?
                           ?
              ??????????????????????????
              ?  On TerrainComp.Load   ?
              ?  (Harmony Postfix)     ?
              ??????????????????????????
                           ?
                           ?
              ??????????????????????????
              ?  DirtFloorRestorer     ?
              ?  - Recreate disks      ?
              ?  - Rebuild meshes      ?
              ?  - Spawn grass         ?
              ??????????????????????????
```

### Data Structures

```csharp
/// <summary>
/// Data for a single dirt floor disk, stored in TerrainComp ZDO.
/// </summary>
public class DiskData
{
    public int Version;           // Data format version
    public Vector3 Position;      // World position (center)
    public float Radius;          // Disk radius
    public float[] Heights;       // Height values (compressed)
    public byte[] PaintMask;      // Paint texture (PNG compressed)
    public string GrassType;      // Which grass prefab to use
    public int CombinerGroupId;   // Which combiner this disk belongs to
}

/// <summary>
/// Data for a mesh combiner group (optional optimization).
/// </summary>
public class CombinerData
{
    public int GroupId;
    public Bounds CombinedBounds;
    public List<int> DiskIndices;  // Indices into DiskData list
    // Could also store pre-computed combined mesh data
}
```

### ZDO Keys

```csharp
public static class DirtFloorZDOKeys
{
    public static readonly int CUSTOM_DISKS = "va_custom_disks".GetStableHashCode();
    public static readonly int COMBINER_STATE = "va_combiner_state".GetStableHashCode();
    
    // Data format version (increment when format changes)
    public const int DATA_VERSION = 1;
}
```

---

## Implementation Steps

### Phase 1: Read Infrastructure

**Goal:** Add ability to read DirtFloor data from TerrainComp ZDO without breaking existing system.

1. **Create `DirtFloorTerrainCompStorage.cs`**
   - Static methods for save/load
   - Data serialization with ZPackage
   - Compression using Utils.Compress/Decompress

2. **Add Harmony patches**
   ```csharp
   [HarmonyPatch(typeof(TerrainComp), "Load")]
   [HarmonyPostfix]
   static void OnTerrainCompLoad(TerrainComp __instance, bool __result)
   {
       if (!__result) return;
       DirtFloorTerrainCompStorage.RestoreFromTerrainComp(__instance);
   }
   
   [HarmonyPatch(typeof(TerrainComp), "OnDestroy")]
   [HarmonyPostfix]
   static void OnTerrainCompDestroy(TerrainComp __instance)
   {
       DirtFloorTerrainCompStorage.OnTerrainCompUnloaded(__instance);
   }
   ```

3. **Deferred restoration** (like clutter)
   - Queue restoration until game systems are ready
   - Use MonoBehaviour polling or coroutine

### Phase 2: Write Infrastructure

**Goal:** Save DirtFloor data to TerrainComp ZDO.

1. **Modify `DirtFloorController.SaveToZDO()`**
   - Instead of saving to own ZDO, call `DirtFloorTerrainCompStorage.SaveDisk()`
   - Find nearest TerrainComp and save there

2. **Handle zone boundaries**
   - Disk center determines which TerrainComp stores it
   - Disks near zone edges: store in "primary" zone only

3. **Batching**
   - Can reuse existing `DirtFloorSaveManager` concept
   - Or simplify since TerrainComp handles some batching

### Phase 3: Migration

**Goal:** Migrate existing disk data to new format.

1. **Detect old-format data**
   - Check if disk has data in its own ZDO (old format)
   - If found, migrate to TerrainComp storage

2. **Migration on load**
   ```csharp
   void MigrateIfNeeded(DirtFloorController disk)
   {
       if (disk.HasLegacyZDOData())
       {
           // Read from old location
           var data = disk.ReadLegacyData();
           
           // Save to new location
           DirtFloorTerrainCompStorage.SaveDisk(data);
           
           // Clear old data (optional, or leave for compatibility)
           disk.ClearLegacyData();
       }
   }
   ```

### Phase 4: Cleanup

**Goal:** Remove old save system, simplify code.

1. **Remove per-disk ZDO writes** (keep reads for migration)
2. **Simplify `DirtFloorSaveManager`** or remove entirely
3. **Update documentation**

---

## Zone Boundary Handling

### The Problem

A disk at position (64, 0, 32) with radius 10 spans two zones:
- Zone (1,0): X=64-128
- Zone (0,0): X=0-64

### Solution Options

**Option A: Single Zone Storage (Recommended)**
- Store disk data in the TerrainComp where disk CENTER is located
- When that zone loads, the disk is restored
- Adjacent zone doesn't need the disk data (mesh extends visually)

```csharp
TerrainComp GetStorageTerrainComp(Vector3 diskCenter)
{
    // Always use the zone containing the center
    return TerrainComp.FindTerrainCompiler(diskCenter);
}
```

**Option B: Multi-Zone References**
- Store in primary zone
- Store reference in adjacent zones
- More complex, probably unnecessary

**Option A is recommended** because:
- Simpler implementation
- Mesh combiner already handles cross-zone rendering
- If player is near disk, both zones are likely loaded anyway

### Edge Case: Disk Center Exactly on Zone Boundary

```csharp
// Add small offset toward disk's "main" zone if exactly on boundary
Vector3 GetAdjustedCenter(Vector3 center)
{
    const float epsilon = 0.01f;
    // Offset toward the zone with more disk coverage
    // Or just pick consistently (e.g., always +X, +Z)
    return center + new Vector3(epsilon, 0, epsilon);
}
```

---

## Data Compression

### Heights Array

Heights array can be large: 64x64 grid = 4096 floats = 16KB uncompressed.

**Compression Strategy:**
```csharp
byte[] CompressHeights(float[] heights)
{
    var pkg = new ZPackage();
    pkg.Write(heights.Length);
    
    // Delta encoding: store differences from first value
    float baseHeight = heights[0];
    pkg.Write(baseHeight);
    
    for (int i = 1; i < heights.Length; i++)
    {
        // Store as half-precision for space savings
        pkg.Write((Half)(heights[i] - baseHeight));
    }
    
    return Utils.Compress(pkg.GetArray());
}
```

Expected compression: 16KB ? ~2-4KB

### Paint Mask

Already stored as PNG (compressed texture format):
```csharp
byte[] CompressPaintMask(Texture2D paintMask)
{
    return paintMask.EncodeToPNG();  // Already compressed
}
```

32x32 RGBA PNG: ~500 bytes to 2KB

### Total Per-Disk Size

| Data | Uncompressed | Compressed |
|------|--------------|------------|
| Position/Radius | 16 bytes | 16 bytes |
| Heights (64x64) | 16KB | ~3KB |
| Paint Mask (32x32) | 4KB | ~1KB |
| Grass Type | ~50 bytes | ~50 bytes |
| **Total** | **~20KB** | **~4KB** |

With multiple disks per zone: 10 disks × 4KB = 40KB per zone (acceptable)

---

## Restoration Timing

### Issue: Systems Not Ready

When `TerrainComp.Load` fires, some systems might not be ready:
- `ClutterSystem.instance` might be null
- Material caches might not be populated

### Solution: Deferred Restoration

```csharp
public class DirtFloorRestoreHelper : MonoBehaviour
{
    private static Queue<(TerrainComp tc, List<DiskData> disks)> _pending;
    
    void Update()
    {
        if (_pending.Count == 0) return;
        
        // Check if ready
        if (!AreSystemsReady()) return;
        
        // Process all pending
        while (_pending.Count > 0)
        {
            var (tc, disks) = _pending.Dequeue();
            RestoreDisks(tc, disks);
        }
    }
    
    static bool AreSystemsReady()
    {
        // Check all required systems
        if (ClutterSystem.instance == null) return false;
        if (ClutterSystem.instance.m_clutter == null) return false;
        if (ZNetScene.instance == null) return false;
        // Add any other checks
        return true;
    }
}
```

---

## Mesh Combiner Integration

### Current Combiner Behavior

`DirtFloorMeshCombiner` collects nearby disks and creates a combined mesh:
- Combines heightmaps
- Combines paint masks
- Spawns grass on combined mesh

### With TerrainComp Storage

**Option A: Restore Disks, Let Combiner Recalculate**
1. Restore individual `DirtFloorController` disks
2. Combiner detects them and rebuilds combined mesh
3. Simplest, most compatible

**Option B: Store Combined State**
1. Also store combiner bounds/membership in ZDO
2. Faster restoration (skip recalculation)
3. More complex, larger data

**Recommendation: Start with Option A**
- Simpler implementation
- Combiner recalculation is already fast
- Can optimize later if needed

---

## API Design

```csharp
public static class DirtFloorTerrainCompStorage
{
    #region Public API
    
    /// <summary>
    /// Saves a disk to the appropriate TerrainComp ZDO.
    /// </summary>
    public static void SaveDisk(DirtFloorController disk)
    {
        var tc = TerrainComp.FindTerrainCompiler(disk.transform.position);
        if (tc == null) return;
        
        var allDisks = LoadDisksFromTerrainComp(tc);
        
        // Update or add
        int existingIndex = allDisks.FindIndex(d => 
            Vector3.Distance(d.Position, disk.transform.position) < 0.1f);
        
        var diskData = CreateDiskData(disk);
        if (existingIndex >= 0)
            allDisks[existingIndex] = diskData;
        else
            allDisks.Add(diskData);
        
        SaveDisksToTerrainComp(tc, allDisks);
    }
    
    /// <summary>
    /// Removes a disk from TerrainComp storage.
    /// </summary>
    public static void RemoveDisk(Vector3 position)
    {
        var tc = TerrainComp.FindTerrainCompiler(position);
        if (tc == null) return;
        
        var allDisks = LoadDisksFromTerrainComp(tc);
        allDisks.RemoveAll(d => Vector3.Distance(d.Position, position) < 0.1f);
        SaveDisksToTerrainComp(tc, allDisks);
    }
    
    /// <summary>
    /// Called from Harmony postfix on TerrainComp.Load.
    /// </summary>
    public static void RestoreFromTerrainComp(TerrainComp tc)
    {
        var disks = LoadDisksFromTerrainComp(tc);
        if (disks.Count == 0) return;
        
        // Queue for deferred restoration
        DirtFloorRestoreHelper.Queue(tc, disks);
    }
    
    #endregion
    
    #region Serialization
    
    private static List<DiskData> LoadDisksFromTerrainComp(TerrainComp tc) { ... }
    private static void SaveDisksToTerrainComp(TerrainComp tc, List<DiskData> disks) { ... }
    private static DiskData CreateDiskData(DirtFloorController disk) { ... }
    
    #endregion
}
```

---

## Testing Plan

### Unit Tests

1. **Serialization roundtrip**
   - Create DiskData, serialize, deserialize, verify equality

2. **Compression effectiveness**
   - Verify heights compress to expected size
   - Verify paint mask survives PNG encode/decode

### Integration Tests

1. **Save and reload**
   - Create disk, save, unload zone, reload zone
   - Verify disk is restored correctly

2. **Zone boundary**
   - Create disk near zone edge
   - Verify stored in correct TerrainComp
   - Verify restored correctly

3. **Multiple disks**
   - Create 10 disks in same zone
   - Save, reload, verify all restored

4. **Migration**
   - Create disk with old format
   - Load, verify migration occurs
   - Reload, verify new format used

### Performance Tests

1. **Save time** - Should be < 100ms for 10 disks
2. **Load time** - Should be < 500ms for 10 disks
3. **Memory** - Monitor ZDO size after many disks

---

## Rollout Plan

### Stage 1: Parallel Systems (1 week)
- Implement new storage system
- Run alongside old system
- New disks save to both locations
- Load prefers new, falls back to old

### Stage 2: Migration (1 week)
- Auto-migrate old data on load
- Monitor for issues
- Logs show migration progress

### Stage 3: Deprecation (1 week)
- Remove old save code
- Keep old load code for stragglers
- Update documentation

### Stage 4: Cleanup
- Remove old load code
- Remove DirtFloorSaveManager (or simplify)
- Final documentation update

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Data loss during migration | Low | High | Keep old data, extensive testing |
| Zone boundary issues | Medium | Medium | Thorough edge case testing |
| Performance regression | Low | Medium | Benchmark before/after |
| Multiplayer desync | Low | High | Test on dedicated server |

---

## Conclusion

Migrating DirtFloor to TerrainComp ZDO storage will:

? **Simplify code** - Remove custom save manager complexity  
? **Automatic zone management** - Data loads/unloads with terrain  
? **Multiplayer sync** - ZDO system handles it  
? **Consistent with clutter** - Same pattern, easier maintenance  

The main work is:
1. Serialization code (~200 lines)
2. Harmony patches (~50 lines)
3. Deferred restoration (~100 lines)
4. Migration logic (~100 lines)

Estimated total: **~500 lines of new code**, replacing/simplifying ~300 lines of existing code.
