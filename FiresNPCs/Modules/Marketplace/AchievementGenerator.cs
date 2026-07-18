using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Probes the LIVE loaded game (ZNetScene creatures + pickables, Piece.PieceCategory, Skills.SkillType)
    /// and writes baseline achievement .cfg files in the 6-line KG block format that
    /// <see cref="AchievementProfileManager"/> reads. Output = AutoGen_*.cfg in the achievement folder;
    /// only AutoGen_* files are touched, never hand-authored profiles.
    ///
    /// Each file emits ONE [profile] header then N headerless 6-line blocks (TriggerType / Name / Description /
    /// Prefab,Amount / R,G,B / Score) separated by blanks — the loader flushes each block on the blank line and
    /// keeps them all under the one profile (see the LoadOne kgHeader fix).
    ///
    /// Generated achievements progress via <see cref="LeaderboardTracker"/>: SkillLevel = live skill read,
    /// per-category StructuresBuilt + per-pickable Harvested tracked by the extended Harmony patches.
    /// </summary>
    public static class AchievementGenerator
    {
        private struct Tier { public string Prefix, Color; public Tier(string p, string c) { Prefix = p; Color = c; } }
        // Tier colors are locked across every ladder in the reference config.
        private static readonly Tier[] Tiers =
        {
            new Tier("Novice",     "190, 190, 190"),
            new Tier("Apprentice", "70, 137, 56"),
            new Tier("Seasoned",   "135, 206, 250"),
            new Tier("Legendary",  "164, 94, 181"),
            new Tier("Master",     "209, 156, 36"),
        };

        // Per-scheme thresholds + scores, index 0..4 = Novice..Master (pulled from the owner's cfg).
        private static readonly int[] MonThr = { 10, 50, 100, 500, 1000 };
        private static readonly int[] MonScore = { 100, 200, 500, 1000, 2000 };
        private static readonly int[] GatherThr = { 10, 50, 100, 500, 1000 };
        private static readonly int[] GatherScore = { 10, 50, 100, 500, 1000 };
        private static readonly int[] SkillThr = { 20, 40, 60, 80, 100 };
        private static readonly int[] SkillScore = { 10, 50, 100, 500, 1000 };
        private static readonly int[] DiedThr = { 1, 5, 10, 25, 50 };
        private static readonly int[] DiedScore = { 5, 25, 50, 100, 500 };
        private static readonly int[] ExplThr = { 10, 25, 50, 75, 100 };
        private static readonly int[] ExplScore = { 10, 50, 100, 500, 1000 };
        // Tool-use ladders. Terraforming/cultivating are counted per terrain tick (coalesced), so the counts climb
        // fast — higher thresholds than the gather ladder. Cultivator is slower than the hoe, so a gentler ladder.
        private static readonly int[] TerraformThr = { 50, 250, 1000, 5000, 15000 };
        private static readonly int[] CultivateThr = { 10, 50, 250, 1000, 5000 };
        // Bosses: 3 tiers using ladder indices 0,2,4 (Novice/Seasoned/Master) at thresholds 1/3/5.
        private static readonly int[] BossIdx = { 0, 2, 4 };
        private static readonly int[] BossThr = { 1, 3, 5 };

        private static readonly HashSet<string> ModNpcNames = new(System.StringComparer.OrdinalIgnoreCase)
        { "StaticNpc", "CompanionNpc", "CompanionNpc_Wild", "BaseNpc", "NPC", "Static Quest NPC", "Static NPC" };

        // Vanilla ships no PieceCategory -> string map; this is generator-owned.
        private static readonly Dictionary<string, string> CatName = new()
        {
            { "Misc", "Miscellaneous" }, { "Crafting", "Crafting Station" },
            { "BuildingWorkbench", "Wood Building" }, { "BuildingStonecutter", "Stone Building" },
            { "Furniture", "Furniture" }, { "Feasts", "Feast" }, { "Food", "Food" }, { "Meads", "Mead" },
        };

        private struct Ach { public string Trigger, Name, Desc, Target, Color; public int Score; }

        public static string Generate(string profile, System.Action<string> log)
        {
            void Say(string s) { (log ?? (l => Debug.Log("[gen_achievements] " + l)))(s); }
            if (ZNetScene.instance == null || ObjectDB.instance == null || Player.m_localPlayer == null)
                return "Load into a world first — the probe reads the live ZNetScene + player.";
            profile = string.IsNullOrWhiteSpace(profile) ? "server" : profile.Trim().ToLowerInvariant();

            string folder = AchievementProfileManager.GetProfileFolder();
            try { Directory.CreateDirectory(folder); } catch (System.Exception ex) { return "cannot create " + folder + ": " + ex.Message; }
            foreach (var f in Directory.GetFiles(folder, "AutoGen_*.cfg")) { try { File.Delete(f); } catch { } }

            int m = WriteFile(folder, "AutoGen_Monsters.cfg", profile, BuildMonsters());
            int sk = WriteFile(folder, "AutoGen_Skills.cfg", profile, BuildSkills());
            int h = WriteFile(folder, "AutoGen_Harvest.cfg", profile, BuildHarvest());
            int b = WriteFile(folder, "AutoGen_Build.cfg", profile, BuildBuild());
            int g = WriteFile(folder, "AutoGen_Generic.cfg", profile, BuildGeneric());
            int tu = WriteFile(folder, "AutoGen_ToolUse.cfg", profile, BuildToolUse());
            int total = m + sk + h + b + g + tu;

            AchievementProfileManager.ReloadAll();
            Say($"monsters={m} skills={sk} harvest={h} build={b} generic={g} tooluse={tu}");
            Say($"-> {total} achievements under profile '{profile}' in {folder}");
            return $"generated {total} achievements (profile '{profile}'). View with: firesnpcs_achievements {profile}";
        }

        // ---- builders ----

        private static List<Ach> BuildMonsters()
        {
            var list = new List<Ach>();
            var seen = new HashSet<string>();
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                var ch = prefab.GetComponent<Character>();
                if (ch == null) continue;
                if (prefab.GetComponent<BaseAI>() == null) continue;        // no AI => not a fightable creature
                if (IsModNpc(prefab)) continue;                              // exclude our own NPCs/companions FIRST
                if (ch.m_faction == Character.Faction.Players) continue;     // player-allied (tames spawn as Players)
                string key = prefab.name;
                if (!seen.Add(key)) continue;
                string subject = CleanName(Localized(ch.m_name), key);

                if (ch.m_boss)
                {
                    for (int t = 0; t < BossIdx.Length; t++)
                    {
                        int i = BossIdx[t]; int n = BossThr[t];
                        list.Add(new Ach
                        {
                            Trigger = "MonstersKilled",
                            Name = Tiers[i].Prefix + " " + subject + " Slayer",
                            Desc = "Slain " + n + " " + subject + Honor(i, false),
                            Target = key + ", " + n,
                            Color = Tiers[i].Color, Score = MonScore[i],
                        });
                    }
                }
                else
                {
                    string s = subject;
                    Add5(list, "MonstersKilled", "Hunter", subject, key, MonThr, MonScore,
                         (n, i) => "Slain " + n + " " + s + Honor(i, false));
                }
            }
            return list;
        }

        private static List<Ach> BuildHarvest()
        {
            var list = new List<Ach>();
            var seen = new HashSet<string>();
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                var pick = prefab.GetComponent<Pickable>();
                if (pick == null) continue;
                string key = prefab.name;
                if (!seen.Add(key)) continue;
                string subject = CleanName(PickableName(pick), key);
                string s = subject;
                Add5(list, "Harvested", "Harvester", subject, key, GatherThr, GatherScore,
                     (n, i) => "Harvested " + n + " " + s + Honor(i, false));
            }
            return list;
        }

        private static List<Ach> BuildSkills()
        {
            var list = new List<Ach>();
            foreach (Skills.SkillType skill in System.Enum.GetValues(typeof(Skills.SkillType)))
            {
                if (skill == Skills.SkillType.None || skill == Skills.SkillType.All) continue;
                string key = skill.ToString();
                string subject = CleanName(Localized("$skill_" + key.ToLowerInvariant()), key);
                string s = subject;
                Add5(list, "SkillLevel", "Adept", subject, key, SkillThr, SkillScore,
                     (n, i) => "Reached level " + n + " in " + s + Honor(i, false));
            }
            return list;
        }

        private static List<Ach> BuildBuild()
        {
            var list = new List<Ach>();
            foreach (Piece.PieceCategory cat in System.Enum.GetValues(typeof(Piece.PieceCategory)))
            {
                if ((int)cat >= (int)Piece.PieceCategory.Max) continue;      // Max + All sentinels
                string key = cat.ToString();
                string subject = CatName.TryGetValue(key, out var d) ? d : key;
                string s = subject;
                Add5(list, "StructuresBuilt", "Builder", subject, key, GatherThr, GatherScore,
                     (n, i) => "Built " + n + " " + s + " pieces" + Honor(i, false));
            }
            // Overall total (bare amount, empty prefab slot -> tracker reads BuiltStructures).
            Add5(list, "StructuresBuilt", "Builder", "", "", GatherThr, GatherScore,
                 (n, i) => "Built " + n + " structures" + Honor(i, false));
            return list;
        }

        private static List<Ach> BuildGeneric()
        {
            var list = new List<Ach>();
            Add5(list, "Died", "Reaper", "", "", DiedThr, DiedScore,
                 (n, i) => "Died " + n + " time" + (n == 1 ? "" : "s") + Honor(i, true));
            Add5(list, "Explored", "Explorer", "", "", ExplThr, ExplScore,
                 (n, i) => "Explored " + n + "% of the world" + Honor(i, false));
            return list;
        }

        // Hoe + cultivator ladders. These read the leaderboard's Terraformed / Cultivated tallies (kept OUT of
        // the build count), so terraforming a mountain earns a Landscaper tier instead of inflating "Builder".
        private static List<Ach> BuildToolUse()
        {
            var list = new List<Ach>();
            Add5(list, "Terraformed", "Landscaper", "", "", TerraformThr, GatherScore,
                 (n, i) => "Reshaped the land " + n + " times" + Honor(i, false));
            Add5(list, "Cultivated", "Farmer", "", "", CultivateThr, GatherScore,
                 (n, i) => "Worked the soil " + n + " times" + Honor(i, false));
            return list;
        }

        // ---- helpers ----

        private static void Add5(List<Ach> list, string trigger, string role, string subject, string key,
                                 int[] thr, int[] score, System.Func<int, int, string> desc)
        {
            for (int i = 0; i < 5; i++)
            {
                list.Add(new Ach
                {
                    Trigger = trigger,
                    Name = string.IsNullOrEmpty(subject) ? Tiers[i].Prefix + " " + role
                                                         : Tiers[i].Prefix + " " + subject + " " + role,
                    Desc = desc(thr[i], i),
                    Target = string.IsNullOrEmpty(key) ? thr[i].ToString() : key + ", " + thr[i],
                    Color = Tiers[i].Color,
                    Score = score[i],
                });
            }
        }

        // Legendary/Master honorifics; death ladders use the trial/challenge variants.
        private static string Honor(int tierIdx, bool death)
        {
            if (tierIdx == 3) return death ? ", Odin's trial" : ", Odin's pride";
            if (tierIdx == 4) return death ? ", Valhalla's challenge" : ", Valhalla's glory";
            return "";
        }

        private static int WriteFile(string folder, string filename, string profile, List<Ach> achs)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(profile).Append(']').Append('\n');
            foreach (var a in achs)
            {
                sb.Append(a.Trigger).Append('\n');
                sb.Append(a.Name).Append('\n');
                sb.Append(a.Desc).Append('\n');
                sb.Append(a.Target).Append('\n');
                sb.Append(a.Color).Append('\n');
                sb.Append(a.Score).Append('\n');
                sb.Append('\n');
            }
            try
            {
                File.WriteAllText(Path.Combine(folder, filename), sb.ToString());
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig(
                    Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    filename, sb.ToString());
            }
            catch (System.Exception ex) { Debug.LogError("[gen_achievements] write " + filename + ": " + ex.Message); }
            return achs.Count;
        }

        private static bool IsModNpc(GameObject prefab)
        {
            string n = prefab.name.Replace("(Clone)", "").Trim();
            if (ModNpcNames.Contains(n)) return true;
            if (prefab.GetComponent("NpcController") != null) return true;
            if (prefab.GetComponent("CompanionAI") != null) return true;
            if (prefab.GetComponent("CompanionController") != null) return true;
            return false;
        }

        private static string PickableName(Pickable p)
        {
            try
            {
                if (!string.IsNullOrEmpty(p.m_overrideName)) return Localized(p.m_overrideName);
                if (p.m_itemPrefab != null)
                {
                    var it = p.m_itemPrefab.GetComponent<ItemDrop>();
                    if (it != null && it.m_itemData?.m_shared != null && !string.IsNullOrEmpty(it.m_itemData.m_shared.m_name))
                        return Localized(it.m_itemData.m_shared.m_name);
                }
            }
            catch { }
            return p.gameObject.name;
        }

        private static string Localized(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            try { return Localization.instance != null ? Localization.instance.Localize(token) : token; }
            catch { return token; }
        }

        // Display subject for names/descriptions: strip loc tokens + collapse whitespace; fall back to prefab key.
        private static string CleanName(string display, string fallbackKey)
        {
            string s = (display ?? "").Replace("$", "").Trim();
            if (string.IsNullOrEmpty(s) || s.StartsWith("[")) s = fallbackKey;
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
            return string.IsNullOrEmpty(s) ? fallbackKey : s;
        }
    }
}
