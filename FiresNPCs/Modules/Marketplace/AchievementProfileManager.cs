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
        /// <summary>Tier accent color, captured from the KG block's "R,G,B" line (line index 4). Drives the
        /// completed-achievement row tint. Defaults to a neutral parchment-gray when absent/unparseable.</summary>
        public Color AccentColor = NeutralAccent;
        /// <summary>Local mock-progress (until the LeaderboardTracker module lands and feeds real player stats).</summary>
        public int MockProgress;

        /// <summary>Neutral fallback accent when a def carries no parseable "R,G,B" line (mirrors the Novice tier gray).</summary>
        public static readonly Color NeutralAccent = new Color(190f / 255f, 190f / 255f, 190f / 255f, 1f);
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

        /// <summary>
        /// The achievement profile the leaderboard scores against. Prefers the configured profile
        /// (<c>Leaderboard / AchievementProfile</c>, default "server") when it actually carries achievements;
        /// otherwise the first non-empty loaded profile; otherwise "example". One method, shared by the
        /// Achievements tab, the local count/list compute, and (in a later step) the server.
        /// </summary>
        public static string ResolveActiveProfile()
        {
            EnsureLoaded();
            string configured = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configAchievementProfile?.Value;
            if (!string.IsNullOrEmpty(configured) && GetAchievements(configured).Count > 0)
                return configured;
            foreach (var name in GetLoadedProfileNames())
            {
                if (GetAchievements(name).Count > 0) return name;
            }
            return "example";
        }

        public static void ReloadAll()
        {
            _initialized = true;
            _byProfile.Clear();
            // Pure clients parse the server-synced in-memory store (no config files on client disk);
            // server/host reads + seeds its folder as before.
            if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
            {
                // Seed when the folder is missing OR holds no profile: the rig dedi's folder existed but was empty since
                // 10-01, so nothing was ever seeded and every client's leaderboard read "Achievements 0 / 0" (Fire 10-08).
                var folder = GetProfileFolder();
                bool empty = !Directory.Exists(folder);
                if (!empty)
                {
                    try { empty = !Directory.EnumerateFiles(folder, "*.cfg").Any(); } catch { }
                }
                if (empty)
                {
                    try { Directory.CreateDirectory(folder); } catch { }
                    WriteExampleProfile(folder);
                }
            }
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines(MarketplacePaths.Achievements))
            {
                try { LoadOne(kv.Key, kv.Value); }
                catch (System.Exception ex) { Debug.LogError("[AchievementProfileManager] " + kv.Key + ": " + ex.Message); }
            }
            Debug.Log("[AchievementProfileManager] Loaded " + _byProfile.Values.Sum(v => v.Count)
                      + " achievement(s) across " + _byProfile.Count + " profile(s)");
        }

        public static string GetProfileFolder() => MarketplacePaths.GetFolder(MarketplacePaths.Achievements);

        private static void LoadOne(string file, string[] lines)
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
                        AccentColor = ParseAccentColor(kgBlock[4]),
                        MockProgress = 0,
                    });
                }
                // Keep kgHeader: a single [profile] header can hold MANY blank-separated 6-line blocks
                // (the AutoGen_*.cfg shape). A new [header] line overwrites it; per-achievement-header files
                // (the KG/VerdantsAscent shape) still load one-profile-per-achievement, unchanged.
                kgBlock.Clear();
            }

            foreach (var raw in lines)
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

        // The KG block's line index 4 is "R,G,B" (0-255 per channel) — the generator's locked per-tier color.
        // Parse it into a Color; fall back to the neutral parchment-gray when it's missing or malformed.
        private static Color ParseAccentColor(string rgb)
        {
            if (string.IsNullOrEmpty(rgb)) return AchievementDef.NeutralAccent;
            var parts = rgb.Replace(" ", "").Split(',');
            if (parts.Length < 3) return AchievementDef.NeutralAccent;
            if (int.TryParse(parts[0], out int r) && int.TryParse(parts[1], out int g) && int.TryParse(parts[2], out int b))
                return new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f), 1f);
            return AchievementDef.NeutralAccent;
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
