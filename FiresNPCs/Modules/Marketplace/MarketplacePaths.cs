using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Central path resolver for every Marketplace-module .cfg folder. Lives under
    /// <c>BepInEx/config/FiresRPGmaker/&lt;ModuleFolder&gt;/</c> so it sits alongside the existing
    /// RPGMaker config dirs (Quests, Dialogues, Traders, etc.) instead of a separate top-level dir.
    ///
    /// Subfolder names mirror KG's Marketplace folder layout so a user can drop KG's
    /// <c>Marketplace\Configs\&lt;Bankers|Gamblers|...&gt;\</c> .cfg files straight into ours and the
    /// matching manager picks them up:
    ///   Bankers, Gamblers, LeaderboardAchievements, Transmogrifications, Marketplace, Mail
    ///
    /// On first access per folder, any leftover content from the older <c>config/firesnpcs/&lt;module&gt;/</c>
    /// path is moved over once so existing edits don't get orphaned.
    /// </summary>
    public static class MarketplacePaths
    {
        // KG-aligned subfolder names so a straight drop-in of KG's Configs/ works.
        public const string Bankers       = "Bankers";
        public const string Gamblers      = "Gamblers";
        public const string Achievements  = "LeaderboardAchievements";
        public const string Leaderboard   = "LeaderboardAchievements";
        public const string Transmogs     = "Transmogrifications";
        public const string Marketplace   = "Marketplace";
        public const string Mail          = "Mail";

        // Old subfolder names that were under config/firesnpcs/ before we migrated to FiresRPGmaker/.
        private static readonly Dictionary<string, string> _legacyByCurrent = new()
        {
            { Bankers,      "banker" },
            { Gamblers,     "gambler" },
            { Achievements, "achievement" },
            { Transmogs,    "transmog" },
            { Marketplace,  "marketplace" },
            { Mail,         "mail" },
        };

        private static readonly HashSet<string> _migrated = new();

        /// <summary>Resolves the .cfg folder for a module (e.g. <c>MarketplacePaths.GetFolder(MarketplacePaths.Gamblers)</c>).
        /// Creates the folder if missing and migrates any legacy <c>firesnpcs/</c> contents over once.</summary>
        public static string GetFolder(string moduleFolder)
        {
            string dst = Path.Combine(BepInEx.Paths.ConfigPath, "FiresRPGmaker", moduleFolder);
            MigrateLegacyIfNeeded(moduleFolder, dst);
            try { Directory.CreateDirectory(dst); } catch { }
            return dst;
        }

        private static void MigrateLegacyIfNeeded(string moduleFolder, string dst)
        {
            if (_migrated.Contains(moduleFolder)) return;
            _migrated.Add(moduleFolder);
            if (!_legacyByCurrent.TryGetValue(moduleFolder, out string legacyName)) return;

            // Try TWO legacy locations: the original BepInEx/config/firesnpcs/<oldname>/ (pre-2026-06-08)
            // AND BepInEx/config/FiresRPGmaker/<oldname>/ (where the user manually parked files).
            string[] legacyRoots = {
                Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs",     legacyName),
                Path.Combine(BepInEx.Paths.ConfigPath, "FiresRPGmaker", legacyName),
            };

            try { Directory.CreateDirectory(dst); } catch { }
            int totalMoved = 0;
            foreach (var legacy in legacyRoots)
            {
                if (!Directory.Exists(legacy) || string.Equals(legacy, dst, System.StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(legacy, "*.cfg"))
                    {
                        string dstFile = Path.Combine(dst, Path.GetFileName(f));
                        if (!File.Exists(dstFile)) { File.Move(f, dstFile); totalMoved++; }
                        else { File.Delete(f); }
                    }
                    try { if (Directory.GetFiles(legacy).Length == 0) Directory.Delete(legacy, recursive: false); } catch { }
                }
                catch (System.Exception ex) { Debug.LogError("[MarketplacePaths] migrate from " + legacy + " failed: " + ex.Message); }
            }
            if (totalMoved > 0)
                Debug.Log("[MarketplacePaths] migrated " + totalMoved + " .cfg(s) -> FiresRPGmaker/" + moduleFolder);
        }
    }
}
