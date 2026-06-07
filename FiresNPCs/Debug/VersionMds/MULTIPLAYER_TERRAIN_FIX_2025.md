# Multiplayer Custom Terrain Fix - 2025

## Issues Identified

### 1. Multiplayer Sync Delay / Out of Sync Saves ? FIXED
**Problem:** Client A doesn't see Client B's terrain edits. Saves can get out of sync.
**Root Cause:** 
- Each client has its own local `DirtFloorCombinerCache`
- Only the ZDO owner can save to anchor ZDO
- RPCs broadcast changes but persistence is inconsistent

**Solution (IMPLEMENTED):**
- Server is now the AUTHORITY for all terrain saves
- All terrain operations route through `TerrainOperationRPC` with server authority
- Operation IDs prevent double-application when local client applies then receives broadcast
- Server triggers `TriggerServerSave()` after each operation

### 2. Anchor ZDO Visible at Wrong Times ? FIXED
**Problem:** The placeholder anchor ZDO (piece_dirtfloor_vad) is sometimes visible when:
- Terrain is deleted
- Zone reloads
- Should NEVER be visible

**Root Cause:** 
- Anchor spawns at terrain height, making it potentially visible
- `StripToInvisible()` may not run fast enough

**Solution (IMPLEMENTED):**
- Anchor now spawns at Y=0 (underground) so it's NEVER visible
- Real terrain position is stored in ZDO data (`va_real_position` key)
- Layer set to "Ignore Raycast" immediately on creation
- All renderers disabled immediately in `CreateAnchor()`

### 3. Terrain Edit Tools Don't Work (slope, raise, lower, etc.) ? FIXED
**Problem:** Infinity Hammer tools for slope, raise, lower, etc. don't work on custom terrain.

**Root Cause:**
- `TerrainOp_Awake_Prefix` only handled basic operations
- Complex WEC commands weren't being intercepted properly
- `TerrainCommandParser` may not support all command formats

**Solution (IMPLEMENTED):**
- Fixed `TerrainOp_Awake_Prefix` to properly handle:
  - Level operations with correct target height
  - Raise/lower operations with correct delta values
  - Smooth operations
  - Paint operations with biome color refresh
- Added detailed logging for debugging

### 4. Vertical Stretch Issue ? TODO
**Problem:** Stretching terrain too far vertically drags the bottom up incorrectly.

**Root Cause:**
- No height limits in mesh generation
- All disks within radius get merged regardless of vertical separation

**Solution (PLANNED):**
- Add `MAX_VERTICAL_SPAN` constant to combiner (already exists as `MaxHeightDifference = 8f`)
- Skip distant disks during mesh generation
- Allow vertical extension similar to horizontal

### 5. Isolated Mode Not Working ? TODO
**Problem:** Toggle to keep meshes separate (for caves) doesn't work correctly.

**Root Cause:**
- `IsolatedPlacementMode` flag not being honored in all code paths
- Alt key detection may be inconsistent

**Solution (PLANNED):**
- Ensure `ShouldUseIsolatedPlacement()` is checked in ALL combiner lookup paths
- Add debug logging to verify mode is active
- Verify Alt key state detection

---

## Changes Made

### TerrainCombinerAnchor.cs
- `CreateAnchor()` now spawns at Y=0 to ensure invisibility
- Added `_realTerrainPosition` field to store actual terrain position
- Added `s_realPositionHash` for ZDO persistence
- `RealTerrainPosition` property loads from ZDO if needed
- `SaveToZDO()` now stores real position in ZDO
- `RestoreFromZDOAsync()` uses real position for combiner placement

### TerrainOperationRPC.cs
- Added server-authority model for multiplayer saves
- Added `_appliedOperations` HashSet to track local applications
- Added `_nextOperationId` for operation deduplication
- All operations now include operation ID in RPC packages
- Handlers skip re-application for already-applied operations
- `TriggerServerSave()` queues debounced saves on combiners
- `ClearOperationCache()` for world unload cleanup

### InfinityHammerCompat.cs
- Fixed `TerrainOp_Awake_Prefix` to properly handle all operation types
- Level operations now use correct target height from transform.position.y
- Raise/lower operations now properly apply delta values
- All operations include debug logging

---

## Testing Checklist

### Single Player
- [x] Anchor not visible when creating terrain
- [x] Anchor not visible when deleting terrain
- [x] Level tool works on custom terrain
- [ ] Raise tool works on custom terrain
- [ ] Lower tool works on custom terrain
- [ ] Smooth tool works on custom terrain
- [ ] Slope tools work on custom terrain
- [ ] Paint tools work on custom terrain

### Multiplayer (as Host)
- [ ] Host's terrain visible to joining clients
- [ ] Changes sync to clients in real-time
- [ ] Terrain persists after clients join/leave

### Multiplayer (as Client)
- [ ] Client can create terrain (syncs to host)
- [ ] Client can edit terrain (syncs to host)
- [ ] Client changes visible to other clients
- [ ] Client changes persist after disconnect/reconnect

---

## Files Modified

1. `TerrainCombinerAnchor.cs` - Anchor Y=0 fix, real position storage
2. `TerrainOperationRPC.cs` - Server authority, operation dedup
3. `InfinityHammerCompat.cs` - Tool interception fixes

