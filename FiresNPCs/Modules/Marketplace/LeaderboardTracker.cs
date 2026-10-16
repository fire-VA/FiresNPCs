using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Player-stat tracker that mirrors KG's <c>Client_Leaderboard</c> shape — kills, deaths, structures
    /// built, items crafted, map explored, harvested resources. Persists to
    /// <c>BepInEx/config/firesnpcs/leaderboard/_local.csv</c> across sessions.
    ///
    /// Stats are LOCAL-only in v1 (each client tracks its own). A future server-side aggregator can pull
    /// these via RPC to populate a true cross-player leaderboard.
    /// </summary>
    public static class LeaderboardTracker
    {
        public class Stats
        {
            public string PlayerName;
            public int KilledCreatures;
            public int KilledPlayers;
            public int BuiltStructures;
            public int ItemsCrafted;
            public int Died;
            public int Terraformed; // hoe terrain ops (raise/level/path) — NOT builds
            public int Cultivated;  // cultivator ops (cultivate + plant)
            public float MapExplored; // 0..1
            public readonly Dictionary<string, int> KilledByPrefab = new();
            public readonly Dictionary<string, int> CraftedByPrefab = new();
            public readonly Dictionary<string, int> HarvestedByPrefab = new();
            public readonly Dictionary<string, int> KilledByCause = new();
            public readonly Dictionary<string, int> BuiltByCategory = new();
        }

        private static readonly Dictionary<string, Stats> _byPlayer = new();
        private static bool _loaded;

        public static Stats GetOrCreate(string playerName)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(playerName)) playerName = "Unknown";
            if (!_byPlayer.TryGetValue(playerName, out var s))
            {
                s = new Stats { PlayerName = playerName };
                _byPlayer[playerName] = s;
            }
            return s;
        }

        public static IReadOnlyDictionary<string, Stats> All { get { EnsureLoaded(); return _byPlayer; } }

        public static Stats Local
        {
            get
            {
                var p = Player.m_localPlayer;
                return GetOrCreate(p != null ? p.GetPlayerName() : "Local");
            }
        }

        public static int GetProgress(string playerName, string triggerType, string prefab)
        {
            var s = GetOrCreate(playerName);
            return triggerType switch
            {
                "MonstersKilled" => string.IsNullOrEmpty(prefab) ? s.KilledCreatures : (s.KilledByPrefab.TryGetValue(prefab, out var v) ? v : 0),
                "ItemsCrafted"   => string.IsNullOrEmpty(prefab) ? s.ItemsCrafted    : (s.CraftedByPrefab.TryGetValue(prefab, out var v) ? v : 0),
                "StructuresBuilt"=> string.IsNullOrEmpty(prefab) ? s.BuiltStructures
                                     : (s.BuiltByCategory.TryGetValue(prefab, out var bc) ? bc : 0),
                "SkillLevel"     => GetSkillLevel(playerName, prefab),
                "Terraformed"    => s.Terraformed,
                "Cultivated"     => s.Cultivated,
                "Died"           => s.Died,
                "KilledBy"       => string.IsNullOrEmpty(prefab) ? 0 : (s.KilledByCause.TryGetValue(prefab, out var v) ? v : 0),
                "Explored"       => Mathf.RoundToInt(s.MapExplored * 100f),
                "Harvested"      => string.IsNullOrEmpty(prefab) ? 0 : (s.HarvestedByPrefab.TryGetValue(prefab, out var v) ? v : 0),
                "PlayersKilled"  => s.KilledPlayers,
                "LoreCollected" or "Granted" => global::FiresRPGmaker.Modules.Quest.LoreBooksBridge.LocalProgress(triggerType, prefab),   // 0.0.560 LoreBooks
                _ => 0,
            };
        }

        // Live read — skills are already game-persisted; only the local player's are knowable client-side.
        private static int GetSkillLevel(string playerName, string skillName)
        {
            var p = Player.m_localPlayer;
            if (p == null || string.IsNullOrEmpty(skillName)) return 0;
            if (p.GetPlayerName() != playerName) return 0;
            if (!System.Enum.TryParse<Skills.SkillType>(skillName, true, out var type)) return 0;
            var skills = p.GetSkills();
            return skills == null ? 0 : Mathf.FloorToInt(skills.GetSkillLevel(type));
        }

        public static void NotifyChanged() => Save();

        // Wipe this client's legacy local stats (the _local.csv mirror + in-memory cache). The authoritative board
        // is server-side; this only resets the local-player pre-snapshot view for a fresh start.
        public static void WipeLocal()
        {
            try { var p = StorePath; if (File.Exists(p)) File.Delete(p); } catch { }
            _byPlayer.Clear();
            _loaded = false;
        }

        private static string StorePath => Path.Combine(FiresCore.Storage.FiresConfigPaths.LeaderboardLocal, "_local.csv");

        private static void EnsureLoaded()
        {
            if (_loaded) return; _loaded = true;
            var path = StorePath;
            if (!File.Exists(path)) return;
            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 7) continue;
                    var s = new Stats
                    {
                        PlayerName = parts[0],
                        KilledCreatures = int.Parse(parts[1]),
                        KilledPlayers = int.Parse(parts[2]),
                        BuiltStructures = int.Parse(parts[3]),
                        ItemsCrafted = int.Parse(parts[4]),
                        Died = int.Parse(parts[5]),
                        MapExplored = float.Parse(parts[6]),
                    };
                    if (parts.Length > 7) DecodeDict(parts[7], s.HarvestedByPrefab);
                    if (parts.Length > 8) DecodeDict(parts[8], s.BuiltByCategory);
                    if (parts.Length > 9 && int.TryParse(parts[9], out var tf)) s.Terraformed = tf;
                    if (parts.Length > 10 && int.TryParse(parts[10], out var cv)) s.Cultivated = cv;
                    if (parts.Length > 11) DecodeDict(parts[11], s.KilledByCause);
                    if (parts.Length > 12) DecodeDict(parts[12], s.KilledByPrefab);
                    _byPlayer[s.PlayerName] = s;
                }
            }
            catch (System.Exception ex) { Debug.LogError("[LeaderboardTracker] load: " + ex.Message); }
        }

        private static void Save()
        {
            try
            {
                var path = StorePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string> { "# player,killedCreatures,killedPlayers,builtStructures,itemsCrafted,died,mapExplored,harvested,builtByCategory,terraformed,cultivated,killedByCause,killedByPrefab" };
                foreach (var s in _byPlayer.Values)
                {
                    lines.Add(string.Join(",",
                        s.PlayerName, s.KilledCreatures, s.KilledPlayers, s.BuiltStructures,
                        s.ItemsCrafted, s.Died, s.MapExplored.ToString("0.####"),
                        EncodeDict(s.HarvestedByPrefab), EncodeDict(s.BuiltByCategory),
                        s.Terraformed, s.Cultivated,
                        EncodeDict(s.KilledByCause), EncodeDict(s.KilledByPrefab)));
                }
                File.WriteAllLines(path, lines);
            }
            catch (System.Exception ex) { Debug.LogError("[LeaderboardTracker] save: " + ex.Message); }
        }

        // Per-prefab/category dicts persist as key:count;key:count in one CSV column (keys sanitized of , ; :).
        private static string EncodeDict(Dictionary<string, int> d)
        {
            if (d == null || d.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var kv in d)
            {
                if (sb.Length > 0) sb.Append(';');
                string k = kv.Key.Replace(",", "").Replace(";", "").Replace(":", "");
                sb.Append(k).Append(':').Append(kv.Value);
            }
            return sb.ToString();
        }

        private static void DecodeDict(string s, Dictionary<string, int> d)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (var pair in s.Split(';'))
            {
                int c = pair.LastIndexOf(':');
                if (c <= 0) continue;
                if (int.TryParse(pair.Substring(c + 1), out var v)) d[pair.Substring(0, c)] = v;
            }
        }
    }

    /// <summary>
    /// What the leaderboard's game-event hooks record into the local tracker. The hooks themselves live once, in
    /// <see cref="LeaderboardFeedPatches"/>, which also feeds the server: one Harmony patch per vanilla method.
    /// </summary>
    internal static class LeaderboardTrackerRecorder
    {
        internal static void OwnDeath(string killerKey)
        {
            var s = LeaderboardTracker.Local;
            s.Died++;
            if (!string.IsNullOrEmpty(killerKey))
            {
                s.KilledByCause.TryGetValue(killerKey, out var n);
                s.KilledByCause[killerKey] = n + 1;
            }
            LeaderboardTracker.NotifyChanged();
        }

        internal static void Kill(Character victim)
        {
            var s = LeaderboardTracker.Local;
            if (victim is Player) s.KilledPlayers++;
            else
            {
                s.KilledCreatures++;
                string name = victim.gameObject.name.Replace("(Clone)", "").Trim();
                s.KilledByPrefab.TryGetValue(name, out var n);
                s.KilledByPrefab[name] = n + 1;
            }
            LeaderboardTracker.NotifyChanged();
        }

        internal static void Crafted()
        {
            LeaderboardTracker.Local.ItemsCrafted++;
            LeaderboardTracker.NotifyChanged();
        }

        internal static void Piece(Piece piece, LeaderboardServerFeed.PieceKind kind)
        {
            var s = LeaderboardTracker.Local;
            switch (kind)
            {
                case LeaderboardServerFeed.PieceKind.Cultivate: s.Cultivated++; break;
                case LeaderboardServerFeed.PieceKind.Terraform: s.Terraformed++; break;
                default:
                    s.BuiltStructures++;
                    string category = piece.m_category.ToString();
                    s.BuiltByCategory.TryGetValue(category, out var n);
                    s.BuiltByCategory[category] = n + 1;
                    break;
            }
            LeaderboardTracker.NotifyChanged();
        }

        internal static void Explored(float fraction)
        {
            LeaderboardTracker.Local.MapExplored = fraction;
            LeaderboardTracker.NotifyChanged();
        }

        internal static void Harvested(string prefab)
        {
            var s = LeaderboardTracker.Local;
            s.HarvestedByPrefab.TryGetValue(prefab, out var n);
            s.HarvestedByPrefab[prefab] = n + 1;
            LeaderboardTracker.NotifyChanged();
        }
    }
}
