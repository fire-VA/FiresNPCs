using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using BepInEx;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Territory drawer: lets an admin create single territories at their current position via console commands.
    /// 
    /// Command Format: drawterritory Name,Shape,Radius,R,G,B[,flags]
    /// 
    /// Parameters (comma-separated):
    ///   Name   - Territory name (no spaces, use underscores)
    ///   Shape  - Circle, Square, or Rectangle
    ///   Radius - Single number for Circle/Square, or Width:Height for Rectangle
    ///   R,G,B  - Color values (0-255)
  ///   flags  - Optional: Territory flags separated by | (e.g., NoMonsters|NoBuild)
    /// 
    /// Examples:
    ///   drawterritory SafeZone,Circle,50,0,255,0,NoMonsters|NoBuild
    ///   drawterritory Arena,Square,100,255,0,0,PvpOnly|NoDeathPenalty
    ///   drawterritory Speedway,Rectangle,200:50,0,0,255,MoveSpeedMultiplier=2.0
    ///   drawterritory Shop,Circle,20,255,255,0,NoAttack|NoPickaxe
    /// </summary>
  [HarmonyPatch]
    internal static class TerritoryDrawer
    {
        private const string FolderName = "drawn";
        private static bool _registered;

        private static string DrawnDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories, FolderName);

        [HarmonyPatch(typeof(Terminal), "Awake")]
      [HarmonyPostfix]
        private static void RegisterCommands(Terminal __instance)
        {
     if (_registered) return;
            _registered = true;

    new Terminal.ConsoleCommand("drawterritory",
       "Draw a territory at current position.\n" +
         "  Format: drawterritory Name,Shape,Radius,R,G,B[,flags]\n" +
         "  Shape: Circle, Square, Rectangle\n" +
            "  Radius: number for Circle/Square, or Width:Height for Rectangle\n" +
   "  Examples:\n" +
  "    drawterritory SafeZone,Circle,50,0,255,0,NoMonsters|NoBuild\n" +
       "    drawterritory Arena,Square,100,255,0,0,PvpOnly\n" +
            "    drawterritory Track,Rectangle,200:50,0,0,255,MoveSpeedMultiplier=2.0",
     DrawCommand);

         new Terminal.ConsoleCommand("removeterritory",
           "Remove a drawn territory by name.\n" +
     "  Format: removeterritory Name\n" +
    "  Example: removeterritory SafeZone",
         RemoveCommand);

         new Terminal.ConsoleCommand("listdrawnterritories",
    "List all drawn territories.",
          ListCommand);
        }

        private static void DrawCommand(Terminal.ConsoleEventArgs args)
  {
   try
            {
     if (TerritoryFileSync.RefuseIfNotAdmin(args.Context, "[TerritoryDrawer]")) return;   // 0.0.504
     if (Player.m_localPlayer == null)
      {
           args.Context.AddString("[TerritoryDrawer] No local player");
         return;
       }

      // Parse the command
    if (!TryParseDrawCommand(args.FullLine, "drawterritory", out var parsed, out var error))
                {
   args.Context.AddString($"[TerritoryDrawer] {error}");
  args.Context.AddString("[TerritoryDrawer] Format: drawterritory Name,Shape,Radius,R,G,B[,flags]");
         args.Context.AddString("[TerritoryDrawer] Example: drawterritory SafeZone,Circle,50,0,255,0,NoMonsters|NoBuild");
         return;
           }

      // Get player position
    var pos = Player.m_localPlayer.transform.position;
      int x = Mathf.RoundToInt(pos.x);
          int z = Mathf.RoundToInt(pos.z);

   // Create the territory file
     Directory.CreateDirectory(DrawnDir);
                var fileName = SanitizeFileName(parsed.Name) + ".cfg";
    var filePath = Path.Combine(DrawnDir, fileName);

  var lines = new List<string>();
  lines.Add($"[{parsed.Name}]");
                lines.Add(parsed.Shape.ToString());

      // Coordinates line
     switch (parsed.Shape)
{
               case TerritoryShape.Circle:
        case TerritoryShape.Square:
      lines.Add($"{x},{z},{parsed.Radius}");
     break;
          case TerritoryShape.Rectangle:
       // For rectangle: minX,minZ,width,depth, centred on the player. Every reader (Territory.IsInside2D, the sync
       // bounds, the map overlay) takes X[1]/Y[1] as the SIZE; writing the far corner here made the area wrong away
       // from (0,0) and empty at negative coordinates (rpg_territory_test spec, 10-08).
               int halfW = parsed.Radius / 2;
               int halfH = parsed.Height / 2;
        lines.Add($"{x - halfW},{z - halfH},{parsed.Radius},{parsed.Height}");
        break;
         }

      // Color line: R,G,B,alpha,showWater
    lines.Add($"{parsed.R},{parsed.G},{parsed.B},80,true");

       // Flags line
          lines.Add(string.IsNullOrWhiteSpace(parsed.Flags) ? "None" : parsed.Flags);

    // Permitted line
   lines.Add("ALL");

 File.WriteAllLines(filePath, lines);
 TerritoryFileSync.Saved(filePath);   // 0.0.504: on a dedi client, the server gets it (one source of truth)

    args.Context.AddString($"[TerritoryDrawer] Created territory '{parsed.Name}' at ({x},{z})");
    args.Context.AddString($"[TerritoryDrawer]   Shape: {parsed.Shape}, Size: {parsed.Radius}" +
           (parsed.Shape == TerritoryShape.Rectangle ? $"x{parsed.Height}" : ""));
         args.Context.AddString($"[TerritoryDrawer]   Color: RGB({parsed.R},{parsed.G},{parsed.B})");
      args.Context.AddString($"[TerritoryDrawer]   Flags: {(string.IsNullOrWhiteSpace(parsed.Flags) ? "None" : parsed.Flags)}");

      MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Territory '{parsed.Name}' created");

        // Reload territories
   try
    {
           TerritoryLedger.Reload();
             TerritorySyncedState.BroadcastTerritories();
           }
   catch (Exception ex)
           {
         args.Context.AddString($"[TerritoryDrawer] Warning: Failed to reload territories: {ex.Message}");
   }
 }
   catch (Exception ex)
     {
      args.Context.AddString($"[TerritoryDrawer] Error: {ex.Message}");
    Debug.LogWarning($"[TerritoryDrawer] drawterritory failed: {ex}");
   }
        }

      private static void RemoveCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (TerritoryFileSync.RefuseIfNotAdmin(args.Context, "[TerritoryDrawer]")) return;   // 0.0.504
     var name = ExtractArguments(args.FullLine, "removeterritory");
                if (string.IsNullOrWhiteSpace(name))
     {
     args.Context.AddString("[TerritoryDrawer] Format: removeterritory Name");
     args.Context.AddString("[TerritoryDrawer] Example: removeterritory SafeZone");
               return;
  }

           var fileName = SanitizeFileName(name) + ".cfg";
        var filePath = Path.Combine(DrawnDir, fileName);

    if (!File.Exists(filePath))
             {
           args.Context.AddString($"[TerritoryDrawer] Territory file not found: {fileName}");
      return;
    }

        File.Delete(filePath);
        TerritoryFileSync.Deleted(filePath);   // 0.0.504: on a dedi client, the server deletes its copy too
                args.Context.AddString($"[TerritoryDrawer] Removed territory '{name}'");
       MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Territory '{name}' removed");

        // Reload territories
    try
         {
       TerritoryLedger.Reload();
    TerritorySyncedState.BroadcastTerritories();
    }
             catch { }
          }
            catch (Exception ex)
   {
         args.Context.AddString($"[TerritoryDrawer] Error: {ex.Message}");
     }
        }

        private static void ListCommand(Terminal.ConsoleEventArgs args)
        {
         try
        {
                if (!Directory.Exists(DrawnDir))
             {
      args.Context.AddString("[TerritoryDrawer] No drawn territories directory");
           return;
        }

         var files = Directory.GetFiles(DrawnDir, "*.cfg");
          if (files.Length == 0)
    {
      args.Context.AddString("[TerritoryDrawer] No drawn territories found");
      return;
      }

            args.Context.AddString($"[TerritoryDrawer] Drawn territories ({files.Length}):");
     foreach (var f in files)
      {
        var name = Path.GetFileNameWithoutExtension(f);
         args.Context.AddString($"  - {name}");
         }
 }
   catch (Exception ex)
            {
    args.Context.AddString($"[TerritoryDrawer] Error: {ex.Message}");
     }
     }

        private class ParsedTerritory
        {
      public string Name;
            public TerritoryShape Shape;
            public int Radius;
            public int Height; // For rectangles
  public int R, G, B;
        public string Flags;
        }

   /// <summary>
        /// Parses the drawterritory command.
  /// Format: drawterritory Name,Shape,Radius,R,G,B[,flags]
    /// Radius can be "Width:Height" for rectangles.
        /// </summary>
     private static bool TryParseDrawCommand(string fullLine, string cmd, out ParsedTerritory result, out string error)
        {
         result = new ParsedTerritory
      {
   Name = string.Empty,
                Shape = TerritoryShape.Circle,
                Radius = 30,
       Height = 30,
R = 0,
    G = 255,
        B = 0,
       Flags = string.Empty
    };
         error = string.Empty;

            try
       {
       var raw = ExtractArguments(fullLine, cmd);
           if (string.IsNullOrWhiteSpace(raw))
          {
        error = "No arguments provided";
           return false;
       }

        // Split by comma - we expect: Name,Shape,Radius,R,G,B[,flags]
         var parts = raw.Split(new[] { ',' }, StringSplitOptions.None);
  if (parts.Length < 6)
       {
  error = $"Not enough arguments ({parts.Length}). Need at least: Name,Shape,Radius,R,G,B";
    return false;
}

     // Part 0: Name
    result.Name = parts[0].Trim();
     if (string.IsNullOrWhiteSpace(result.Name))
      {
     error = "Territory name cannot be empty";
          return false;
      }

        // Part 1: Shape
     var shapeStr = parts[1].Trim();
           if (!Enum.TryParse<TerritoryShape>(shapeStr, true, out var shape))
    {
              error = $"Invalid shape '{shapeStr}'. Use Circle, Square, or Rectangle";
return false;
       }
           result.Shape = shape;

          // Part 2: Radius (or Width:Height for Rectangle)
           var radiusStr = parts[2].Trim();
          if (radiusStr.Contains(":"))
         {
                    // Rectangle format: width:height
   var sizeParts = radiusStr.Split(':');
         if (sizeParts.Length >= 2)
       {
          if (!int.TryParse(sizeParts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
                 {
            error = $"Invalid width in '{radiusStr}'";
       return false;
    }
     if (!int.TryParse(sizeParts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
       {
        error = $"Invalid height in '{radiusStr}'";
   return false;
           }
      result.Radius = w;
            result.Height = h;
         }
     }
        else
    {
           if (!int.TryParse(radiusStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
                {
              error = $"Invalid radius '{radiusStr}'";
     return false;
     }
  result.Radius = r;
             result.Height = r;
              }

                // Parts 3,4,5: R,G,B
      if (!int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rVal))
   {
      error = $"Invalid R color value '{parts[3]}'";
        return false;
}
             if (!int.TryParse(parts[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var gVal))
        {
       error = $"Invalid G color value '{parts[4]}'";
    return false;
          }
       if (!int.TryParse(parts[5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bVal))
       {
 error = $"Invalid B color value '{parts[5]}'";
    return false;
                }
       result.R = Mathf.Clamp(rVal, 0, 255);
        result.G = Mathf.Clamp(gVal, 0, 255);
   result.B = Mathf.Clamp(bVal, 0, 255);

                // Part 6+: Flags (optional) - join remaining parts
   if (parts.Length > 6)
     {
         result.Flags = string.Join(",", parts.Skip(6)).Trim();
         }

          return true;
            }
            catch (Exception ex)
        {
         error = $"Parse error: {ex.Message}";
         return false;
            }
        }

        /// <summary>
        /// Extracts the arguments portion of a command line, removing the command name.
     /// </summary>
        private static string ExtractArguments(string fullLine, string commandName)
        {
            if (string.IsNullOrWhiteSpace(fullLine)) return string.Empty;

            fullLine = fullLine.Trim();
            var lowerLine = fullLine.ToLowerInvariant();
     var cmdLower = commandName.ToLowerInvariant();

            int cmdIdx = lowerLine.IndexOf(cmdLower);
        if (cmdIdx >= 0)
            {
     fullLine = fullLine.Substring(cmdIdx + cmdLower.Length).TrimStart(' ', '=');
          }

       return fullLine.Trim();
        }

 private static string SanitizeFileName(string name)
        {
    var invalid = Path.GetInvalidFileNameChars();
  foreach (var ch in invalid)
            {
        name = name.Replace(ch, '_');
            }
    return name.Replace("..", "_").Replace(" ", "_").Trim();
        }
    }
}
