# Infinity Hammer Tool System Reference

## Tool Configuration (infinity_tools.yaml)

IH tools are configured in YAML. Here's the structure:

### Default Hoe Tools

```yaml
hoe:
- name: Level
  description: |-
    Flattens terrain.
    Hold <mod1> to smooth.
    Hold <mod2> for free mode.
    Hold <alt> for player height.
  icon: mud_road
  command: terrain level smooth=<mod1>?.5:0 <area>
  index: 10
  terrainGrid: -<mod2>
  playerHeight: <alt>

- name: Raise
  description: |-
    Raises terrain.
    Hold <mod1> to smooth.
    Hold <mod2> for free mode.
  icon: raise
  command: terrain raise=<h> smooth=<mod1>?.5:0 <area>
  initialHeight: 0.2
  terrainGrid: -<mod2>
  snapGround: true

- name: Pave
  command: terrain paint=paved <area>
  continuous: -<mod1>
  terrainGrid: -<mod2>
  snapGround: true
```

### Placeholder Tokens

| Token | Meaning | Replaced With |
|-------|---------|---------------|
| `<x>` | World X position | `placementGhost.transform.position.x` |
| `<y>` | World Y position | `placementGhost.transform.position.y` |
| `<z>` | World Z position | `placementGhost.transform.position.z` |
| `<r>` | Radius | Scaling.X (mouse wheel adjustable) |
| `<r2>` | Inner radius (ring) | Scaling.Z |
| `<w>` | Width | Scaling.X |
| `<w2>` | Inner width (frame) | Scaling.Z |
| `<d>` | Depth | Scaling.Z |
| `<h>` | Height offset | Scaling.Y |
| `<a>` | Angle | `placementGhost.rotation.eulerAngles.y` |
| `<area>` | Full area spec | `from=<x>,<z>,<y> circle=<r>-<r2> angle=<a> rect=<w>-<w2>,<d>` |
| `<mod1>` | Modifier key 1 | LeftAlt (configurable) |
| `<mod2>` | Modifier key 2 | LeftControl (configurable) |

### Tool Properties

| Property | Type | Description |
|----------|------|-------------|
| `name` | string | Display name |
| `description` | string | Tooltip text |
| `icon` | string | Item/prefab name for icon |
| `command` | string | Command template |
| `commands` | array | Multiple commands with key conditions |
| `continuous` | string | Keep applying while held |
| `initialHeight` | float | Starting height value |
| `initialSize` | float | Starting radius/size |
| `initialShape` | string | circle/ring/square/rectangle/frame |
| `snapGround` | string | Snap ruler to ground |
| `terrainGrid` | string | Snap to terrain grid |
| `playerHeight` | string | Use player Y for level height |
| `highlight` | string | Highlight objects in area |
| `targetEdge` | string | Target edge mode (slope tool) |
| `instant` | bool | Execute immediately vs deferred |

---

## Command Execution Flow

### 1. Tool Selection
```
User presses key with hoe equipped
  ? ToolManager.Get("hoe") returns tool list
  ? Tool is created from ToolData
  ? ToolSelection wraps Tool
  ? Selection.CreateGhost() shows visual
```

### 2. Ruler Update (every frame)
```
Ruler.Update()
  ? Gets placementGhost.transform
  ? Updates projector position
  ? Builds visual (CircleRuler/RectangleRuler)
  ? Snaps to ground if enabled
  ? Snaps to terrain grid if enabled
```

### 3. Placement (click)
```
Player.PlacePiece()
  ? ToolSelection.AfterPlace(obj)
  ? HandleCommand(obj)
    ? Gets transform from placementGhost
    ? Replaces all <tokens> with actual values
    ? Creates PlacedCommand component
    ? PlacedCommand.Start() runs Console.TryRunCommand()
```

### 4. Command Processing
```
Console.TryRunCommand("terrain level from=-5367,343,38.79 circle=5")
  ? Terminal_TryRunCommand (our prefix patch)
  ? InfinityHammerCompat checks if over custom terrain
  ? If yes: TerrainCommandParser.Parse() ? TerrainOperationExecutor.Execute()
  ? If no: Let WEC handle it ? Terrain.GetCompilers() ? TerrainComp.Apply()
```

---

## World Edit Commands Terrain Syntax

### Basic Format
```
terrain [shape] [operation] [parameters]
```

### Shapes
- `circle=R` - Circle with radius R
- `circle=R1-R2` - Ring between R1 and R2  
- `rect=W,D` - Rectangle with width W, depth D
- `rect=W1-W2,D` - Frame with outer W1, inner W2

### Operations
- `level` - Flatten to specified height
- `raise=H` - Raise by H meters
- `lower=H` - Lower by H meters (same as raise=-H)
- `smooth=S` - Smooth with power S (0-1)
- `slope` - Create slope between two points
- `paint=TYPE` - Paint terrain texture
- `reset` - Reset terrain to original

### Paint Types
- `dirt` / `path` / `pathen` ? Dirt path
- `cultivate` / `cultivated` ? Farmland
- `paved` / `stone` ? Stone paving
- `paved_moss` ? Mossy paving
- `paved_dark` ? Dark paving
- `grass` / `grass_dark` / `patches` / `reset` ? Grass variants

### Position Parameters
- `from=X,Z,Y` - Center position (NOTE: order is X,Z,Y not X,Y,Z!)
- `to=X,Z,Y` - End position (for slope)
- `angle=A` - Rotation in degrees
- `offset=F,R,U` - Forward, Right, Up offset
- `step=F,R,U` - Step-based offset

### Other Parameters
- `smooth=S` - Edge smoothing (0-1)
- `blockcheck` - Check for structures
- `within=MIN-MAX` - Altitude filter
- `chance=X` - Random chance (0-1)

---

## Our TerrainCommandParser

Must parse WEC syntax and extract:

```csharp
public class TerrainOperationParams
{
    public Vector3 Position;       // from=X,Z,Y
    public float Size;             // circle=R or rect width
    public float Size2;            // ring outer or frame inner
    public float Depth;            // rect depth
    public float Angle;            // angle=A
    public float Height;           // raise=H
    public float Smooth;           // smooth=S
    public string PaintType;       // paint=TYPE
    public bool IsLevel;           // level operation
    public bool IsRaise;           // raise operation
    public bool IsSmooth;          // smooth operation
    public bool IsPaint;           // paint operation
    public bool IsReset;           // reset operation
}
```

### CRITICAL: Position Parsing

WEC uses `from=X,Z,Y` format (note Z before Y!):
```csharp
// CORRECT parsing:
string[] parts = param.Split(',');
position = new Vector3(
    float.Parse(parts[0]),  // X
    float.Parse(parts[2]),  // Y (third value!)
    float.Parse(parts[1])   // Z (second value!)
);
```

---

## Integration Points for Custom Terrain

### 1. Check if Over Custom Terrain
```csharp
public static bool IsPositionOverCustomTerrain(Vector3 pos, float radius)
{
    // Check DirtFloorController.AllDirtFloors
    // Check DirtFloorMeshCombiner.AllCombiners bounds
    return hasCustomTerrainAtPosition;
}
```

### 2. Intercept Commands
```csharp
[HarmonyPatch(typeof(Terminal), "TryRunCommand")]
static bool Prefix(string text)
{
    if (!text.StartsWith("terrain")) return true;
    var pars = TerrainCommandParser.Parse(text);
    if (!IsPositionOverCustomTerrain(pars.Position, pars.Size)) return true;
    
    TerrainOperationExecutor.Execute(pars);
    return false; // Skip vanilla
}
```

### 3. Intercept TerrainOp
```csharp
[HarmonyPatch(typeof(TerrainOp), "Awake")]
static bool Prefix(TerrainOp __instance)
{
    if (!IsPositionOverCustomTerrain(__instance.transform.position, 4f)) return true;
    
    // Handle with our system
    HandleTerrainOp(__instance);
    Object.Destroy(__instance.gameObject);
    return false;
}
```

---

## Common Issues and Solutions

### Issue: Height at Sea Level (Y?0)

**Cause:** Raycast hit water collider instead of terrain

**Solution:** Validate height in LevelArea:
```csharp
if (height < 5f) // Suspiciously low
{
    // Get actual ground height from heightmap or nearby disks
    height = GetActualGroundHeight(position);
}
```

### Issue: Paint Not Showing

**Cause:** Paint mask not applied to material

**Solution:** Ensure after painting:
```csharp
_terrainMaterial.SetTexture("_ClearedMaskTex", _combinedPaintMask);
```

### Issue: Collision Offset from Visual

**Cause:** Combiner transform.position doesn't match mesh vertex positions

**Solution:** Mesh vertices are LOCAL space. Collision must be at same transform.
```csharp
// Mesh vertex Y is relative to transform.position.y
// Collision mesh must have same transform as render mesh
```
