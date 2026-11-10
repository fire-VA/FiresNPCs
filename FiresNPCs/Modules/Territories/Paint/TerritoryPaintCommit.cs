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
            // The flood fill is stored as ONE Polygon block - the outline itself, simplified - instead of a lattice of 15 m circles (his
            // 'territory' was 65,899 Circle blocks, ~4 MB, all pushed and looked up one by one). Its inside is the even-odd test of that
            // outline (Territory.IsInside2D), so every flag lives on one entry.
            var ring = new List<Vector2>(pts.Count);
            foreach (var p in pts) ring.Add(new Vector2(p.x, p.z));
            if (ring.Count > 1 && Vector2.Distance(ring[0], ring[ring.Count - 1]) <= 1f) ring.RemoveAt(ring.Count - 1);   // stored open; the test closes it
            var simple = SimplifyRing(ring, 2f);
            for (float tol = 4f; simple.Count > MaxPolygonPoints && tol < 512f; tol *= 2f) simple = SimplifyRing(ring, tol);
            if (simple.Count < 3) return;
            var sb = new System.Text.StringBuilder(simple.Count * 12);
            int last = int.MinValue, lastZ = int.MinValue, kept = 0;
            foreach (var v in simple)
            {
                int x = Mathf.RoundToInt(v.x), z = Mathf.RoundToInt(v.y);
                if (x == last && z == lastZ) continue;   // whole metres: drop repeats after rounding
                if (kept++ > 0) sb.Append(',');
                sb.Append(x).Append(',').Append(z);
                last = x; lastZ = z;
            }
            if (kept < 3) return;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryPaint] flood '{b.Name}' saved as one Polygon: {kept} point(s) from a {ring.Count}-point outline");
            WriteAndSync(b, "outlines", new List<string> { Block(b, "Polygon", sb.ToString()) });
        }

        private const int MaxPolygonPoints = 600;

        // Douglas-Peucker on a closed ring (split at the point farthest from the first, so both halves are simplified as open runs).
        private static List<Vector2> SimplifyRing(List<Vector2> ring, float tol)
        {
            if (ring.Count <= 3) return new List<Vector2>(ring);
            int far = 0; float best = -1f;
            for (int i = 1; i < ring.Count; i++) { float d = (ring[i] - ring[0]).sqrMagnitude; if (d > best) { best = d; far = i; } }
            var a = ring.GetRange(0, far + 1);
            var c = ring.GetRange(far, ring.Count - far); c.Add(ring[0]);
            var ra = Dp(a, tol); var rc = Dp(c, tol);
            var outp = new List<Vector2>(ra.Count + rc.Count);
            outp.AddRange(ra);
            for (int i = 1; i < rc.Count - 1; i++) outp.Add(rc[i]);   // skip the shared far point and the closing first point
            return outp;
        }
        private static List<Vector2> Dp(List<Vector2> pts, float tol)
        {
            var keep = new bool[pts.Count]; keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>(); stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (s, e) = stack.Pop();
                float dmax = 0f; int idx = -1;
                Vector2 A = pts[s], B = pts[e], AB = B - A; float len2 = AB.sqrMagnitude;
                for (int i = s + 1; i < e; i++)
                {
                    Vector2 P = pts[i];
                    float d = len2 < 1e-6f ? (P - A).magnitude : Mathf.Abs(AB.x * (P.y - A.y) - AB.y * (P.x - A.x)) / Mathf.Sqrt(len2);
                    if (d > dmax) { dmax = d; idx = i; }
                }
                if (idx >= 0 && dmax > tol) { keep[idx] = true; stack.Push((s, idx)); stack.Push((idx, e)); }
            }
            var r = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) r.Add(pts[i]);
            return r;
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
