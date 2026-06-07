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
            public float MapExplored; // 0..1
            public readonly Dictionary<string, int> KilledByPrefab = new();
            public readonly Dictionary<string, int> CraftedByPrefab = new();
            public readonly Dictionary<string, int> HarvestedByPrefab = new();
            public readonly Dictionary<string, int> KilledByCause = new();
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
                "StructuresBuilt"=> s.BuiltStructures,
                "Died"           => s.Died,
                "KilledBy"       => string.IsNullOrEmpty(prefab) ? 0 : (s.KilledByCause.TryGetValue(prefab, out var v) ? v : 0),
                "Explored"       => Mathf.RoundToInt(s.MapExplored * 100f),
                "Harvested"      => string.IsNullOrEmpty(prefab) ? 0 : (s.HarvestedByPrefab.TryGetValue(prefab, out var v) ? v : 0),
                "PlayersKilled"  => s.KilledPlayers,
                _ => 0,
            };
        }

        public static void NotifyChanged() => Save();

        private static string StorePath => Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs", "leaderboard", "_local.csv");

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
                var lines = new List<string> { "# player,killedCreatures,killedPlayers,builtStructures,itemsCrafted,died,mapExplored" };
                foreach (var s in _byPlayer.Values)
                {
                    lines.Add(string.Join(",",
                        s.PlayerName, s.KilledCreatures, s.KilledPlayers, s.BuiltStructures,
                        s.ItemsCrafted, s.Died, s.MapExplored.ToString("0.####")));
                }
                File.WriteAllLines(path, lines);
            }
            catch (System.Exception ex) { Debug.LogError("[LeaderboardTracker] save: " + ex.Message); }
        }
    }

    /// <summary>Harmony patches that increment <see cref="LeaderboardTracker"/> counters from real game events.</summary>
    public static class LeaderboardTrackerPatches
    {
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class Character_OnDeath_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(Character __instance)
            {
                if (__instance == null) return;
                var local = Player.m_localPlayer;
                // Player death
                if (__instance == (Character)local)
                {
                    LeaderboardTracker.Local.Died++;
                    LeaderboardTracker.NotifyChanged();
                    return;
                }
                // Creature/player killed by us (local credited). m_lastHit is protected; reach for it via
                // Harmony's AccessTools so we don't need a publicized assembly_valheim ref.
                var hit = AccessTools.Field(typeof(Character), "m_lastHit").GetValue(__instance) as HitData;
                var attacker = hit?.GetAttacker();
                if (attacker != null && attacker == (Character)local)
                {
                    var s = LeaderboardTracker.Local;
                    if (__instance is Player) { s.KilledPlayers++; }
                    else
                    {
                        s.KilledCreatures++;
                        string name = __instance.gameObject.name.Replace("(Clone)", "").Trim();
                        s.KilledByPrefab.TryGetValue(name, out var n); s.KilledByPrefab[name] = n + 1;
                    }
                    LeaderboardTracker.NotifyChanged();
                }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        public static class InventoryGui_DoCrafting_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(Player player)
            {
                if (player != Player.m_localPlayer) return;
                var s = LeaderboardTracker.Local;
                s.ItemsCrafted++;
                LeaderboardTracker.NotifyChanged();
            }
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        public static class Piece_SetCreator_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(Piece __instance, long uid)
            {
                if (Player.m_localPlayer == null) return;
                if (uid != Player.m_localPlayer.GetPlayerID()) return;
                LeaderboardTracker.Local.BuiltStructures++;
                LeaderboardTracker.NotifyChanged();
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Explore), typeof(int), typeof(int))]
        public static class Minimap_Explore_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(bool __result, Minimap __instance)
            {
                if (!__result || __instance == null) return;
                // Sample explored fraction every N calls — exact pixel-count would thrash the file writer.
                if (Random.Range(0, 200) != 0) return;
                int total = __instance.m_textureSize * __instance.m_textureSize;
                int explored = 0;
                if (__instance.m_explored != null)
                {
                    var arr = __instance.m_explored;
                    for (int i = 0; i < arr.Length; i++) if (arr[i]) explored++;
                }
                LeaderboardTracker.Local.MapExplored = total <= 0 ? 0f : (float)explored / total;
                LeaderboardTracker.NotifyChanged();
            }
        }
    }
}
