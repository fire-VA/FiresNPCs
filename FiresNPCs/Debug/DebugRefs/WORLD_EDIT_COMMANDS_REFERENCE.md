# World Edit Commands Terrain System Reference

## Overview

This document explains how WEC processes terrain commands and where our custom terrain 
integration needs to hook in.

**Last Updated:** Contains full decompiled source from WEC for Terrain, TerrainCommand, 
TerrainParameters, UndoTerrain, and related classes.

---

## Full Decompiled Source Code

### TerrainNode (Base Class)

```csharp
namespace WorldEditCommands;

public abstract class TerrainNode
{
    public int Index;
    public Vector3 Position;
    public float DistanceWidth;
    public float DistanceDepth;
    public float Distance;
    public TerrainComp? Compiler;
}
```

### HeightNode

```csharp
namespace WorldEditCommands;

public class HeightNode : TerrainNode
{
    // Inherits all from TerrainNode
    // Used for height operations (level, raise, smooth, slope)
}
```

### PaintNode

```csharp
namespace WorldEditCommands;

public class PaintNode : TerrainNode
{
    // Inherits all from TerrainNode
    // Used for paint operations
}
```

### HeightUndoData

```csharp
namespace WorldEditCommands;

public class HeightUndoData
{
    public bool HeightModified;
    public float Level;
    public float Smooth;
    public int Index = -1;
}
```

### PaintUndoData

```csharp
namespace WorldEditCommands;

public class PaintUndoData
{
    public bool PaintModified;
    public Color Paint = Color.black;
    public int Index = -1;
}
```

### TerrainUndoData

```csharp
namespace WorldEditCommands;

public class TerrainUndoData
{
    public List<HeightUndoData> Heights = new List<HeightUndoData>();
    public List<PaintUndoData> Paints = new List<PaintUndoData>();
}
```

### UndoTerrain

```csharp
namespace WorldEditCommands;

public class UndoTerrain : IUndoAction
{
    private readonly Dictionary<Vector3, TerrainUndoData> Before;
    private readonly Dictionary<Vector3, TerrainUndoData> After;
    public Vector3 Position;
    public float Radius;

    public UndoTerrain(
        Dictionary<Vector3, TerrainUndoData> before,
        Dictionary<Vector3, TerrainUndoData> after,
        Vector3 position,
        float radius)
    {
        Before = before;
        After = after;
        Position = position;
        Radius = radius;
    }

    public string Undo()
    {
        Terrain.ApplyData(Before, Position, Radius);
        return "Undoing terrain changes";
    }

    public string Redo()
    {
        Terrain.ApplyData(After, Position, Radius);
        return "Redoing terrain changes";
    }
}
```

---

## Key Classes

### TerrainParameters (FULL DECOMPILED)

```csharp
namespace WorldEditCommands;

public class TerrainParameters
{
    public Vector3 Position = Vector3.zero;
    public Vector3 Offset = Vector3.zero;
    public Vector3 Step = Vector3.zero;
    public float Size;
    public Range<float>? Radius;
    public Range<float>? Width;
    public Range<float>? Depth;
    public string[] IncludedIds = Array.Empty<string>();
    public string[] ExcludedIds = Array.Empty<string>();
    public float Angle;
    public float? Set;
    public bool Reset;
    public bool Void;
    public float? Delta;
    public float? Level;
    public float? Min;
    public float? Max;
    public float Smooth;
    public float? Slope;
    public float SlopeAngle;
    public string Paint = "";
    public bool FixedPosition;
    public bool FixedAngle;
    public BlockCheck BlockCheck;
    public Range<float>? Within;
    public float Chance = 1f;

    public TerrainParameters(Terminal.ConsoleEventArgs args)
    {
        if ((bool)Player.m_localPlayer)
        {
            float num = 0.7853982f; // 45 degrees in radians
            Position = Player.m_localPlayer.transform.position;
            Angle = num * Mathf.Round(Player.m_localPlayer.transform.rotation.eulerAngles.y / 45f);
        }
        ParseArgs(args.Args);
    }

    private float ParseAngle(string value)
    {
        float num;
        switch (value)
        {
            case "n": num = 0.0f; break;
            case "ne": num = 45f; break;
            case "e": num = 90f; break;
            case "se": num = 135f; break;
            case "s": num = 180f; break;
            case "sw": num = 225f; break;
            case "w": num = 270f; break;
            case "nw": num = 315f; break;
            default: num = Parse.Float(value, 0.0f); break;
        }
        return num * (Mathf.PI / 180f);
    }

    protected void ParseArgs(string[] args)
    {
        bool flag = true; // Need to get ground height
        
        // First pass: check for "from" parameter
        foreach (string str in args)
        {
            string[] strArray = str.Split('=');
            string lower1 = strArray[0].ToLower();
            
            if (strArray.Length >= 2)
            {
                string lower2 = strArray[1].ToLower();
                if (lower1 == "from")
                {
                    FixedPosition = true;
                    flag = Parse.Split(lower2, ',').Length < 3; // No Y provided
                    Position = Parse.VectorXZY(Parse.Split(lower2, ','));
                }
            }
        }
        
        // CRITICAL: Get ground height if Y not provided
        if (flag)
        {
            Position.y = ZoneSystem.instance.IsZoneLoaded(Position) 
                ? ZoneSystem.instance.GetGroundHeight(Position) 
                : throw new InvalidOperationException("Unable to find ground height. Use from with Y coordinate.");
        }
        
        // Second pass: parse all other parameters
        foreach (string str in args)
        {
            string[] strArray1 = str.Split('=');
            string lower3 = strArray1[0].ToLower();
            
            // Flag-style parameters (no value)
            if (lower3 == "reset") Reset = true;
            if (lower3 == "void") Void = true;
            if (lower3 == "delta") Set = 0.0f;
            if (lower3 == "level") Level = Position.y;
            if (lower3 == "blockcheck") BlockCheck = BlockCheck.On;
            if (lower3 == "circle") Radius = new Range<float>(0.0f);
            if (lower3 == "rect")
            {
                Width = new Range<float>(0.0f);
                Depth = new Range<float>(0.0f);
            }
            if (lower3 == "slope") Slope = 0.0f;
            
            // Value parameters
            if (strArray1.Length >= 2)
            {
                string lower4 = strArray1[1].ToLower();
                string[] strArray2 = Parse.Split(lower4, ',');
                
                if (lower3 == "chance") Chance = Helper.RandomValue(Parse.FloatRange(lower4, 1f));
                if (lower3 == "id") IncludedIds = strArray2;
                if (lower3 == "ignore") ExcludedIds = strArray2;
                if (lower3 == "circle") Radius = Parse.FloatRange(lower4, 0.0f);
                if (lower3 == "rect")
                {
                    Range<Vector3> range = Parse.ScaleRange(lower4);
                    Width = new Range<float>(range.Min.x, range.Max.x);
                    Depth = new Range<float>(range.Min.z, range.Max.z);
                }
                if (lower3 == "paint") Paint = lower4;
                if (lower3 == "angle")
                {
                    FixedAngle = true;
                    Angle = ParseAngle(lower4);
                }
                if (lower3 == "delta") Set = Parse.Float(lower4, 0.0f);
                if (lower3 == "min") Min = Parse.Float(lower4, float.MinValue);
                if (lower3 == "max") Max = Parse.Float(lower4, float.MaxValue);
                if (lower3 == "raise") Delta = Parse.Float(lower4, 0.0f);
                if (lower3 == "lower") Delta = -Parse.Float(lower4, 0.0f);
                if (lower3 == "smooth") Smooth = Parse.Float(lower4, 0.0f);
                if (lower3 == "slope")
                {
                    Slope = Parse.Float(strArray2, 0, 0.0f);
                    if (strArray2.Length > 1) SlopeAngle = ParseAngle(strArray2[1]);
                }
                if (lower3 == "offset") Offset = Parse.VectorZXY(strArray2);
                if (lower3 == "within") Within = Parse.FloatRange(lower4, 0.0f);
                if (lower3 == "level") Level = Parse.Float(lower4, Position.y);
                if (lower3 == "step") Step = Parse.VectorZXY(strArray2);
                if (lower3 == "blockcheck")
                {
                    switch (lower4)
                    {
                        case "on": BlockCheck = BlockCheck.On; break;
                        case "inverse": BlockCheck = BlockCheck.Inverse; break;
                        case "off": BlockCheck = BlockCheck.Off; break;
                        default: throw new InvalidOperationException($"Invalid value {lower4} for blockcheck.");
                    }
                }
            }
        }
        
        HandleTo(args);
        
        // Validation
        if (Radius != null && Depth != null)
            throw new InvalidOperationException("circle and rect can't be used together.");
        if (Radius == null && Depth == null)
            throw new InvalidOperationException("circle or rect parameter must be used.");
        
        // Calculate Size
        if (Radius != null)
            Size = Radius.Max;
        if (Depth != null && Width != null)
            Size = Mathf.Max(Depth.Max, Width.Max);
        
        // Handle step offset
        if (Step != Vector3.zero)
        {
            float num1 = Size;
            float num2 = Size;
            if (Width != null) num1 = Width.Max;
            if (Depth != null) num2 = Depth.Max;
            Offset.x += Step.x * num1 * 2.0f;
            Offset.z += Step.z * num2 * 2.0f;
            if (Slope.HasValue)
            {
                Offset.y = Slope.Value * (Step.z + Step.y);
                if (Level.HasValue)
                    Level = Level.Value + Offset.y - 0.5f * Slope.Value;
            }
        }
        
        // Apply rotation to offset
        if (Offset != Vector3.zero)
        {
            Vector3 offset = Offset;
            Offset.x = Mathf.Cos(Angle) * offset.x + Mathf.Sin(Angle) * offset.z;
            Offset.z = Mathf.Cos(Angle) * offset.z - Mathf.Sin(Angle) * offset.x;
            Position += Offset;
        }
        
        if (Radius != null)
            SlopeAngle += Angle;
    }

    private void HandleTo(string[] args)
    {
        foreach (string str in args)
        {
            string[] strArray = str.Split('=');
            string lower1 = strArray[0].ToLower();
            
            if (strArray.Length >= 2 && lower1 == "to")
            {
                string lower2 = strArray[1].ToLower();
                Vector3 target = Parse.VectorXZY(Parse.Split(lower2, ','));
                
                // Get ground height for target if Y not provided
                if (Slope.GetValueOrDefault() == 0.0f && Parse.Split(lower2, ',').Length < 3)
                {
                    target.y = ZoneSystem.instance.IsZoneLoaded(target) 
                        ? ZoneSystem.instance.GetGroundHeight(target) 
                        : throw new InvalidOperationException("Unable to find ground height for 'to'.");
                }
                
                float dist = Utils.DistanceXZ(Position, target);
                
                // Update radius to half distance
                if (Radius != null)
                    Radius = new Range<float>(dist / 2f);
                if (Width?.Max == 0.0f)
                    Width = new Range<float>(dist / 2f);
                if (Depth != null)
                    Depth = new Range<float>(dist / 2f);
                
                // Calculate angle from position to target
                FixedAngle = true;
                Angle = Vector3.SignedAngle(Vector3.forward, Utils.DirectionXZ(target - Position), Vector3.up) * Mathf.PI / 180f;
                
                // Center position between from and to
                Position.x = (Position.x + target.x) / 2f;
                Position.z = (Position.z + target.z) / 2f;
                
                // Handle slope
                if (Slope.HasValue)
                {
                    if (Slope.Value == 0.0f)
                        Slope = target.y - Position.y;
                    Position.y += Slope.Value / 2f;
                }
            }
        }
    }
}
```

### Terrain (Core Operations - FULL DECOMPILED)

```csharp
namespace WorldEditCommands;

public class Terrain
{
    // Smooth falloff calculation (0 at edge, 1 at center)
    private static float CalculateSmooth(float smooth, float distance)
    {
        return 1.0 - distance < smooth ? (1f - distance) / smooth : 1f;
    }

    // Slope height offset calculation
    private static float CalculateSlope(float angle, float distanceWidth, float distanceDepth)
    {
        return Mathf.Sin(angle) * distanceWidth + Mathf.Cos(angle) * distanceDepth;
    }

    // Set terrain to absolute delta from original
    public static void SetTerrain(List<HeightNode> nodes, Vector3 pos, float radius, float smooth, float delta)
    {
        DoOperation(nodes, pos, radius, (compiler, index, node) =>
        {
            float smoothFactor = CalculateSmooth(smooth, node.Distance);
            compiler.m_levelDelta[index] = delta * smoothFactor;
            compiler.m_smoothDelta[index] = 0.0f;
            compiler.m_modifiedHeight[index] = compiler.m_levelDelta[index] != 0.0;
        });
    }

    // Raise/lower terrain by amount
    public static void RaiseTerrain(List<HeightNode> nodes, Vector3 pos, float radius, float smooth, float amount)
    {
        DoOperation(nodes, pos, radius, (compiler, index, node) =>
        {
            float smoothFactor = CalculateSmooth(smooth, node.Distance);
            compiler.m_levelDelta[index] += smoothFactor * amount + compiler.m_smoothDelta[index];
            compiler.m_smoothDelta[index] = 0.0f;
            compiler.m_modifiedHeight[index] = compiler.m_levelDelta[index] != 0.0;
        });
    }

    // Remove all terrain (creates holes)
    public static void VoidTerrain(List<HeightNode> nodes, Vector3 pos, float radius)
    {
        DoOperation(nodes, pos, radius, (compiler, index, node) =>
        {
            compiler.m_levelDelta[index] = float.NaN;
            compiler.m_smoothDelta[index] = 0.0f;
            compiler.m_modifiedHeight[index] = true;
        });
    }

    // Level terrain to specific altitude
    public static void LevelTerrain(List<HeightNode> nodes, Vector3 pos, float radius, float smooth, float altitude)
    {
        DoOperation(nodes, pos, radius, (compiler, index, node) =>
        {
            float smoothFactor = CalculateSmooth(smooth, node.Distance);
            // KEY: Uses original heightmap height as baseline
            compiler.m_levelDelta[index] += smoothFactor * (altitude - compiler.m_hmap.m_heights[index]);
            compiler.m_smoothDelta[index] = 0.0f;
            compiler.m_modifiedHeight[index] = compiler.m_levelDelta[index] != 0.0;
        });
    }

    // Clamp terrain to maximum altitude
    public static void MaxTerrain(List<HeightNode> nodes, Vector3 pos, float radius, float altitude)
    {
        DoOperation(nodes, pos, radius, (compiler, index, node) =>
        {
            float num = Mathf.Min(altitude, compiler.m_hmap.m_heights[index]);
            compiler.m_levelDelta[index] += num - compiler.m_hmap.m_heights[index];
            compiler.m_smoothDelta[index] = 0.0f;
            compiler.m_modifiedHeight[index] = compiler.m_levelDelta[index] != 0.0;
        });
    }

    // Clamp terrain to minimum altitude
    public static void MinTerrain(List<HeightNode> nodes, Vector3 pos, float radius, float altitude)
    {
        DoOperation(nodes, pos, radius, (compiler, index, node) =>
        {
            float num = Mathf.Max(altitude, compiler.m_hmap.m_heights[index]);
            compiler.m_levelDelta[index] += num - compiler.m_hmap.m_heights[index];
            compiler.m_smoothDelta[index] = 0.0f;
            compiler.m_modifiedHeight[index] = compiler.m_levelDelta[index] != 0.0;
        });
    }

    // Create sloped terrain
    public static void SlopeTerrain(List<HeightNode> nodes, Vector3 pos, float radius, float angle, float smooth, float altitude, float amount)
    {
        DoOperation(nodes, pos, radius, (compiler, index, node) =>
        {
            float slopeOffset = CalculateSlope(angle, node.DistanceWidth, node.DistanceDepth) * CalculateSmooth(smooth, node.Distance);
            compiler.m_levelDelta[index] += altitude - compiler.m_hmap.m_heights[index] + slopeOffset * amount / 2.0f;
            compiler.m_smoothDelta[index] = 0.0f;
            compiler.m_modifiedHeight[index] = compiler.m_levelDelta[index] != 0.0;
        });
    }

    // Paint terrain with color
    public static void PaintTerrain(List<PaintNode> nodes, Vector3 pos, float radius, float smooth, Color color)
    {
        DoOperation(nodes, pos, radius, (compiler, index, node) =>
        {
            float smoothFactor = CalculateSmooth(smooth, node.Distance);
            Color newColor = Color.Lerp(compiler.m_paintMask[index], color, smoothFactor);
            newColor.a = color.a;
            compiler.m_paintMask[index] = newColor;
            compiler.m_modifiedPaint[index] = true;
        });
    }

    // Clear paint modifications
    public static void ClearPaint(List<PaintNode> nodes, Vector3 pos, float radius)
    {
        DoOperation(nodes, pos, radius, (compiler, index, node) =>
        {
            compiler.m_modifiedPaint[index] = false;
        });
    }

    // Reset terrain to original
    public static void ResetTerrain(List<HeightNode> heightNodes, List<PaintNode> paintNodes, Vector3 pos, float radius)
    {
        // Remove terrain modifiers (hoe, cultivator placements)
        List<TerrainModifier> modifiers = new List<TerrainModifier>();
        TerrainModifier.GetModifiers(pos, radius + 1f, modifiers);
        foreach (TerrainModifier tm in modifiers)
        {
            if (tm.m_nview != null)
            {
                tm.m_nview.ClaimOwnership();
                ZNetScene.instance.Destroy(tm.gameObject);
            }
        }
        
        // Reset height deltas
        DoOperation(heightNodes, pos, radius, (compiler, index, node) =>
        {
            compiler.m_levelDelta[index] = 0.0f;
            compiler.m_smoothDelta[index] = 0.0f;
            compiler.m_modifiedHeight[index] = false;
        });
        
        // Clear paint
        ClearPaint(paintNodes, pos, radius);
    }

    // Execute operation on paint nodes
    private static void DoOperation(List<PaintNode> nodes, Vector3 pos, float radius, Action<TerrainComp, int, TerrainNode> action)
    {
        foreach (PaintNode node in nodes)
            action(node.Compiler, node.Index, node);
        ClutterSystem.instance?.ResetGrass(pos, radius);
    }

    // Execute operation on height nodes
    private static void DoOperation(List<HeightNode> nodes, Vector3 pos, float radius, Action<TerrainComp, int, TerrainNode> action)
    {
        foreach (HeightNode node in nodes)
            action(node.Compiler, node.Index, node);
        ClutterSystem.instance?.ResetGrass(pos, radius);
    }

    // Capture current terrain state for undo
    public static Dictionary<Vector3, TerrainUndoData> GetData(List<HeightNode> heightNodes, List<PaintNode> paintNodes)
    {
        Dictionary<Vector3, TerrainUndoData> data = new Dictionary<Vector3, TerrainUndoData>();
        
        foreach (HeightNode node in heightNodes)
        {
            TerrainComp compiler = node.Compiler;
            if (compiler == null) continue;
            
            Vector3 position = compiler.transform.position;
            if (!data.ContainsKey(position))
                data.Add(position, new TerrainUndoData());
            
            data[position].Heights.Add(new HeightUndoData
            {
                Index = node.Index,
                HeightModified = compiler.m_modifiedHeight[node.Index],
                Level = compiler.m_levelDelta[node.Index],
                Smooth = compiler.m_smoothDelta[node.Index]
            });
        }
        
        foreach (PaintNode node in paintNodes)
        {
            TerrainComp compiler = node.Compiler;
            if (compiler == null) continue;
            
            Vector3 position = compiler.transform.position;
            if (!data.ContainsKey(position))
                data.Add(position, new TerrainUndoData());
            
            data[position].Paints.Add(new PaintUndoData
            {
                Index = node.Index,
                PaintModified = compiler.m_modifiedPaint[node.Index],
                Paint = compiler.m_paintMask[node.Index]
            });
        }
        
        return data;
    }

    // Apply saved terrain state (for undo/redo)
    public static void ApplyData(Dictionary<Vector3, TerrainUndoData> data, Vector3 pos, float radius)
    {
        foreach (var kvp in data)
        {
            TerrainComp compiler = TerrainComp.FindTerrainCompiler(kvp.Key);
            if (compiler == null) continue;
            
            foreach (HeightUndoData height in kvp.Value.Heights)
            {
                compiler.m_smoothDelta[height.Index] = height.Smooth;
                compiler.m_levelDelta[height.Index] = height.Level;
                compiler.m_modifiedHeight[height.Index] = height.HeightModified;
            }
            
            foreach (PaintUndoData paint in kvp.Value.Paints)
            {
                compiler.m_modifiedPaint[paint.Index] = paint.PaintModified;
                compiler.m_paintMask[paint.Index] = paint.Paint;
            }
            
            Save(compiler);
        }
        
        ClutterSystem.instance?.ResetGrass(pos, radius);
    }

    // Save terrain changes to ZDO
    public static void Save(TerrainComp compiler)
    {
        compiler.GetComponent<ZNetView>()?.ClaimOwnership();
        ++compiler.m_operations;
        compiler.m_lastOpPoint = Vector3.zero;
        compiler.m_lastOpRadius = 0.0f;
        compiler.Save();
        compiler.m_hmap.Poke(false);
    }

    // Get TerrainComp instances for circular area
    public static TerrainComp[] GetCompilers(Vector3 position, Range<float> radius)
    {
        List<Heightmap> heightmaps = new List<Heightmap>();
        Heightmap.FindHeightmap(position, radius.Max + 1f, heightmaps);
        Vector3 refPos = ZNet.instance.GetReferencePosition();
        
        return heightmaps
            .Where(hmap => ZNetScene.InActiveArea(ZoneSystem.GetZone(hmap.transform.position), refPos))
            .Select(hmap => hmap.GetAndCreateTerrainCompiler())
            .ToArray();
    }

    // Get TerrainComp instances for rectangular area
    public static TerrainComp[] GetCompilers(Vector3 position, Range<float> width, Range<float> depth, float angle)
    {
        List<Heightmap> heightmaps = new List<Heightmap>();
        float maxDist = Mathf.Max(width.Max, depth.Max) * (Mathf.Abs(Mathf.Sin(angle)) + Mathf.Abs(Mathf.Cos(angle)));
        Heightmap.FindHeightmap(position, maxDist + 1f, heightmaps);
        Vector3 refPos = ZNet.instance.GetReferencePosition();
        
        return heightmaps
            .Where(hmap => ZNetScene.InActiveArea(ZoneSystem.GetZone(hmap.transform.position), refPos))
            .Select(hmap => hmap.GetAndCreateTerrainCompiler())
            .ToArray();
    }

    // Create filter for blocking structures
    public static Func<TerrainNode, bool> CreateBlockCheckFilter(BlockCheck blockCheck, string[] includedIds, string[] excludedIds)
    {
        HashSet<int> included = Selector.GetPrefabs(includedIds);
        HashSet<int> excluded = Selector.GetAllPrefabs(excludedIds);
        ZoneSystem zs = ZoneSystem.instance;
        
        return (node) =>
        {
            if (blockCheck == BlockCheck.Off) return true;
            
            Vector3 position = node.Position;
            position.y += 2000f;
            
            bool hasBlocker = Physics.RaycastAll(position, Vector3.down, 10000f, zs.m_blockRayMask)
                .Select(hit => Selector.GetPrefabFromHit(hit))
                .Any(prefab => included.Contains(prefab) && !excluded.Contains(prefab));
            
            return (!hasBlocker || blockCheck != BlockCheck.On) && (hasBlocker || blockCheck != BlockCheck.Inverse);
        };
    }

    // Create filter for altitude range
    public static Func<TerrainNode, bool> CreateAltitudeFilter(float min, float max)
    {
        return (node) =>
        {
            float groundHeight = ZoneSystem.instance.GetGroundHeight(node.Position);
            return groundHeight >= min && groundHeight <= max;
        };
    }

    // Get height nodes within a circle
    public static void GetHeightNodesWithCircle(List<HeightNode> nodes, TerrainComp compiler, Vector3 centerPos, Range<float> radius)
    {
        if (radius.Max == 0.0) return;
        
        int resolution = compiler.m_width + 1;
        
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                Vector3 world = VertexToWorld(compiler.m_hmap, x, z);
                float dx = world.x - centerPos.x;
                float dz = world.z - centerPos.z;
                float dist = Utils.DistanceXZ(centerPos, world);
                
                if (Helper.Within(radius, dist))
                {
                    nodes.Add(new HeightNode
                    {
                        Index = z * resolution + x,
                        Position = world,
                        DistanceWidth = dx / radius.Max,
                        DistanceDepth = dz / radius.Max,
                        Distance = dist / radius.Max,
                        Compiler = compiler
                    });
                }
            }
        }
    }

    // Convert heightmap vertex to world position
    private static Vector3 VertexToWorld(Heightmap hmap, int x, int z)
    {
        Vector3 position = hmap.transform.position;
        position.x += (x - hmap.m_width / 2) * hmap.m_scale;
        position.z += (z - hmap.m_width / 2) * hmap.m_scale;
        return position;
    }

    // Rotation helpers
    private static float GetX(float x, float z, float angle)
    {
        return Mathf.Cos(angle) * x - Mathf.Sin(angle) * z;
    }

    private static float GetZ(float x, float z, float angle)
    {
        return Mathf.Sin(angle) * x + Mathf.Cos(angle) * z;
    }

    // Get height nodes within a rectangle
    public static void GetHeightNodesWithRect(List<HeightNode> nodes, TerrainComp compiler, Vector3 centerPos, Range<float> width, Range<float> depth, float angle)
    {
        if (width.Max == 0.0 || depth.Max == 0.0) return;
        
        int resolution = compiler.m_width + 1;
        
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                Vector3 world = VertexToWorld(compiler.m_hmap, x, z);
                float dx = world.x - centerPos.x;
                float dz = world.z - centerPos.z;
                float rotX = GetX(dx, dz, angle);
                float rotZ = GetZ(dx, dz, angle);
                
                if (Helper.Within(width, depth, Mathf.Abs(rotX), Mathf.Abs(rotZ)))
                {
                    float normX = rotX / width.Max;
                    float normZ = rotZ / depth.Max;
                    
                    nodes.Add(new HeightNode
                    {
                        Index = z * resolution + x,
                        Position = world,
                        DistanceWidth = normX,
                        DistanceDepth = normZ,
                        Distance = Mathf.Max(Mathf.Abs(normX), Mathf.Abs(normZ)),
                        Compiler = compiler
                    });
                }
            }
        }
    }

    // Get paint nodes within a rectangle
    public static void GetPaintNodesWithRect(List<PaintNode> nodes, TerrainComp compiler, Vector3 centerPos, Range<float> width, Range<float> depth, float angle)
    {
        int resolution = compiler.m_width + 1;
        
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                Vector3 world = VertexToWorld(compiler.m_hmap, x, z);
                float dx = world.x - centerPos.x;
                float dz = world.z - centerPos.z;
                float rotX = GetX(dx, dz, angle);
                float rotZ = GetZ(dx, dz, angle);
                
                if (Helper.Within(width, depth, Mathf.Abs(rotX), Mathf.Abs(rotZ)))
                {
                    float normX = rotX / width.Max;
                    float normZ = rotZ / depth.Max;
                    
                    nodes.Add(new PaintNode
                    {
                        Index = z * resolution + x,
                        Position = world,
                        DistanceWidth = normX,
                        DistanceDepth = normZ,
                        Distance = Mathf.Max(Mathf.Abs(normX), Mathf.Abs(normZ)),
                        Compiler = compiler
                    });
                }
            }
        }
    }

    // Get paint nodes within a circle
    public static void GetPaintNodesWithCircle(List<PaintNode> nodes, TerrainComp compiler, Vector3 centerPos, Range<float> radius)
    {
        int resolution = compiler.m_width + 1;
        
        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                Vector3 world = VertexToWorld(compiler.m_hmap, x, z);
                float dx = world.x - centerPos.x;
                float dz = world.z - centerPos.z;
                float dist = Utils.DistanceXZ(centerPos, world);
                
                if (Helper.Within(radius, dist))
                {
                    nodes.Add(new PaintNode
                    {
                        Index = z * resolution + x,
                        Position = world,
                        DistanceWidth = dx / radius.Max,
                        DistanceDepth = dz / radius.Max,
                        Distance = dist / radius.Max,
                        Compiler = compiler
                    });
                }
            }
        }
    }
}
```

### TerrainCommand (FULL DECOMPILED)

```csharp
namespace WorldEditCommands;

public class TerrainCommand
{
    public const string Name = "terrain";
    
    // Paint color definitions
    public static Dictionary<string, Color> Paints = new Dictionary<string, Color>()
    {
        { "grass", Color.black },
        { "patches", new Color(0.0f, 0.75f, 0.0f) },
        { "grass_dark", new Color(0.6f, 0.5f, 0.0f) },
        { "dirt", Color.red },
        { "cultivated", Color.green },
        { "paved", Color.blue },
        { "paved_moss", new Color(0.0f, 0.0f, 0.5f) },
        { "paved_dirt", new Color(1f, 0.0f, 0.5f) },
        { "paved_dark", new Color(0.0f, 1f, 0.5f) }
    };

    private TerrainComp[] GetCompilers(TerrainParameters pars)
    {
        if (pars.Radius != null)
            return Terrain.GetCompilers(pars.Position, pars.Radius);
        if (pars.Width != null && pars.Depth != null)
            return Terrain.GetCompilers(pars.Position, pars.Width, pars.Depth, pars.Angle);
        throw new InvalidOperationException("Unable to select any terrain");
    }

    private List<HeightNode> GetHeightNodes(TerrainParameters pars, IEnumerable<TerrainComp> compilers)
    {
        List<HeightNode> heightNodes = new List<HeightNode>();
        foreach (TerrainComp compiler in compilers)
        {
            if (pars.Radius != null)
                Terrain.GetHeightNodesWithCircle(heightNodes, compiler, pars.Position, pars.Radius);
            if (pars.Width != null && pars.Depth != null)
                Terrain.GetHeightNodesWithRect(heightNodes, compiler, pars.Position, pars.Width, pars.Depth, pars.Angle);
        }
        if (pars.Chance < 1.0)
            heightNodes = heightNodes.Where(n => Random.value < pars.Chance).ToList();
        return heightNodes;
    }

    private List<PaintNode> GetPaintNodes(TerrainParameters pars, IEnumerable<TerrainComp> compilers)
    {
        List<PaintNode> paintNodes = new List<PaintNode>();
        foreach (TerrainComp compiler in compilers)
        {
            if (pars.Radius != null)
                Terrain.GetPaintNodesWithCircle(paintNodes, compiler, pars.Position, pars.Radius);
            if (pars.Width != null && pars.Depth != null)
                Terrain.GetPaintNodesWithRect(paintNodes, compiler, pars.Position, pars.Width, pars.Depth, pars.Angle);
        }
        if (pars.Chance < 1.0)
            paintNodes = paintNodes.Where(n => Random.value < pars.Chance).ToList();
        return paintNodes;
    }

    private List<Func<TerrainNode, bool>> GetFilterers(TerrainParameters pars)
    {
        List<Func<TerrainNode, bool>> filterers = new List<Func<TerrainNode, bool>>();
        if (pars.BlockCheck != BlockCheck.Off)
            filterers.Add(Terrain.CreateBlockCheckFilter(pars.BlockCheck, pars.IncludedIds, pars.ExcludedIds));
        if (pars.Within != null)
            filterers.Add(Terrain.CreateAltitudeFilter(pars.Within.Min, pars.Within.Max));
        return filterers;
    }

    public TerrainCommand()
    {
        TerrainAutoComplete autoComplete = new TerrainAutoComplete();
        
        Helper.Command("terrain", "Manipulates the terrain.", (args) =>
        {
            TerrainParameters pars = new TerrainParameters(args);
            TerrainComp[] compilers = GetCompilers(pars);
            List<Func<TerrainNode, bool>> filterers = GetFilterers(pars);
            
            List<HeightNode> heightNodes = GetHeightNodes(pars, compilers)
                .Where(n => filterers.All(f => f(n))).ToList();
            List<PaintNode> paintNodes = GetPaintNodes(pars, compilers)
                .Where(n => filterers.All(f => f(n))).ToList();
            
            // Capture BEFORE state for undo
            Dictionary<Vector3, TerrainUndoData> beforeData = Terrain.GetData(heightNodes, paintNodes);
            
            // Apply operations in order
            if (pars.Reset)
                Terrain.ResetTerrain(heightNodes, paintNodes, pars.Position, pars.Size);
            if (pars.Set.HasValue)
                Terrain.SetTerrain(heightNodes, pars.Position, pars.Size, pars.Smooth, pars.Set.Value);
            if (pars.Slope.HasValue && !pars.Level.HasValue)
                Terrain.SlopeTerrain(heightNodes, pars.Position, pars.Size, pars.SlopeAngle, pars.Smooth, pars.Position.y, pars.Slope.Value);
            if (pars.Level.HasValue)
                Terrain.LevelTerrain(heightNodes, pars.Position, pars.Size, pars.Smooth, pars.Level.Value);
            if (pars.Delta.HasValue)
                Terrain.RaiseTerrain(heightNodes, pars.Position, pars.Size, pars.Smooth, pars.Delta.Value);
            if (pars.Min.HasValue)
                Terrain.MinTerrain(heightNodes, pars.Position, pars.Size, pars.Min.Value);
            if (pars.Max.HasValue)
                Terrain.MaxTerrain(heightNodes, pars.Position, pars.Size, pars.Max.Value);
            if (pars.Void)
                Terrain.VoidTerrain(heightNodes, pars.Position, pars.Size);
            
            // Handle paint operation
            if (pars.Paint != "")
            {
                string[] parts = pars.Paint.Split(',');
                if (parts.Length > 2)
                {
                    // Custom color: paint=r,g,b,a
                    Color color = new Color(
                        Parse.Float(parts, 0, 0.0f),
                        Parse.Float(parts, 1, 0.0f),
                        Parse.Float(parts, 2, 0.0f),
                        Parse.Float(parts, 3, 1f));
                    Terrain.PaintTerrain(paintNodes, pars.Position, pars.Size, pars.Smooth, color);
                }
                else
                {
                    // Named color
                    if (Paints.TryGetValue(pars.Paint, out Color namedColor))
                        Terrain.PaintTerrain(paintNodes, pars.Position, pars.Size, pars.Smooth, namedColor);
                }
            }
            
            // Save all modified TerrainComps
            foreach (TerrainComp compiler in compilers)
                Terrain.Save(compiler);
            
            // Capture AFTER state and add to undo
            Dictionary<Vector3, TerrainUndoData> afterData = Terrain.GetData(heightNodes, paintNodes);
            UndoManager.Add(new UndoTerrain(beforeData, afterData, pars.Position, pars.Size));
        });
    }
}
```

### BaseParameters (Abstract Base)

```csharp
namespace WorldEditCommands;

public abstract class BaseParameters
{
    public Vector3 From;
    public Vector3? Center;
    public string[] ConnectionIds;
    public string[] IncludedIds;
    public string[] ExcludedIds;
    public float Angle;
    public Range<float>? Radius;
    public Range<float>? Width;
    public Range<float>? Depth;
    public float Height;
    public float Chance;
    public string Match;
    public string Unmatch;
    public bool Connect;
    public HashSet<string> Components;
    public Dictionary<string, string> DataParameters;
    private readonly Dictionary<string, Type> SupportedOperations;
    private Terminal terminal;
    public Dictionary<string, object?> Operations = new Dictionary<string, object>();
    public static Random Random = new Random();

    public BaseParameters(Dictionary<string, Type> supportedOperations)
    {
        SupportedOperations = supportedOperations;
    }

    public void ParseCommand(Terminal.ConsoleEventArgs args)
    {
        if ((bool)Player.m_localPlayer)
            From = Player.m_localPlayer.transform.position;
        terminal = args.Context;
        ParseArgs(args.Args);
    }

    protected abstract void ParseArg(string arg);
    protected abstract void ParseArg(string arg, string value);

    protected virtual void ParseArgs(string[] args)
    {
        foreach (string str in args)
        {
            string[] parts = str.Split('=', 2);
            string lower = parts[0].ToLower();
            
            ParseArgInternal(lower);
            ParseArg(lower);
            
            if (parts.Length >= 2)
            {
                string value = parts[1].Trim();
                ParseArgInternal(lower, value);
                ParseArg(lower, value);
            }
        }
        
        // Validation
        if (Operations.Count == 0)
            throw new InvalidOperationException("Missing the operation.");
        if (Radius != null && Depth != null)
            throw new InvalidOperationException("circle and rect can't be used together.");
        if (Radius != null && Connect)
            throw new InvalidOperationException("circle and connect can't be used together.");
        if (Depth != null && Connect)
            throw new InvalidOperationException("connect and rect can't be used together.");
    }

    private void ParseArgInternal(string name)
    {
        if (SupportedOperations.TryGetValue(name, out Type type))
        {
            if (Operations.ContainsKey(name) && type != typeof(string[]))
                throw new InvalidOperationException($"Operation {name} used multiple times.");
            if (type == typeof(string[]))
            {
                if (!Operations.ContainsKey(name))
                    Operations.Add(name, new string[0]);
            }
            else
                Operations.Add(name, null);
        }
        if (name == "connect")
            Connect = true;
    }

    private void ParseArgInternal(string name, string value)
    {
        if (SupportedOperations.TryGetValue(name, out Type type))
        {
            if (type == typeof(int))
                Operations[name] = Parse.Int(value, 0);
            else if (type == typeof(float))
                Operations[name] = Parse.Float(value, 0.0f);
            else if (type == typeof(bool))
                Operations[name] = Parse.Boolean(value);
            else if (type == typeof(string[]))
                Operations[name] = ((string[])Operations[name]).Append(value).ToArray();
            else
                Operations[name] = value;
        }
        
        string[] values = Parse.Split(value, ',');
        
        if (name == "par")
        {
            var kvp = Parse.Kvp(value, ',');
            DataParameters[$"<{kvp.Key}>"] = kvp.Key != "" ? kvp.Value 
                : throw new InvalidOperationException($"Invalid data parameter {value}.");
        }
        if (name == "connect") ConnectionIds = values;
        if (name == "center" || name == "from") Center = Parse.VectorXZY(values);
        if (name == "id") IncludedIds = values;
        if (name == "ignore") ExcludedIds = values;
        if (name == "chance") Chance = Parse.Float(value, 1f);
        if (name == "type") AddComponents(values);
        if (name == "match") Match = value;
        if (name == "unmatch") Unmatch = value;
        if (name == "rect")
        {
            Range<Vector3> range = Parse.ScaleRange(value);
            Width = new Range<float>(range.Min.x, range.Max.x);
            Depth = new Range<float>(range.Min.z, range.Max.z);
        }
        if (name == "radius" || name == "range" || name == "circle")
            Radius = Parse.FloatRange(value, 0.0f);
        if (name == "height") Height = Parse.Float(value, 0.0f);
        if (name == "angle") Angle = Parse.Float(value, 0.0f) * Mathf.PI / 180f;
    }

    private void AddComponents(string[] values)
    {
        foreach (string str in values)
        {
            switch (str.ToLowerInvariant())
            {
                case "structure": Components.Add("WearNTear"); break;
                case "creature": Components.Add("Humanoid"); break;
                default: Components.Add(str); break;
            }
        }
    }

    public ZNetView[] GetObjects()
    {
        ZNetView[] source;
        
        if (Connect)
        {
            ZNetView hovered = Selector.GetHovered(50f, IncludedIds, Components, ExcludedIds);
            if (hovered == null) return Array.Empty<ZNetView>();
            source = Selector.GetConnected(hovered, ConnectionIds, IncludedIds, ExcludedIds);
        }
        else if (Radius != null)
        {
            source = Selector.GetNearby(IncludedIds, Components, ExcludedIds, Center ?? From, Radius, Height);
        }
        else if (Width != null && Depth != null)
        {
            source = Selector.GetNearby(IncludedIds, Components, ExcludedIds, Center ?? From, Angle, Width, Depth, Height);
        }
        else
        {
            ZNetView hovered = Selector.GetHovered(50f, IncludedIds, Components, ExcludedIds);
            if (hovered == null) return Array.Empty<ZNetView>();
            if (!Selector.GetPrefabs(IncludedIds).Contains(hovered.GetZDO().GetPrefab()))
            {
                Helper.AddMessage(terminal, $"Skipped: {hovered.name} has invalid id.", false);
                return Array.Empty<ZNetView>();
            }
            source = new ZNetView[1] { hovered };
        }
        
        // Apply filters
        DataEntry matchData = Match == "" ? null : DataHelper.Get(Match);
        DataEntry unmatchData = Unmatch == "" ? null : DataHelper.Get(Unmatch);
        
        return source.Where(view =>
        {
            if (view == null) return false;
            if (!view.GetZDO().IsValid())
            {
                terminal.AddString($"Skipped: {view.name} is not loaded.");
                return false;
            }
            if (matchData != null && !matchData.Match(DataParameters, view.GetZDO()))
            {
                terminal.AddString($"Skipped: {view.name} not matching filter.");
                return false;
            }
            if (unmatchData != null && !unmatchData.Unmatch(DataParameters, view.GetZDO()))
            {
                terminal.AddString($"Skipped: {view.name} matching filter.");
                return false;
            }
            if (!Roll(Chance))
            {
                terminal.AddString($"Skipped: {view.name} (chance).");
                return false;
            }
            return true;
        }).ToArray();
    }

    public static bool Roll(float value)
    {
        return value >= 1.0 || Random.NextDouble() < value;
    }
}
```

---

## The Height Problem

### Why IH Shows Correct Height But Terrain Places Wrong

**IH's Ruler/Ghost Position:**
- IH does its own raycast for placement
- The raycast CAN hit custom terrain (if on "terrain" layer)
- Ghost shows at correct height (Y=38)

**WEC's Position Calculation:**
- WEC uses `ZoneSystem.instance.GetGroundHeight()`
- This method ONLY queries vanilla `Heightmap`
- If no vanilla terrain at position, returns 0 or water level

**The Mismatch:**
```
IH Ghost at: (-5373, 38, 342)  ? From raycast hitting custom terrain
WEC Position: (-5373, 0.74, 342)  ? From GetGroundHeight() returning ocean level
```

### Where GetGroundHeight is Called

In `TerrainParameters.ParseArgs()`:
```csharp
foreach (string str in args)
{
    if (lower1 == "from")
    {
        this.FixedPosition = true;
        flag = Parse.Split(lower2, ',').Length < 3;  // No Y provided
        this.Position = Parse.VectorXZY(Parse.Split(lower2, ','));
    }
}

// After parsing "from":
if (flag)  // If no Y was provided
    this.Position.y = ZoneSystem.instance.GetGroundHeight(this.Position);
```

---

## Solution Options

### Option 1: Patch GetGroundHeight (Global Fix)

Patch `ZoneSystem.GetGroundHeight` to check custom terrain first:

```csharp
[HarmonyPatch(typeof(ZoneSystem), "GetGroundHeight")]
static class GetGroundHeight_Patch
{
    static bool Prefix(Vector3 p, ref float __result)
    {
        // Check custom terrain first
        var disk = DirtFloorController.GetDirtFloorAtPosition(p, 1f);
        if (disk != null)
        {
            __result = disk.transform.position.y;
            return false;  // Skip original
        }
        
        // Also check combiner mesh
        var combiner = DirtFloorMeshCombiner.GetCombinerAt(p);
        if (combiner != null)
        {
            float height = combiner.SampleHeightAt(p);
            if (height > -50f)
            {
                __result = height;
                return false;
            }
        }
        
        return true;  // Use vanilla
    }
}
```

### Option 2: Intercept Earlier in Command Chain

In our `Terminal.TryRunCommand` prefix, modify the command to include explicit Y:

```csharp
static bool Terminal_TryRunCommand_Prefix(string text)
{
    if (!text.StartsWith("terrain")) return true;
    
    // Parse the command to find position
    var pars = TerrainCommandParser.Parse(text.Split(' '));
    
    // If position is over custom terrain and no explicit Y
    if (IsOverCustomTerrain(pars.Position))
    {
        float customHeight = GetCustomTerrainHeight(pars.Position);
        
        // Rebuild command with explicit Y
        text = InsertExplicitHeight(text, customHeight);
    }
    
    // Continue with modified command
}
```

### Option 3: Override IH's Command Building

Patch `ToolSelection.HandleCommand` to use custom terrain height:

```csharp
[HarmonyPatch(typeof(InfinityTools.ToolSelection), "HandleCommand")]
static class HandleCommand_Patch
{
    static void Prefix(GameObject obj)
    {
        Transform transform = HammerHelper.GetPlacementGhost().transform;
        Vector3 pos = transform.position;
        
        // If over custom terrain, ensure Y is from custom terrain
        var combiner = DirtFloorMeshCombiner.GetCombinerAt(pos);
        if (combiner != null)
        {
            float customY = combiner.SampleHeightAt(pos);
            if (customY > -50f)
            {
                // The ghost position IS correct (from raycast)
                // But we need to ensure the command uses this Y
                // Store it for command building
                CustomTerrainHeightOverride.Set(customY);
            }
        }
    }
}
```

---

## Recommended Fix

**Option 1 (Patch GetGroundHeight)** is the cleanest solution because:

1. Fixes ALL code paths that use GetGroundHeight
2. Works for both IH and manual console commands
3. Doesn't require modifying command strings
4. Other mods will also benefit

**Implementation Location:** `InfinityHammerCompat.cs` or new `ZoneSystemPatches.cs`

---

## Command Format Reference

### from Parameter

Format: `from=X,Z,Y` or `from=X,Z` (Y from ground height)

**IMPORTANT:** Order is X,Z,Y not X,Y,Z!

```csharp
// Parsing in WEC:
this.Position = Parse.VectorXZY(Parse.Split(lower2, ','));
// Parse.VectorXZY takes [X, Z, Y] array and returns Vector3(X, Y, Z)
```

### level Parameter

Format: `level` or `level=ALTITUDE`

- Without value: Uses `Position.y` (which came from GetGroundHeight if no explicit Y)
- With value: Uses explicit altitude

### Paint Colors

```csharp
public static Dictionary<string, Color> Paints = new Dictionary<string, Color>()
{
    { "grass", Color.black },
    { "patches", new Color(0.0f, 0.75f, 0.0f) },
    { "grass_dark", new Color(0.6f, 0.5f, 0.0f) },
    { "dirt", Color.red },
    { "cultivated", Color.green },
    { "paved", Color.blue },
    { "paved_moss", new Color(0.0f, 0.0f, 0.5f) },
    { "paved_dirt", new Color(1f, 0.0f, 0.5f) },
    { "paved_dark", new Color(0.0f, 1f, 0.5f) }
};
```

---

## Testing After Fix

1. **Stand on custom terrain at Y=38**
2. **Select Level tool in IH**
3. **IH ghost should show at Y=38** (already works)
4. **Click to execute**
5. **Command should use Y=38** (currently uses Y?0)
6. **New terrain should create at Y=38** (currently creates at Y?0)

---

## Integration Strategy for Custom Terrain

### Problem Summary

WEC's terrain commands work on vanilla `TerrainComp` which is tied to `Heightmap`. Our custom 
terrain uses `DirtFloorMeshCombiner` with a separate paint mask and heightmap system. This means:

1. **WEC paint commands don't affect our terrain** - They modify `TerrainComp.m_paintMask`
2. **WEC height commands don't affect our terrain** - They modify `TerrainComp.m_levelDelta`
3. **WEC undo doesn't work** - It restores vanilla TerrainComp state, not ours

### Solution: Intercept and Redirect

When over custom terrain, we need to:

1. **Intercept the terrain command** before WEC processes it
2. **Apply operations to our `DirtFloorMeshCombiner`** instead
3. **Track our own undo state** that WEC's undo can trigger

### Required Custom Classes

```csharp
// Our equivalent of TerrainUndoData
public class CustomTerrainUndoData
{
    public string CombinerZoneKey;
    public List<TerrainDisk> TerrainDisksBefore;
    public Texture2D PaintMaskBefore;
    public float[] HeightsBefore;
}

// Our equivalent of UndoTerrain
public class CustomTerrainUndo : IUndoAction
{
    Dictionary<string, CustomTerrainUndoData> Before;
    Dictionary<string, CustomTerrainUndoData> After;
    Vector3 Position;
    float Radius;
    
    public string Undo()
    {
        foreach (var kvp in Before)
        {
            var combiner = DirtFloorZoneRegistry.GetCombiner(kvp.Key);
            if (combiner != null)
            {
                // Restore paint mask
                // Restore heights
                combiner.RegenerateMesh();
            }
        }
        return "Undoing custom terrain changes";
    }
    
    public string Redo()
    {
        foreach (var kvp in After)
        {
            var combiner = DirtFloorZoneRegistry.GetCombiner(kvp.Key);
            if (combiner != null)
            {
                // Apply after state
                combiner.RegenerateMesh();
            }
        }
        return "Redoing custom terrain changes";
    }
}
```

### Paint Color Mapping

Map WEC paint colors to our paint types:

```csharp
public static DirtFloorController.PaintType? MapWECColorToPaintType(Color color)
{
    // WEC colors:
    // grass = Color.black (0,0,0)
    // dirt = Color.red (1,0,0)
    // cultivated = Color.green (0,1,0)
    // paved = Color.blue (0,0,1)
    
    if (color.r > 0.5f && color.g < 0.5f && color.b < 0.5f)
        return DirtFloorController.PaintType.Dirt;
    if (color.g > 0.5f && color.r < 0.5f && color.b < 0.5f)
        return DirtFloorController.PaintType.Cultivate;
    if (color.b > 0.5f && color.r < 0.5f && color.g < 0.5f)
        return DirtFloorController.PaintType.Paved;
    if (color.r < 0.2f && color.g < 0.2f && color.b < 0.2f)
        return DirtFloorController.PaintType.Reset; // Grass
    
    return null; // Unknown/mixed color
}
```

### Command Interception Point

In `Terminal.TryRunCommand` prefix:

```csharp
[HarmonyPatch(typeof(Terminal), "TryRunCommand")]
[HarmonyPrefix]
static bool Terminal_TryRunCommand_Prefix(string text)
{
    if (!text.StartsWith("terrain")) return true;
    
    // Parse command to get position and radius
    var pars = TerrainCommandParser.Parse(text.Split(' '));
    
    // Check if ANY part of the operation area is over custom terrain
    if (!DirtFloorTerrainManager.IsPositionOverCustomTerrain(pars.Position, pars.Size + 2f))
        return true; // Let WEC handle vanilla terrain
    
    // Intercept and handle on our terrain
    return !TerrainOperationExecutor.Execute(pars);
}
```

---

## WEC Range<T> Reference

```csharp
namespace ServerDevcommands;

public class Range<T>
{
    public T Min { get; }
    public T Max { get; }
    
    public Range(T value) { Min = value; Max = value; }
    public Range(T min, T max) { Min = min; Max = max; }
}
```

Used for radius like `circle=2-5` which creates a ring shape (min 2m, max 5m).

---

## Infinity Hammer Integration Notes

IH calls WEC terrain commands via `<area>` expansion. Need to find:

1. How `<area>` gets expanded to actual command parameters
2. How the ruler ring preview position is determined
3. Where the command is built before calling `Terminal.TryRunCommand`

This will help us ensure:
- Ruler ring shows at correct height on our terrain
- Commands use our terrain's actual height, not vanilla ground height
