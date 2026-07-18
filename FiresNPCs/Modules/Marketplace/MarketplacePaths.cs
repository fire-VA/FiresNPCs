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

        /// <summary>Resolves the .cfg folder for a module (e.g. <c>MarketplacePaths.GetFolder(MarketplacePaths.Gamblers)</c>)
        /// through the shared family resolver — the grouped <c>FiresRPGmaker/Marketplace/&lt;module&gt;</c> path. Legacy
        /// locations are folded in once by <see cref="FiresCore.Storage.FiresConfigPaths.Migrate"/> at Core startup.</summary>
        public static string GetFolder(string moduleFolder)
            => FiresCore.Storage.FiresConfigPaths.GroupedPath(moduleFolder);
    }
}
