using System;

namespace FiresRPGmaker.Modules.Vault
{
    [Serializable]
    public class ActiveQuestData
    {
        public string QuestId { get; set; }
        public int Progress { get; set; } = 0;
        public DateTime StartedAt { get; set; }
    }

    // Tracks completion info for a quest including timestamp for cooldown calculation
    [Serializable]
    public class CompletedQuestEntry
    {
        public int TimesCompleted { get; set; } = 1;
        public DateTime LastCompletedAt { get; set; } = DateTime.UtcNow;

        public CompletedQuestEntry() { }

        public CompletedQuestEntry(int times, DateTime completedAt)
        {
            TimesCompleted = times;
            LastCompletedAt = completedAt;
        }
    }
}
