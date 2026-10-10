using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace FiresRPGmaker.Modules.Territories.Paint
{
    /// <summary>Turns a painted brush stroke into the existing KG territory .cfg blocks and persists + syncs them
    /// the same way TerritoryDrawer/OutlineSurveyor already do. Single shapes → drawn/; flood/road tile into many
    /// 15m circles (OutlineSurveyor.FillWithCircles) → outlines/. Both folders are parsed by TerritoryLedger and
    /// the "Territories" folder is allowlisted + watched, so on a dedi the admin (a client) pushes via
    /// FiresConfigSync and the server writes + reloads + rebroadcasts; on a host we write + reload + broadcast.</summary>
    public static class TerritoryPaintCommit
    {
        public static void CommitCircle(TerritoryBrush b, Vector3 center, int radius)
            => WriteAndSync(b, "drawn", new List<string> { Block(b, "Circle", $"{R(center.x)},{R(center.z)},{Mathf.Max(1, radius)}") });

        public static void CommitSquare(TerritoryBrush b, Vector3 center, int radius)
            => WriteAndSync(b, "drawn", new List<string> { Block(b, "Square", $"{R(center.x)},{R(center.z)},{Mathf.Max(1, radius)}") });

        public static void CommitFlood(TerritoryBrush b, List<Vector3> outline)
        {
            if (outline == null || outline.Count < 3) return;
            var pts = new List<Vector3>(outline);
            if (Vector3.Distance(pts[0], pts[pts.Count - 1]) > 1f) pts.Add(pts[0]);   // close the loop
            var circles = OutlineSurveyor.FillWithCircles(pts);
            if (circles.Count == 0) return;
            var blocks = new List<string>(circles.Count);
            foreach (var c in circles) blocks.Add(Block(b, "Circle", $"{R(c.x)},{R(c.z)},15"));
            WriteAndSync(b, "outlines", blocks);
        }

        public static void CommitRoad(TerritoryBrush b, List<Vector3> line)
        {
            if (line == null || line.Count < 2) return;
            int rad = Mathf.Max(5, b.Radius);
            var blocks = new List<string>();
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector3 p0 = line[i], p1 = line[i + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(p0, p1) / 10f));
                for (int s = 0; s <= steps; s++)
                {
                    var p = Vector3.Lerp(p0, p1, s / (float)steps);
                    blocks.Add(Block(b, "Circle", $"{R(p.x)},{R(p.z)},{rad}"));
                }
            }
            if (blocks.Count > 0) WriteAndSync(b, "outlines", blocks);
        }

        // One KG 6-line block: [Name] / Shape / coords / R,G,B,A,showWater / flags / permitted.
        private static string Block(TerritoryBrush b, string shape, string coords)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(string.IsNullOrWhiteSpace(b.Name) ? "territory" : b.Name.Trim()).Append("]\n");
            sb.Append(shape).Append('\n');
            sb.Append(coords).Append('\n');
            sb.Append(Mathf.Clamp(b.R, 0, 255)).Append(',').Append(Mathf.Clamp(b.G, 0, 255)).Append(',')
              .Append(Mathf.Clamp(b.B, 0, 255)).Append(',').Append(Mathf.Clamp(b.Alpha, 0, 255)).Append(",true\n");
            sb.Append(string.IsNullOrWhiteSpace(b.Flags) ? "None" : b.Flags.Trim()).Append('\n');
            sb.Append("ALL");
            return sb.ToString();
        }

        private static int R(float v) => Mathf.RoundToInt(v);

        private static void WriteAndSync(TerritoryBrush b, string subfolder, List<string> blocks)
        {
            string body = string.Join("\n", blocks) + "\n";
            string safe = SanitizeFileName(string.IsNullOrWhiteSpace(b.Name) ? "territory" : b.Name);

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                try
                {
                    string dir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories, subfolder);
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, safe + ".cfg"), body);
                    TerritoryLedger.Reload();
                    TerritorySyncedState.BroadcastTerritories();
                }
                catch (System.Exception ex) { Debug.LogWarning("[TerritoryPaint] host write failed: " + ex.Message); }
            }
            else
            {
                // Dedi admin = client: push to the server, which validates admin, writes, reloads + rebroadcasts.
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Territories", subfolder + "/" + safe + ".cfg", body);
            }
        }

        private static string SanitizeFileName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length > 0 ? sb.ToString() : "territory";
        }

        // ── editing an existing territory: rewrite its cfg's name/color/alpha/flags, keep the shapes ──
        public static string FindCfgRel(string name)
        {
            string safe = SanitizeFileName(name);
            foreach (var sub in new[] { "drawn", "outlines", "legacy" })
                if (File.Exists(Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories, sub, safe + ".cfg")))
                    return sub + "/" + safe + ".cfg";
            return null;
        }

        public static bool EditTerritory(string cfgRel, TerritoryBrush b)
        {
            if (string.IsNullOrEmpty(cfgRel)) return false;
            string full = Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories, cfgRel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) return false;
            string header = "[" + (string.IsNullOrWhiteSpace(b.Name) ? "territory" : b.Name.Trim()) + "]";
            string colorLine = $"{Mathf.Clamp(b.R, 0, 255)},{Mathf.Clamp(b.G, 0, 255)},{Mathf.Clamp(b.B, 0, 255)},{Mathf.Clamp(b.Alpha, 0, 255)},true";
            string flagLine = string.IsNullOrWhiteSpace(b.Flags) ? "None" : b.Flags.Trim();
            var outp = new List<string>(); int bl = -1;
            foreach (var raw in File.ReadAllLines(full))
            {
                if (raw.TrimStart().StartsWith("[")) { bl = 0; outp.Add(header); continue; }   // block header → new name
                if (bl < 0) { outp.Add(raw); continue; }
                bl++;
                if (bl == 3) outp.Add(colorLine);       // color line
                else if (bl == 4) outp.Add(flagLine);   // flag line
                else outp.Add(raw);                     // shape / coords / permitted preserved
            }
            WriteRawAndSync(cfgRel, string.Join("\n", outp) + "\n");
            return true;
        }

        private static void WriteRawAndSync(string cfgRel, string body)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                try
                {
                    string full = Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories, cfgRel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    File.WriteAllText(full, body);
                    TerritoryLedger.Reload();
                    TerritorySyncedState.BroadcastTerritories();
                }
                catch (System.Exception ex) { Debug.LogWarning("[TerritoryPaint] edit write failed: " + ex.Message); }
            }
            else global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Territories", cfgRel, body);
        }
    }
}
