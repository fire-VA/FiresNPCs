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
                "Died"           => s.Died,
                "KilledBy"       => string.IsNullOrEmpty(prefab) ? 0 : (s.KilledByCause.TryGetValue(prefab, out var v) ? v : 0),
                "Explored"       => Mathf.RoundToInt(s.MapExplored * 100f),
                "Harvested"      => string.IsNullOrEmpty(prefab) ? 0 : (s.HarvestedByPrefab.TryGetValue(prefab, out var v) ? v : 0),
                "PlayersKilled"  => s.KilledPlayers,
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
                    if (parts.Length > 7) DecodeDict(parts[7], s.HarvestedByPrefab);
                    if (parts.Length > 8) DecodeDict(parts[8], s.BuiltByCategory);
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
                var lines = new List<string> { "# player,killedCreatures,killedPlayers,builtStructures,itemsCrafted,died,mapExplored,harvested,builtByCategory" };
                foreach (var s in _byPlayer.Values)
                {
                    lines.Add(string.Join(",",
                        s.PlayerName, s.KilledCreatures, s.KilledPlayers, s.BuiltStructures,
                        s.ItemsCrafted, s.Died, s.MapExplored.ToString("0.####"),
                        EncodeDict(s.HarvestedByPrefab), EncodeDict(s.BuiltByCategory)));
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
                if (Player.m_localPlayer == null || __instance == null) return;
                if (uid != Player.m_localPlayer.GetPlayerID()) return;
                var s = LeaderboardTracker.Local;
                s.BuiltStructures++;
                string cat = __instance.m_category.ToString();
                s.BuiltByCategory.TryGetValue(cat, out var n); s.BuiltByCategory[cat] = n + 1;
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

        // Pickable.Interact's return is m_useInteractAnimation (a static flag), NOT a did-pick signal. Credit a
        // non-repeat interact with an UN-picked pickable (prefix __state == false) by the local player. We do
        // NOT re-check GetPicked() afterward: on a dedicated server the pick RPC routes to the ZDO owner and
        // m_picked flips a frame LATER, so a post-interact check would miss every client harvest. Optimistic
        // (a contested pick can over-credit by 1 — fine for an achievement counter).
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        public static class Pickable_Interact_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(Pickable __instance, out bool __state)
                => __state = __instance != null && __instance.GetPicked();

            [HarmonyPostfix]
            private static void Postfix(Pickable __instance, Humanoid character, bool repeat, bool __state)
            {
                if (repeat || __state) return;                       // repeat-use, or already picked before this interact
                if (__instance == null || character == null) return;
                var lp = Player.m_localPlayer;
                if (lp == null || character != (Humanoid)lp) return;
                var s = LeaderboardTracker.Local;
                string name = __instance.gameObject.name.Replace("(Clone)", "").Trim();
                s.HarvestedByPrefab.TryGetValue(name, out var n); s.HarvestedByPrefab[name] = n + 1;
                LeaderboardTracker.NotifyChanged();
            }
        }
    }
}
