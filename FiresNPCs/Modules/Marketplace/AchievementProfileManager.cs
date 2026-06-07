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
        public string TriggerType; // MonstersKilled, ItemsCrafted, StructuresBuilt, Died, KilledBy, Explored, Harvested, PlayersKilled
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

        public static string GetProfileFolder() => Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs", "achievement");

        private static void LoadOne(string file)
        {
            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var pool = new List<AchievementDef>();
            int nextId = 1;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    key = line.Replace("[", "").Replace("]", "").Trim().ToLowerInvariant();
                    if (!_byProfile.ContainsKey(key)) _byProfile[key] = new List<AchievementDef>();
                    pool = _byProfile[key];
                    continue;
                }
                var parts = line.Split('|');
                if (parts.Length < 7) continue;
                int.TryParse(parts[0], out int id);
                int.TryParse(parts[5], out int minAmt);
                int.TryParse(parts[6], out int score);
                var def = new AchievementDef
                {
                    Id = id > 0 ? id : nextId++,
                    Name = parts[1].Trim(),
                    Description = parts[2].Trim(),
                    TriggerType = parts[3].Trim(),
                    Prefab = parts[4].Trim(),
                    MinAmount = minAmt,
                    Score = score,
                    MockProgress = Mathf.Clamp(Random.Range(0, minAmt + 1), 0, minAmt),
                };
                pool.Add(def);
            }
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
                "# triggerType: MonstersKilled, ItemsCrafted, StructuresBuilt, Died, KilledBy, Explored, Harvested, PlayersKilled",
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
