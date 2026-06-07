# Unified Custom Terrain System

## Overview

This document describes the unified approach to custom terrain generation where all components
(visual mesh, collision, paint, grass) use the exact same height data.

---

## The Problem (Before)

The terrain system had multiple sources of height data causing mismatches:

| Component | Height Source | Issue |
|-----------|---------------|-------|
| Visual mesh | `_heights[idx] - transform.position.y` | Local space |
| Collision mesh | Separate mesh with `-0.02f` offset | Different mesh! |
| Paint mask | Sampled at `transform.position.y` | Wrong Y for sampling |
| Grass | `_heights[idx] + 0.02f` | Extra offset |

**Result:** Player floats/sinks, paint doesn't match terrain, grass at wrong height.

---

## The Solution (After)

### Single Source of Truth: `_heights[]` Array

All components now derive from the same `_heights[]` array which stores **absolute world Y** values.

| Component | Height Source | Result |
|-----------|---------------|--------|
| Visual mesh | `_heights[idx] - transform.position.y` | Local space vertex Y |
| Collision mesh | **Same mesh as visual** | Perfect alignment |
| Paint mask | UV from mesh vertices | Matches mesh exactly |
| Grass | `_heights[idx]` (exact) | Same as visual |

---

## Key Changes

### 1. Collision Mesh: Use Same Mesh

**Before:**
```csharp
// Created separate collision mesh with offset
Mesh collisionMesh = new Mesh();
Vector3[] collisionVertices = new Vector3[vertices.Length];
for (int i = 0; i < vertices.Length; i++)
{
    collisionVertices[i] = vertices[i];
    collisionVertices[i].y -= 0.02f;  // OFFSET CAUSED MISMATCH!
}
collisionMesh.vertices = collisionVertices;
_meshCollider.sharedMesh = collisionMesh;
```

**After:**
```csharp
// Use the SAME mesh for collision
Physics.BakeMesh(_combinedMesh.GetInstanceID(), false);
_meshCollider.sharedMesh = _combinedMesh;  // SAME MESH = PERFECT ALIGNMENT
```

### 2. Grass Spawning: Use Exact Height

**Before:**
```csharp
Vector3 spawnPos = new Vector3(
    worldPos.x + randX,
    _heights[idx] + 0.02f,  // EXTRA OFFSET!
    worldPos.z + randZ
);
```

**After:**
```csharp
Vector3 spawnPos = new Vector3(
    worldPos.x + randX,
    terrainHeight,  // EXACT HEIGHT from _heights array
    worldPos.z + randZ
);
```

### 3. Paint System: Terrain-Aware

**Before:**
- Paint applied to any combiner in XZ range
- No vertical check
- Paint could apply to wrong layer

**After:**
- Check `SampleHeightAt()` before painting
- Verify vertical distance to terrain
- Only paint combiners with actual terrain at position

---

## Height Data Flow

```
Disk Placement
     |
     v
SampleHeights() -> _heights[] array (absolute world Y)
     |
     v
BuildMesh() -> vertices in local space (height - transform.y)
     |
     v
UpdateCollisionMesh() -> uses SAME mesh (_combinedMesh)
     |
     v
SpawnGrassOnMesh() -> uses _heights[] directly
     |
     v
PaintDirectlyOnCombinedMask() -> UV from mesh matches exactly
```

---

## Benefits

1. **No more floating/sinking** - Collision is the visual mesh
2. **Paint always matches** - UV coordinates from unified mesh
3. **Grass at correct height** - Uses same height data
4. **Simpler code** - Single source of truth
5. **Less memory** - No duplicate collision mesh
6. **Faster** - No separate mesh generation

---

## Testing Checklist

- [ ] Place terrain disk - collision matches visual
- [ ] Walk on terrain - no floating/sinking
- [ ] Paint terrain - paint appears where clicked
- [ ] Grass spawns at correct height
- [ ] Level tool creates flat terrain
- [ ] Multiple overlapping disks work correctly

---

## Files Modified

- `DirtFloorMeshCombiner.Utilities.cs` - Collision uses same mesh
- `DirtFloorMeshCombiner.Grass.cs` - Exact heights, no offset
- `DirtFloorMeshCombiner.Static.cs` - Paint vertical check
