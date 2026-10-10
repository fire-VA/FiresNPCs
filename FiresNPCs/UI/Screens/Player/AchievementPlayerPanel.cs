namespace FiresRPGmaker.UI.Screens.PlayerPanels
{
    /// <summary>
    /// Shortcut entry-point that forwards to the merged Leaderboard panel, opened on the Achievements tab.
    /// (Per user feedback 2026-06-07: "achievements are meant to be part of the leaderboard ui".)
    /// </summary>
    public static class AchievementPlayerPanel
    {
        public static void Show(string achievementProfile, string npcDisplayName)
        {
            LeaderboardPlayerPanel.Show(
                leaderboardProfile: achievementProfile,
                npcDisplayName: npcDisplayName,
                startTab: LeaderboardPlayerPanel.InnerTab.Achievements,
                achievementProfile: achievementProfile);
        }
    }
}
