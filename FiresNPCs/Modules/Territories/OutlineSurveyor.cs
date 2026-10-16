using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using BepInEx;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Outline surveyor: lets an admin walk the perimeter of an area and auto-fill it with circle territories.
  /// 
 /// Command Format: startoutline Name[,R,G,B,flags]
    /// 
    /// Parameters (comma-separated):
    ///   Name  - Territory name (required)
    ///   R,G,B - Color values 0-255 (optional, default: 0,255,0 green)
    ///   flags - Territory flags separated by | (optional, default: None)
    /// 
    /// Examples:
    ///   startoutline VillageArea
    ///   startoutline SafeZone,0,255,0
 ///   startoutline ProtectedRegion,0,255,0,NoMonsters|NoBuild
    ///   startoutline FastZone,0,0,255,MoveSpeedMultiplier=1.5|InfiniteStamina
    /// 
    /// Usage:
    ///   1. Run: startoutline MyArea,R,G,B,flags
    ///   2. Walk the perimeter of the area you want to fill
    /// 3. Return near your starting point
    ///   4. Run: stopoutline
    /// 
    /// The system auto-fills the enclosed polygon with 15m radius circles on a 15m grid.
  /// Output: Config/FiresRPGmaker/Territories/outlines/&lt;Name&gt;.cfg
    /// </summary>
    [HarmonyPatch]
    internal static class OutlineSurveyor
  {
        private const float Radius = 15f;
        private const float Step = 15f;
        private const float MinCloseDistance = 12f;
        private const string FolderName = "outlines";

        private static bool _active;
     private static string _name = string.Empty;
        private static readonly List<Vector3> _points = new();
 private static Minimap.PinData _pin;
      private static int _colorR = 0, _colorG = 255, _colorB = 0;
        private static string _flags = "None";

        private static string OutlineDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories, FolderName);

  [HarmonyPatch(typeof(Terminal), "Awake")]
        [HarmonyPostfix]
 private static void RegisterCommands(Terminal __instance)
        {
      new Terminal.ConsoleCommand("startoutline",
  "Start outline area recording.\n" +
                "  Format: startoutline Name[,R,G,B,flags]\n" +
     "  Walk the perimeter, then 'stopoutline' to fill with territories.\n" +
         "  Examples:\n" +
     "    startoutline VillageArea\n" +
        "    startoutline SafeZone,0,255,0\n" +
         "    startoutline ProtectedRegion,0,255,0,NoMonsters|NoBuild",
      StartCommand);

  new Terminal.ConsoleCommand("stopoutline",
         "Stop outline recording and generate filled territories.\n" +
           "  Requires at least 3 points to form a valid area.",
    StopCommand);
        }

        private static void StartCommand(Terminal.ConsoleEventArgs args)
 {
        try
     {
         if (TerritoryFileSync.RefuseIfNotAdmin(args.Context, "[Outline]")) return;   // 0.0.504
         if (Player.m_localPlayer == null)
        {
         args.Context.AddString("[Outline] No local player");
               return;
      }

    if (!TryParseArgs(args.FullLine, "startoutline", out var name, out var r, out var g, out var b, out var flags))
      {
   args.Context.AddString("[Outline] Format: startoutline Name[,R,G,B,flags]");
          args.Context.AddString("[Outline] Examples:");
     args.Context.AddString("[Outline]   startoutline VillageArea");
    args.Context.AddString("[Outline]   startoutline SafeZone,0,255,0");
        args.Context.AddString("[Outline]   startoutline ProtectedRegion,0,255,0,NoMonsters|NoBuild");
return;
         }

     _colorR = r;
     _colorG = g;
          _colorB = b;
_flags = string.IsNullOrWhiteSpace(flags) ? "None" : flags.Trim();

      _points.Clear();
         _active = true;
     _name = name.Trim();
                _points.Add(Player.m_localPlayer.transform.position);
     Directory.CreateDirectory(OutlineDir);

     // Add minimap pin at start
        TryRemovePin();
         var mm = Minimap.instance;
        if (mm != null)
        {
          _pin = mm.AddPin(Player.m_localPlayer.transform.position, Minimap.PinType.Death, _name, false, false);
                }

                args.Context.AddString($"[Outline] Started outline '{_name}'");
 args.Context.AddString($"[Outline]   Color: RGB({_colorR},{_colorG},{_colorB})");
                args.Context.AddString($"[Outline]   Flags: {_flags}");
          args.Context.AddString($"[Outline]   Walk the perimeter, then run 'stopoutline'");
        
          MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Outline '{_name}' started - walk the perimeter");
            }
            catch (Exception ex)
     {
       args.Context.AddString($"[Outline] Error: {ex.Message}");
        }
        }

 private static void StopCommand(Terminal.ConsoleEventArgs args)
  {
            try
            {
    if (!_active)
       {
       args.Context.AddString("[Outline] Not currently recording an outline");
            args.Context.AddString("[Outline] Use 'startoutline Name' to begin");
      return;
       }

_active = false;

       if (_points.Count < 3)
      {
  args.Context.AddString($"[Outline] Need at least 3 points to form an area (have {_points.Count})");
        args.Context.AddString("[Outline] Walk further around your desired area");
         TryRemovePin();
  _points.Clear();
            _name = string.Empty;
   return;
      }

     // Close polygon if not near start
      if (Vector3.Distance(_points[0], _points[_points.Count - 1]) > MinCloseDistance)
       {
    _points.Add(_points[0]);
     }

   var filePath = Path.Combine(OutlineDir, SanitizeFileName(_name) + ".cfg");
         var circles = FillWithCircles(_points);

   var lines = new List<string>();
 foreach (var c in circles)
         {
               lines.Add($"[{_name}]");
            lines.Add("Circle");
         lines.Add($"{Mathf.RoundToInt(c.x)},{Mathf.RoundToInt(c.z)},{Mathf.RoundToInt(Radius)}");
   lines.Add($"{Mathf.Clamp(_colorR, 0, 255)},{Mathf.Clamp(_colorG, 0, 255)},{Mathf.Clamp(_colorB, 0, 255)},80,true");
        lines.Add(string.IsNullOrWhiteSpace(_flags) ? "None" : _flags);
  lines.Add("ALL");
                }

            File.WriteAllLines(filePath, lines);
            TerritoryFileSync.Saved(filePath);   // 0.0.504: on a dedi client, the server gets the outline

 args.Context.AddString($"[Outline] Saved outline '{_name}'");
             args.Context.AddString($"[Outline]   Points walked: {_points.Count}");
     args.Context.AddString($"[Outline]   Territories created: {circles.Count}");
     args.Context.AddString($"[Outline]   File: {filePath}");
       
           MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Outline '{_name}' saved ({circles.Count} territories)");

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
    args.Context.AddString($"[Outline] Error: {ex.Message}");
            }
            finally
   {
           TryRemovePin();
      _points.Clear();
          _name = string.Empty;
        }
        }

        /// <summary>
   /// Parses command arguments in format: Name[,R,G,B,flags]
        /// </summary>
        private static bool TryParseArgs(string raw, string cmd, out string name, out int r, out int g, out int b, out string flags)
        {
            // Defaults
     name = string.Empty;
      r = 0; g = 255; b = 0; // Default green
    flags = string.Empty;

       try
       {
     if (string.IsNullOrWhiteSpace(raw)) return false;

      // Remove command prefix
        raw = raw.Trim();
   var lower = raw.ToLowerInvariant();
                int idx = lower.IndexOf(cmd.ToLowerInvariant());
           if (idx >= 0)
  {
   raw = raw.Substring(idx + cmd.Length).TrimStart(' ', '=');
    }

                if (string.IsNullOrWhiteSpace(raw)) return false;

  // Split by comma: Name,R,G,B,flags
  var parts = raw.Split(new[] { ',' }, StringSplitOptions.None);

          // Part 0: Name (required)
                name = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(name)) return false;

        // Parts 1,2,3: R,G,B (optional)
if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
             int.TryParse(parts[1].Trim(), out r);
    if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
          int.TryParse(parts[2].Trim(), out g);
        if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
         int.TryParse(parts[3].Trim(), out b);

    // Part 4+: flags (optional)
          if (parts.Length > 4)
    {
  flags = string.Join(",", parts.Skip(4)).Trim();
       }

         r = Mathf.Clamp(r, 0, 255);
    g = Mathf.Clamp(g, 0, 255);
         b = Mathf.Clamp(b, 0, 255);

         return true;
   }
            catch
            {
    return false;
            }
        }

    private static void TryRemovePin()
        {
    if (_pin != null && Minimap.instance != null)
         {
        Minimap.instance.RemovePin(_pin);
            }
    _pin = null;
        }

        public static void Tick(Player player)
   {
         if (!_active || player == null || player != Player.m_localPlayer) return;

   var pos = player.transform.position;
        if (_points.Count == 0 || Vector3.Distance(_points[_points.Count - 1], pos) >= Step)
    {
            _points.Add(pos);
       MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Outline point {_points.Count}");
    }
        }

      internal static List<Vector3> FillWithCircles(List<Vector3> pts)
        {
          // Project to XZ plane
       var poly = pts.Select(p => new Vector2(p.x, p.z)).ToList();
       float minX = poly.Min(p => p.x) - Radius;
  float maxX = poly.Max(p => p.x) + Radius;
       float minY = poly.Min(p => p.y) - Radius;
  float maxY = poly.Max(p => p.y) + Radius;

        var result = new List<Vector3>();
            for (float y = minY; y <= maxY; y += Step)
            {
                for (float x = minX; x <= maxX; x += Step)
     {
      if (PointInPolygon(poly, new Vector2(x, y)))
     {
  result.Add(new Vector3(x, 0f, y));
      }
                }
            }
            return result;
        }

        private static bool PointInPolygon(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
     for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var pi = poly[i];
       var pj = poly[j];
                bool intersect = ((pi.y > p.y) != (pj.y > p.y)) &&
         (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + float.Epsilon) + pi.x);
       if (intersect) inside = !inside;
      }
    return inside;
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
