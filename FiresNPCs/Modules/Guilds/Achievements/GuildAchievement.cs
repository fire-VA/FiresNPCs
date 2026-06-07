using System;
using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Guilds.Achievements
{
    /// <summary>
    /// Definition of a single guild achievement milestone.
    /// Achievements are progress-based: they unlock when the guild's tracked
    /// value for this achievement reaches the defined threshold.
    /// </summary>
    [Serializable]
    public class GuildAchievementDef
    {
        public string Id;
        public string Name;
        public string Description;
        // Progress thresholds (e.g. [10, 50, 100] means three tiers)
        public List<float> Thresholds = new List<float>();
    }

    /// <summary>Per-guild runtime progress state for one achievement.</summary>
    [Serializable]
    public class GuildAchievementProgress
    {
        public string      AchievementId;
        public float       Progress;
        public int         TierReached;
        public List<long>  CompletedAtTicks = new List<long>(); // one per tier
    }
}
