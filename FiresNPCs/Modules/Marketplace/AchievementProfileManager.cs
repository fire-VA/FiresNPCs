using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>Definition of a single achievement.</summary>
    public class AchievementDef
    {
        public int Id;
        public string Name;
        public string Description;
        public string TriggerType; // MonstersKilled, ItemsCrafted, StructuresBuilt, Died, KilledBy, Explored, Harvested, PlayersKilled, SkillLevel
        public string Prefab;
        public int MinAmount;
        public int Score;
        /// <summary>Local mock-progress (until the LeaderboardTracker module lands and feeds real player stats).</summary>
        public int MockProgress;
    }

    /// <summary>
    /// Loads achievement-definition .cfg files from <c>BepInEx/config/firesnpcs/achievement/</c>.
    /// File format:
    /// <code>
    /// [profilename]
    /// id|name|description|triggerType|prefab|minAmount|score
    /// </code>
    /// </summary>
    public static class AchievementProfileManager
    {
        private static readonly Dictionary<string, List<AchievementDef>> _byProfile = new();
        private static bool _initialized;

        public static IReadOnlyList<AchievementDef> GetAchievements(string profile)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(profile)) return new List<AchievementDef>();
            return _byProfile.TryGetValue(profile.ToLowerInvariant(), out var list) ? list : new List<AchievementDef>();
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
            var folder = GetProfileFolder();
            if (!Directory.Exists(folder))
            {
                try { Directory.CreateDirectory(folder); } catch { }
                WriteExampleProfile(folder);
            }
            foreach (var file in Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories))
            {
                try { LoadOne(file); }
                catch (System.Exception ex) { Debug.LogError("[AchievementProfileManager] " + file + ": " + ex.Message); }
            }
            Debug.Log("[AchievementProfileManager] Loaded " + _byProfile.Values.Sum(v => v.Count)
                      + " achievement(s) across " + _byProfile.Count + " profile(s)");
        }

        public static string GetProfileFolder() => MarketplacePaths.GetFolder(MarketplacePaths.Achievements);

        private static void LoadOne(string file)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var pool = new List<AchievementDef>();
            int nextId = 1;
            // KG-format buffer: 6 data lines per [header] block (TriggerType / Name / Description /
            // Prefab,MinAmount / R,G,B / Score). When pipe-separated my-format lines appear, we still
            // honor them inline.
            var kgBlock = new List<string>();
            string kgHeader = null;

            void FlushKgBlock()
            {
                if (kgBlock.Count == 6 && !string.IsNullOrEmpty(kgHeader))
                {
                    var prefabMin = kgBlock[3].Replace(" ", "").Split(',');
                    // Bare amount (Died/Explored/overall StructuresBuilt): a single pure-int token => no prefab,
                    // that int IS the threshold. Otherwise "prefab,amount".
                    string prefab; int minAmt;
                    if (prefabMin.Length == 1 && int.TryParse(prefabMin[0], out var bare)) { prefab = ""; minAmt = bare; }
                    else { prefab = prefabMin.Length > 0 ? prefabMin[0] : ""; minAmt = prefabMin.Length > 1 && int.TryParse(prefabMin[1], out var m) ? m : 0; }
                    int score = int.TryParse(kgBlock[5].Trim(), out var s) ? s : 0;
                    pool.Add(new AchievementDef
                    {
                        Id = nextId++,
                        Name = kgBlock[1].Trim(),
                        Description = kgBlock[2].Trim(),
                        TriggerType = kgBlock[0].Trim(),
                        Prefab = prefab,
                        MinAmount = minAmt,
                        Score = score,
                        MockProgress = 0,
                    });
                }
                // Keep kgHeader: a single [profile] header can hold MANY blank-separated 6-line blocks
                // (the AutoGen_*.cfg shape). A new [header] line overwrites it; per-achievement-header files
                // (the KG/VerdantsAscent shape) still load one-profile-per-achievement, unchanged.
                kgBlock.Clear();
            }

            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) { FlushKgBlock(); continue; }
                if (line.StartsWith("["))
                {
                    FlushKgBlock();
                    key = line.Replace("[", "").Replace("]", "").Trim().ToLowerInvariant();
                    if (!_byProfile.ContainsKey(key)) _byProfile[key] = new List<AchievementDef>();
                    pool = _byProfile[key];
                    kgHeader = key;
                    continue;
                }
                if (line.Contains("|"))
                {
                    // My pipe-separated format.
                    var parts = line.Split('|');
                    if (parts.Length < 7) continue;
                    int.TryParse(parts[0], out int id);
                    int.TryParse(parts[5], out int minAmtP);
                    int.TryParse(parts[6], out int scoreP);
                    pool.Add(new AchievementDef
                    {
                        Id = id > 0 ? id : nextId++,
                        Name = parts[1].Trim(),
                        Description = parts[2].Trim(),
                        TriggerType = parts[3].Trim(),
                        Prefab = parts[4].Trim(),
                        MinAmount = minAmtP,
                        Score = scoreP,
                        MockProgress = 0,
                    });
                    continue;
                }
                // KG block format: accumulate 6 lines under current header.
                kgBlock.Add(line);
                if (kgBlock.Count == 6) FlushKgBlock();
            }
            FlushKgBlock();
            if (pool.Count > 0 && !_byProfile.ContainsKey(key)) _byProfile[key] = pool;
        }

        private static void WriteExampleProfile(string folder)
        {
            var path = Path.Combine(folder, "example.cfg");
            if (File.Exists(path)) return;
            var content = string.Join(System.Environment.NewLine, new[]
            {
                "# Example achievement profile. Drop in BepInEx\\config\\firesnpcs\\achievement\\<name>.cfg.",
                "# Format: id|name|description|triggerType|prefab|minAmount|score",
                "# triggerType: MonstersKilled, ItemsCrafted, StructuresBuilt, Died, KilledBy, Explored, Harvested, PlayersKilled, SkillLevel",
                "",
                "[example]",
                "1|Greydwarf Bane|Slay 50 Greydwarves|MonstersKilled|Greydwarf|50|10",
                "2|Troll Vanquisher|Slay 5 Trolls|MonstersKilled|Troll|5|25",
                "3|Master Carpenter|Build 500 structures|StructuresBuilt||500|20",
                "4|Eikthyr's Curse|Die at least 10 times|Died||10|5",
                "5|Cartographer|Explore 25% of the map|Explored||25|30",
                "6|Bonemass Slayer|Down Bonemass|MonstersKilled|Bonemass|1|50",
                "7|PvP Champion|Slay 10 players|PlayersKilled||10|40",
                "8|Mass Producer|Craft 1000 items|ItemsCrafted||1000|25",
            });
            try { File.WriteAllText(path, content); } catch { }
        }
    }
}
