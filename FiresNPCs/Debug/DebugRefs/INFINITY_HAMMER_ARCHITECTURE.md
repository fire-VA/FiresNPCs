# Infinity Hammer Architecture Reference

## Overview

This document explains how Infinity Hammer (IH) and World Edit Commands (WEC) work internally, 
and how our custom terrain system needs to integrate with them.

---

## Key Classes and Their Roles

### 1. ToolSelection (`InfinityTools.ToolSelection`)

**Purpose:** Represents an active tool selection (like Level, Raise, Paint, etc.)

**Key Properties:**
- `Tool` - The tool configuration
- `TerrainGrid` - Whether to snap to terrain grid
- `Continuous` - Whether the tool applies continuously while held
- `PlayerHeight` - Whether to use player height as reference

**Key Methods:**
- `AfterPlace(GameObject obj)` - Called when tool is "placed" (executed)
- `HandleCommand(GameObject obj)` - Builds and executes the actual command

**How Commands Are Built:**
```csharp
// In HandleCommand(), position comes from placement ghost:
Transform transform = HammerHelper.GetPlacementGhost().transform;
string newValue1 = transform.position.x.ToString();
string newValue2 = transform.position.y.ToString();  // THIS IS THE Y WE NEED
string newValue3 = transform.position.z.ToString();
```

### 2. Tool (`InfinityTools.Tool`)

**Purpose:** Defines a tool's behavior and command template

**Key Properties:**
- `Name` - Display name
- `Commands` - List of command templates
- `Radius`, `Width`, `Depth`, `Height` - Shape parameters
- `TerrainGrid` - Whether to snap to terrain grid
- `SnapGround` - Whether to snap to ground

**Command Templates use placeholders:**
- `<x>`, `<y>`, `<z>` - World position
- `<r>` - Radius
- `<w>`, `<d>` - Width, Depth  
- `<h>` - Height offset
- `<a>` - Angle

### 3. Ruler (`InfinityTools.Ruler`)

**Purpose:** Visual indicator showing the tool's area of effect

**Key Properties:**
- `Shape` - Circle, Ring, Square, Rectangle, Frame
- `Projector` - The visual GameObject

**Updates position from:**
```csharp
Transform transform2 = placementGhost.transform;
Ruler.Projector.transform.position = transform2.position;
```

### 4. TerrainInfo (`Service.TerrainInfo`)

**Purpose:** Collects terrain data from vanilla Heightmaps/TerrainComps

**Key Methods:**
- `GetTerrainDataWithCircle()` - Gets terrain in circular area
- `GetTerrainDataWithRect()` - Gets terrain in rectangular area
- `CollectTerrainDataInRadius()` - Merges multiple heightmaps

**THIS IS WHERE WE NEED TO HOOK** - IH queries terrain data here

---

## Command Flow (Level Tool Example)

```
1. User selects "Level" tool from hoe menu
   ??? Creates ToolSelection with tool config

2. User aims at ground, IH shows ruler at placementGhost.transform.position
   ??? Position comes from Player.UpdatePlacementGhost()
   ??? Y value comes from raycast hit point

3. User clicks to place
   ??? ToolSelection.AfterPlace() is called
   ??? HandleCommand() builds command string:
       "terrain level from=X,Z,Y circle=R"
   
4. Command is executed via Console.TryRunCommand()
   ??? WEC's terrain command handler processes it
   ??? Calls Terrain.GetCompilers() to find TerrainComp
   ??? Modifies TerrainComp heights/paint

5. TerrainComp calls Heightmap.Poke() to regenerate mesh
```

---

## Where Our Integration Hooks In

### Current Hooks (InfinityHammerCompat.cs)

1. **Terminal.TryRunCommand Prefix**
   - Intercepts "terrain" console commands
   - Checks if position is over custom terrain
   - Routes to our DirtFloorTerrainManager

2. **TerrainOp.Awake Prefix**  
   - Intercepts vanilla TerrainOp placements
   - Checks position against our terrain
   - Destroys TerrainOp and handles ourselves

### Problem: Position Sources

The Y position can come from multiple sources:

| Source | When | Correct? |
|--------|------|----------|
| `TerrainOp.transform.position` | Vanilla hoe | Usually yes |
| `placementGhost.transform.position` | IH tools | YES - this is what we want |
| `Console command from=X,Z,Y` | WEC command | YES |
| Direct piece placement | IH piece mode | PROBLEMATIC |

**The Bug:** When placing `Piece_DirtFloor_vad` directly (not via terrain tool),
the raycast may hit water at Y?0 instead of existing terrain.

---

## Required Fixes

### Fix 1: Use IH's Placement Height

When IH places our piece, we should get the height from:
- `Player.m_placementGhost.transform.position.y` if available
- Or the `LevelArea` height parameter (already passed correctly)

The problem is the piece placement itself goes through a different code path
that doesn't use our `LevelArea()`.

### Fix 2: Intercept Piece Placement

We may need to add a patch to intercept when `Piece_DirtFloor_vad` is placed:

```csharp
[HarmonyPatch(typeof(Player), "PlacePiece")]
class PlacePiece_Patch
{
    static void Postfix(Piece piece, bool __result)
    {
        if (!__result) return;
        if (piece.name.Contains("DirtFloor"))
        {
            // The piece was placed at Player.m_placementGhost.transform.position
            // This position IS correct (IH sets it properly)
            // We just need to ensure our DirtFloorController uses this Y
        }
    }
}
```

### Fix 3: Validate Height in DirtFloorController.OnPlaced()

The `DirtFloorController` should trust its `transform.position.y` because
IH has already set it correctly via the placement ghost.

The issue is somewhere we're overriding this height incorrectly.

---

## Key Insight from Decompiled Code

In `ToolSelection.HandleCommand()`:
```csharp
Transform transform = HammerHelper.GetPlacementGhost().transform;
string newValue2 = transform.position.y.ToString();
```

**The Y coordinate IS correct in IH.** The problem is in OUR code that receives it.

Looking at our logs:
```
[DirtFloorTerrainManager] LevelArea at (-5373.29, 0.74, 342.25), radius=5, height=0.7416729
```

The `LevelArea` is being called with height=0.74 which is wrong.
This means the CALLER is passing wrong values.

---

## Next Steps

1. Trace where `LevelArea` is being called with wrong height
2. Check `TerrainCommandParser` - is it parsing height correctly?
3. Check `TerrainOperationExecutor` - is it passing height correctly?
4. The issue is likely in how we extract Y from the placement

See: `INFINITY_HAMMER_INTEGRATION_FIXES.md` for specific code fixes.
