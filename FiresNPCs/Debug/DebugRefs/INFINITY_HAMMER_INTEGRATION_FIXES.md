# Infinity Hammer Integration - Required Fixes

## Current Issues

Based on the logs and decompiled IH code, here are the specific issues and fixes needed:

---

## Issue 1: Sea-Level Height (Y ? 0)

### Symptom
```
[DirtFloorTerrainManager] LevelArea at (-5373.29, 0.74, 342.25), radius=5, height=0.7416729
```
Disk created at Y=0.74 (sea level) instead of Y?38 (actual ground).

### Root Cause Analysis

The problem occurs when placing `Piece_DirtFloor_vad` directly (not via terrain level tool).

**IH Terrain Tool Flow (WORKS):**
1. User selects Level tool
2. IH raycast hits ground at Y=38
3. Command built with `from=X,Z,38`
4. Our parser gets correct Y
5. LevelArea called with height=38 ?

**IH Piece Placement Flow (BROKEN):**
1. User selects Piece_DirtFloor_vad in hammer
2. IH raycast hits... something at Y=0.7
3. Piece instantiated at that position
4. OnPlaced() called
5. LevelArea called with transform.position.y = 0.7 ?

### Why Raycast Hits Water

The raycast layer mask might include water/ocean layer, or:
- No existing terrain at that XZ position
- Heightmap hasn't loaded yet
- Custom terrain collider not on "terrain" layer

### Fix Location

**File:** `DirtFloorController.cs` - `OnPlaced()` method

**Current Code (probably):**
```csharp
public void OnPlaced()
{
    // Uses transform.position.y directly
    DirtFloorTerrainManager.LevelArea(transform.position, DirtRadius, transform.position.y);
}
```

**Fixed Code:**
```csharp
public void OnPlaced()
{
    float height = transform.position.y;
    
    // Validate height - if suspiciously low, try to find real ground
    if (height < 5f)
    {
        // Try to get height from:
        // 1. Nearby custom terrain disks
        // 2. Vanilla heightmap at this position
        // 3. Physics raycast from above
        height = GetValidatedHeight(transform.position, height);
    }
    
    DirtFloorTerrainManager.LevelArea(transform.position, DirtRadius, height);
}

private float GetValidatedHeight(Vector3 pos, float fallback)
{
    // Check nearby disks first
    var nearbyDisk = DirtFloorController.GetDirtFloorAtPosition(pos, 20f);
    if (nearbyDisk != null && nearbyDisk != this)
    {
        return nearbyDisk.transform.position.y;
    }
    
    // Check vanilla heightmap
    float hmHeight;
    if (Heightmap.GetHeight(pos, out hmHeight) && hmHeight > 2f)
    {
        return hmHeight;
    }
    
    // Raycast from above
    if (Physics.Raycast(new Vector3(pos.x, 500f, pos.z), Vector3.down, 
        out RaycastHit hit, 1000f, LayerMask.GetMask("terrain", "Default")))
    {
        if (hit.point.y > 2f)
            return hit.point.y;
    }
    
    return fallback;
}
```

---

## Issue 2: Collision Mesh Offset

### Symptom
Collision is "way off from where the actual terrain is drawing in some spots"

### Root Cause Analysis

The `DirtFloorMeshCombiner` creates mesh vertices in LOCAL space relative to its transform.
If the transform position doesn't match the center of the mesh data, collision will be offset.

**Check in `CalculateCombinedBounds()`:**
```csharp
transform.position = new Vector3(
    _combinedBounds.center.x,
    _connectedDisks[0].transform.position.y,  // Uses FIRST disk's Y
    _combinedBounds.center.z
);
```

**Problem:** If disks are at different Y heights, mesh center Y doesn't match transform Y.

### Fix

**File:** `DirtFloorMeshCombiner.MeshGen.cs` - `CalculateCombinedBounds()`

```csharp
private void CalculateCombinedBounds()
{
    // ... calculate minX, maxX, minZ, maxZ, minY, maxY ...
    
    // Use AVERAGE Y of all disks, not just first
    float avgY = 0f;
    int count = 0;
    foreach (var disk in _connectedDisks)
    {
        if (disk == null) continue;
        avgY += disk.transform.position.y;
        count++;
    }
    if (count > 0) avgY /= count;
    
    transform.position = new Vector3(
        _combinedBounds.center.x,
        avgY,  // Use average, not first disk
        _combinedBounds.center.z
    );
}
```

Also verify collision mesh uses same transform:
```csharp
private void UpdateCollisionMesh()
{
    // Collision mesh vertices must be in same local space as render mesh
    // Both are relative to this.transform.position
    
    // Log for debugging:
    Debug.Log($"[Collision] Transform at {transform.position}, " +
              $"Mesh bounds center: {_combinedMesh.bounds.center}, " +
              $"World center: {transform.position + _combinedMesh.bounds.center}");
}
```

---

## Issue 3: Paint Not Visible

### Symptom
Paint operations complete in logs but texture doesn't change visually

### Root Cause Analysis

The paint flow is:
1. `PaintAllLayersAt()` finds combiners
2. `OnPaintTerrain()` paints on combined mask
3. Material should receive updated texture

**Potential Issues:**
- `_combinedPaintMask` not created yet
- `_terrainMaterial` doesn't have `_ClearedMaskTex` property
- Material shader is wrong (Standard instead of Custom/Heightmap)
- UV mapping mismatch between mesh and paint mask

### Fix - Add Debug Logging

**File:** `DirtFloorMeshCombiner.Utilities.cs` - `OnPaintTerrain()`

```csharp
public void OnPaintTerrain(Vector3 worldPos, float radius, DirtFloorController.PaintType paintType)
{
    Debug.Log($"[Paint] OnPaintTerrain at {worldPos}, radius={radius}, type={paintType}");
    Debug.Log($"[Paint] Combiner bounds: {_combinedBounds}");
    Debug.Log($"[Paint] Has paint mask: {_combinedPaintMask != null}");
    Debug.Log($"[Paint] Has material: {_terrainMaterial != null}");
    
    if (_terrainMaterial != null)
    {
        Debug.Log($"[Paint] Material shader: {_terrainMaterial.shader?.name}");
        Debug.Log($"[Paint] Has _ClearedMaskTex: {_terrainMaterial.HasProperty("_ClearedMaskTex")}");
    }
    
    // Ensure bounds are valid
    if (_combinedBounds.size.x < 0.001f || _combinedBounds.size.z < 0.001f)
    {
        Debug.LogWarning("[Paint] Invalid bounds - regenerating");
        CalculateCombinedBounds();
    }
    
    PaintDirectlyOnCombinedMask(worldPos, radius, paintType);
    
    // ... rest of method
}
```

### Fix - Ensure Material Has Correct Shader

**File:** `DirtFloorMeshCombiner.Paint.cs` - `SetupMaterial()`

```csharp
private void SetupMaterial()
{
    // After creating/getting material, verify shader
    if (_terrainMaterial.shader == null || 
        _terrainMaterial.shader.name == "Standard" ||
        _terrainMaterial.shader.name.Contains("InternalError"))
    {
        Debug.LogWarning($"[Paint] Bad shader: {_terrainMaterial.shader?.name}");
        
        // Try to fix with cached material
        TerrainMaterialCache.TryFixMaterial(_terrainMaterial, transform.position);
    }
    
    // Verify paint mask property exists
    if (!_terrainMaterial.HasProperty("_ClearedMaskTex"))
    {
        Debug.LogError("[Paint] Material missing _ClearedMaskTex property!");
        // This material won't show paint - need different shader
    }
}
```

---

## Issue 4: Piece Placement Position (IH Integration)

### Symptom
"Tool to place our own patch does not properly place at the selected spot based off of infinity hammers tool settings"

### Root Cause Analysis

When IH places a piece, it uses:
1. `Player.m_placementGhost.transform.position` for position
2. The piece is instantiated at that position
3. Our `OnPlaced()` is called

The position SHOULD be correct from IH. If it's not, check:
1. Are we modifying position in OnPlaced()?
2. Is there a race condition where IH position isn't set yet?
3. Are we using wrong position source?

### Fix - Trust IH Position

**File:** `DirtFloorController.cs`

```csharp
public void OnPlaced()
{
    // DON'T modify transform.position - IH already set it correctly
    Vector3 placementPos = transform.position;
    
    Debug.Log($"[DirtFloor] OnPlaced at {placementPos}");
    
    // The height IS the transform.position.y that IH set
    // Only validate if suspiciously low (ocean hit)
    float height = placementPos.y;
    if (height < 3f) // Below minimum terrain height
    {
        Debug.LogWarning($"[DirtFloor] Suspicious height {height}, validating...");
        height = GetValidatedHeight(placementPos, height);
    }
    
    // Create the terrain at the IH-specified position
    DirtFloorTerrainManager.LevelArea(
        new Vector3(placementPos.x, height, placementPos.z),
        DirtRadius,
        height,
        forceNewDisk: false
    );
}
```

---

## Testing Checklist

After implementing fixes:

- [ ] Place DirtFloor piece on ground via IH - should be at correct height
- [ ] Place DirtFloor piece over water via IH - should snap to nearby terrain or error
- [ ] Use Level tool on existing custom terrain - should level correctly
- [ ] Use Paint tool on custom terrain - should show paint texture
- [ ] Check collision matches visual mesh position
- [ ] Zone reload - terrain should restore correctly
- [ ] Multiple disks at different heights - each should maintain correct Y

---

## Files to Modify

1. `DirtFloorController.cs` - `OnPlaced()` height validation
2. `DirtFloorMeshCombiner.MeshGen.cs` - `CalculateCombinedBounds()` Y calculation
3. `DirtFloorMeshCombiner.Utilities.cs` - `OnPaintTerrain()` debugging
4. `DirtFloorMeshCombiner.Paint.cs` - `SetupMaterial()` shader validation
5. `DirtFloorTerrainManager.cs` - `LevelArea()` height validation (already added)
