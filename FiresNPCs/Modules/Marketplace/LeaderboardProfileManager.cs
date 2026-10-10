using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    public class LeaderboardEntry
    {
        public string PlayerName;
        public int KilledCreatures;
        public int KilledPlayers;
        public int BuiltStructures;
        public int ItemsCrafted;
        public int Died;
        public float MapExplored; // 0..1
    }

    /// <summary>
    /// Loads leaderboard-sample profiles from <c>BepInEx/config/firesnpcs/leaderboard/</c>.
    /// .cfg format:
    /// <code>
    /// [profilename]
    /// PlayerName|killedCreatures|killedPlayers|builtStructures|itemsCrafted|died|mapExploredPct
    /// </code>
    /// Real per-player rollup ships in the LeaderboardTracker module.
    /// </summary>
    public static class LeaderboardProfileManager
    {
        private static readonly Dictionary<string, List<LeaderboardEntry>> _byProfile = new();
        private static bool _initialized;

        public static IReadOnlyList<LeaderboardEntry> GetSampleEntries(string profile)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(profile)) return new List<LeaderboardEntry>();
            if (_byProfile.TryGetValue(profile.ToLowerInvariant(), out var list)) return list;
            return new List<LeaderboardEntry>();
        }

        public static List<LeaderboardEntry> SortBy(IEnumerable<LeaderboardEntry> entries, string field)
        {
            return field switch
            {
                "KilledCreatures" => entries.OrderByDescending(e => e.KilledCreatures).ToList(),
                "KilledPlayers"   => entries.OrderByDescending(e => e.KilledPlayers).ToList(),
                "BuiltStructures" => entries.OrderByDescending(e => e.BuiltStructures).ToList(),
                "ItemsCrafted"    => entries.OrderByDescending(e => e.ItemsCrafted).ToList(),
                "Died"            => entries.OrderByDescending(e => e.Died).ToList(),
                "MapExplored"     => entries.OrderByDescending(e => e.MapExplored).ToList(),
                _                 => entries.OrderByDescending(e => e.KilledCreatures).ToList(),
            };
        }

        public static IEnumerable<string> GetLoadedProfileNames() { EnsureLoaded(); return _byProfile.Keys; }

        public static void EnsureLoaded()
        {
            if (_initialized) return;
            ReloadAll();
        }

        public static void ReloadAll()
        {
            _initialized = true;
            _byProfile.Clear();
            // Pure clients parse the server-synced in-memory store (no config files on client disk);
            // server/host reads + seeds its folder as before.
            if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
            {
                var folder = GetProfileFolder();
                if (!Directory.Exists(folder))
                {
                    try { Directory.CreateDirectory(folder); } catch { }
                    WriteExampleProfile(folder);
                }
            }
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines(MarketplacePaths.Leaderboard))
            {
                try { LoadOne(kv.Key, kv.Value); }
                catch (System.Exception ex) { Debug.LogError("[LeaderboardProfileManager] " + kv.Key + ": " + ex.Message); }
            }
            Debug.Log("[LeaderboardProfileManager] Loaded " + _byProfile.Values.Sum(v => v.Count)
                      + " entry(ies) across " + _byProfile.Count + " profile(s)");
        }

        public static string GetProfileFolder() => MarketplacePaths.GetFolder(MarketplacePaths.Leaderboard);

        private static void LoadOne(string file, string[] lines)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var pool = new List<LeaderboardEntry>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    key = line.Replace("[", "").Replace("]", "").Trim().ToLowerInvariant();
                    if (!_byProfile.ContainsKey(key)) _byProfile[key] = new List<LeaderboardEntry>();
                    pool = _byProfile[key];
                    continue;
                }
                var parts = line.Split('|');
                if (parts.Length < 7) continue;
                pool.Add(new LeaderboardEntry
                {
                    PlayerName = parts[0].Trim(),
                    KilledCreatures = int.TryParse(parts[1], out var v1) ? v1 : 0,
                    KilledPlayers   = int.TryParse(parts[2], out var v2) ? v2 : 0,
                    BuiltStructures = int.TryParse(parts[3], out var v3) ? v3 : 0,
                    ItemsCrafted    = int.TryParse(parts[4], out var v4) ? v4 : 0,
                    Died            = int.TryParse(parts[5], out var v5) ? v5 : 0,
                    MapExplored     = float.TryParse(parts[6], out var v6) ? v6 / 100f : 0f,
                });
            }
            if (pool.Count > 0 && !_byProfile.ContainsKey(key)) _byProfile[key] = pool;
        }

        private static void WriteExampleProfile(string folder)
        {
            var path = Path.Combine(folder, "example.cfg");
            if (File.Exists(path)) return;
            var content = string.Join(System.Environment.NewLine, new[]
            {
                "# Example leaderboard data. Drop in BepInEx\\config\\firesnpcs\\leaderboard\\<name>.cfg.",
                "# Format per row: Name|killedCreatures|killedPlayers|builtStructures|itemsCrafted|died|mapExploredPct (0-100)",
                "",
                "[example]",
                "AdminMcHelpful|2400|42|18000|9500|11|85",
                "FletcherGertrude|820|3|5400|2100|4|62",
                "SapMaster|410|0|3100|1850|8|44",
                "FirewoodPete|180|0|11200|6700|12|33",
                "RaidBossCorpsey|3100|18|2200|900|1|28",
                "RookeryRune|640|2|4900|3200|2|71",
                "BlackForestBenny|1450|1|1600|2400|6|39",
                "MeadowsMartha|95|0|7800|4400|14|22",
            });
            try { File.WriteAllText(path, content); } catch { }
        }
    }
}
