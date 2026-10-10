using System.Collections.Generic;
using FiresCore.Bridge;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Offers the Marketplace achievements to any mod that displays achievements, through Core's
    /// <see cref="AchievementBridge"/>. RPGmaker keeps every bit of authority - the definitions, the
    /// active profile and the completion predicate - and hands out a read-only snapshot.
    ///
    /// Read on demand, never cached: progress comes from the live <see cref="LeaderboardTracker"/>, the
    /// same source the leaderboard board and <see cref="AchievementAnnouncer"/> use, so a reader can
    /// never show a staler number than the board does.
    /// </summary>
    public static class AchievementCatalogue
    {
        public const string SourceId = "rpgmaker";

        private const string DisplayName = "RPG Maker";
        private const int DisplayOrder = 10;

        public static void Register()
        {
            if (FiresCore.Lifecycle.FiresMod.IsHeadless) return;
            AchievementBridge.RegisterCatalogue(SourceId, DisplayName, Read, DisplayOrder);
        }

        private static IReadOnlyList<AchievementRow> Read()
        {
            var rows = new List<AchievementRow>();

            string profile = AchievementProfileManager.ResolveActiveProfile();
            if (string.IsNullOrEmpty(profile)) return rows;

            IReadOnlyList<AchievementDef> defs = AchievementProfileManager.GetAchievements(profile);
            if (defs == null) return rows;

            string localName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Local";

            for (int i = 0; i < defs.Count; i++)
            {
                AchievementDef def = defs[i];
                if (def == null) continue;

                int progress = LeaderboardTracker.GetProgress(localName, def.TriggerType, def.Prefab);
                rows.Add(new AchievementRow
                {
                    Id = def.Id.ToString(),
                    Name = def.Name ?? string.Empty,
                    Description = def.Description ?? string.Empty,
                    Group = profile,
                    Progress = progress,
                    Target = def.MinAmount,
                    Earned = progress >= def.MinAmount,
                    Score = def.Score,
                    Accent = def.AccentColor
                });
            }

            return rows;
        }
    }
}
