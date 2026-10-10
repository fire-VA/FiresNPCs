using System;
using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Vault
{
    [Serializable]
    public class ActiveQuestData
    {
        public string QuestId { get; set; }

        // Legacy single counter. Kept for back-compat and for quests with no targets. For quests WITH
        // targets it now mirrors the SUM of TargetProgress so any older reader still sees movement.
        public int Progress { get; set; } = 0;

        // Per-objective progress, indexed by QuestTarget order. Null on quests saved before this existed;
        // lazily seeded from Progress (objective 0) on first access so old multi-objective quests migrate cleanly.
        public List<int> TargetProgress { get; set; }

        public DateTime StartedAt { get; set; }

        /// <summary>Progress for one objective (by index), migrating a legacy single counter into objective 0.</summary>
        public int GetTargetProgress(int index, int targetCount)
        {
            EnsureTargetProgress(targetCount);
            if (TargetProgress != null && index >= 0 && index < TargetProgress.Count) return TargetProgress[index];
            return index == 0 ? Progress : 0;
        }

        /// <summary>Set one objective's progress and keep the legacy Progress field as the sum across objectives.</summary>
        public void SetTargetProgress(int index, int value, int targetCount)
        {
            EnsureTargetProgress(targetCount);
            if (TargetProgress == null || index < 0) return;
            while (TargetProgress.Count <= index) TargetProgress.Add(0);
            TargetProgress[index] = value < 0 ? 0 : value;
            int sum = 0;
            foreach (var v in TargetProgress) sum += v;
            Progress = sum;
        }

        public void EnsureTargetProgress(int targetCount)
        {
            if (targetCount <= 0) return;
            if (TargetProgress == null)
            {
                TargetProgress = new List<int>(targetCount);
                for (int i = 0; i < targetCount; i++) TargetProgress.Add(0);
                if (Progress > 0) TargetProgress[0] = Progress; // migrate legacy single counter into objective 0
            }
            else
            {
                while (TargetProgress.Count < targetCount) TargetProgress.Add(0);
            }
        }

        /// <summary>True only when EVERY objective's stored progress meets its target amount (multi-objective safe).</summary>
        public bool AllTargetsComplete(global::FiresRPGmaker.Modules.Quest.QuestDefinition def)
        {
            if (def?.QuestTargets == null || def.QuestTargets.Count == 0) return Progress > 0;
            for (int i = 0; i < def.QuestTargets.Count; i++)
                if (GetTargetProgress(i, def.QuestTargets.Count) < def.QuestTargets[i].Amount) return false;
            return true;
        }
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
