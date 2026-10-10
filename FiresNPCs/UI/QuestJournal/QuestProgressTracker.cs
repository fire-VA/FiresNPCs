using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Services;

namespace FiresRPGmaker.UI.QuestJournal
{
    /// <summary>
    /// Tracks and calculates quest progress for the player's active quests. Progress is stored PER OBJECTIVE
    /// (ActiveQuestData.TargetProgress, indexed by QuestTarget order); Collect/Gather/Harvest additionally
    /// read live inventory for real-time display.
    /// </summary>
    public static class QuestProgressTracker
    {
        /// <summary>Short progress summary for the quest list — "[2/5]" (single target) or "[40%]" (multi).</summary>
        public static string GetProgressSummary(QuestDefinition quest, ActiveQuestData activeEntry)
        {
            if (quest == null || activeEntry == null)
                return "[In Progress]";

            try
            {
                if (quest.QuestTargets != null && quest.QuestTargets.Count == 1)
                {
                    var target = quest.QuestTargets[0];
                    int current = GetProgressForTarget(quest, activeEntry, target);
                    return $"[{current}/{target.Amount}]";
                }

                if (quest.QuestTargets != null && quest.QuestTargets.Count > 1)
                {
                    int totalRequired = 0;
                    int totalProgress = 0;
                    foreach (var target in quest.QuestTargets)
                    {
                        totalRequired += target.Amount;
                        totalProgress += Math.Min(GetProgressForTarget(quest, activeEntry, target), target.Amount);
                    }
                    if (totalRequired > 0)
                    {
                        int percent = (int)((float)totalProgress / totalRequired * 100);
                        return $"[{percent}%]";
                    }
                }

                if (activeEntry.Progress > 0)
                    return $"[{activeEntry.Progress}]";
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestProgressTracker] Error getting progress summary: {ex.Message}");
            }

            return "[In Progress]";
        }

        /// <summary>Detailed per-objective progress for the details panel.</summary>
        public static string GetDetailedProgress(QuestDefinition quest, ActiveQuestData activeEntry)
        {
            if (quest == null || activeEntry == null)
                return "No progress data available.";

            try
            {
                var sb = new System.Text.StringBuilder();
                string questType = quest.QuestType?.ToLowerInvariant() ?? "unknown";

                if (quest.QuestTargets == null || quest.QuestTargets.Count == 0)
                {
                    sb.AppendLine($"<color=#AAAAAA>Progress: {activeEntry.Progress}</color>");
                    return sb.ToString();
                }

                foreach (var target in quest.QuestTargets)
                {
                    int current = GetProgressForTarget(quest, activeEntry, target);
                    bool isComplete = current >= target.Amount;

                    string statusColor = isComplete ? "#66FF66" : "#FFFF66";
                    // Per-objective type wins over the quest-level type for the verb.
                    string actionVerb = GetActionVerb(!string.IsNullOrEmpty(target.Type) ? target.Type.ToLowerInvariant() : questType);
                    string targetName = GetTargetDisplayName(target);

                    sb.AppendLine($"<color={statusColor}>{targetName}: {current} / {target.Amount} {actionVerb}</color>");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestProgressTracker] Error getting detailed progress: {ex.Message}");
                return "Error calculating progress.";
            }
        }

        /// <summary>Current progress for a specific quest target (per-objective).</summary>
        public static int GetProgressForTarget(QuestDefinition quest, ActiveQuestData activeEntry, QuestTarget target)
        {
            if (quest == null || activeEntry == null || target == null)
                return 0;

            try
            {
                int index = quest.QuestTargets != null ? quest.QuestTargets.IndexOf(target) : 0;
                if (index < 0) index = 0;
                int count = quest.QuestTargets != null ? quest.QuestTargets.Count : 1;
                // Prefer THIS objective's own type ("Item:" collect line etc.) over the quest-level type, so a collect
                // objective on a mixed quest reads live inventory instead of a stored counter.
                string objType = (!string.IsNullOrEmpty(target.Type) ? target.Type : quest.QuestType)?.ToLowerInvariant() ?? "unknown";

                switch (objType)
                {
                    case "collect":
                    case "gather":
                    case "harvest":
                    case "item":
                        // Real-time: the higher of live inventory or stored per-objective progress.
                        return GetCollectProgress(activeEntry, target, index, count);
                    default:
                        // Kill / Craft / Build / Talk / Move / Travel / unknown → stored per-objective count.
                        return GetStoredProgress(activeEntry, index, count);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestProgressTracker] Error getting progress for target: {ex.Message}");
                return 0;
            }
        }

        #region Quest Type Specific Progress

        private static int GetCollectProgress(ActiveQuestData activeEntry, QuestTarget target, int index, int count)
        {
            try
            {
                int inventoryCount = PlayerInventoryService.GetPlayerItemCount(target.Prefab);
                int storedProgress = GetStoredProgress(activeEntry, index, count);
                return Math.Max(inventoryCount, storedProgress);
            }
            catch
            {
                return GetStoredProgress(activeEntry, index, count);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>Stored progress for a specific objective (by index), migrating a legacy single counter into objective 0.</summary>
        private static int GetStoredProgress(ActiveQuestData activeEntry, int index, int count)
        {
            return activeEntry?.GetTargetProgress(index, count) ?? 0;
        }

        /// <summary>Gets the appropriate action verb for a quest type.</summary>
        private static string GetActionVerb(string questType)
        {
            switch (questType?.ToLowerInvariant())
            {
                case "kill":
                    return "killed";
                case "collect":
                case "gather":
                    return "collected";
                case "harvest":
                    return "harvested";
                case "craft":
                    return "crafted";
                case "build":
                    return "built";
                case "talk":
                    return "talked to";
                case "move":
                case "travel":
                    return "reached";
                default:
                    return "completed";
            }
        }

        /// <summary>Localized/formatted display name for a prefab (items, creatures, pieces).</summary>
        private static string GetDisplayName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return "Unknown";

            string displayName = PlayerInventoryService.GetItemDisplayName(prefabName);
            if (!string.IsNullOrEmpty(displayName) && displayName != prefabName)
                return displayName;

            try
            {
                if (ZNetScene.instance != null)
                {
                    var prefab = ZNetScene.instance.GetPrefab(prefabName);
                    if (prefab != null)
                    {
                        var character = prefab.GetComponent<Character>();
                        if (character != null)
                        {
                            string charName = character.m_name;
                            if (!string.IsNullOrEmpty(charName))
                            {
                                if (charName.StartsWith("$"))
                                {
                                    string localized = Localization.instance?.Localize(charName);
                                    if (!string.IsNullOrEmpty(localized) && localized != charName)
                                        return localized;
                                }
                                else
                                {
                                    return charName;
                                }
                            }
                        }

                        var humanoid = prefab.GetComponent<Humanoid>();
                        if (humanoid != null)
                        {
                            string humanoidName = humanoid.m_name;
                            if (!string.IsNullOrEmpty(humanoidName))
                            {
                                if (humanoidName.StartsWith("$"))
                                {
                                    string localized = Localization.instance?.Localize(humanoidName);
                                    if (!string.IsNullOrEmpty(localized) && localized != humanoidName)
                                        return localized;
                                }
                                else
                                {
                                    return humanoidName;
                                }
                            }
                        }

                        var piece = prefab.GetComponent<Piece>();
                        if (piece != null)
                        {
                            string pieceName = piece.m_name;
                            if (!string.IsNullOrEmpty(pieceName))
                            {
                                if (pieceName.StartsWith("$"))
                                {
                                    string localized = Localization.instance?.Localize(pieceName);
                                    if (!string.IsNullOrEmpty(localized) && localized != pieceName)
                                        return localized;
                                }
                                else
                                {
                                    return pieceName;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            displayName = System.Text.RegularExpressions.Regex.Replace(prefabName, "([a-z])([A-Z])", "$1 $2");
            displayName = System.Text.RegularExpressions.Regex.Replace(displayName, "([A-Z]+)([A-Z][a-z])", "$1 $2");
            if (displayName.Length > 0)
                displayName = char.ToUpper(displayName[0]) + displayName.Substring(1);

            return displayName;
        }

        /// <summary>Display name for a quest target, preferring NameFilter (M_Name) if set.</summary>
        public static string GetTargetDisplayName(QuestTarget target)
        {
            if (target == null)
                return "Unknown";
            if (!string.IsNullOrEmpty(target.NameFilter))
                return target.NameFilter;
            return GetDisplayName(target.Prefab);
        }

        #endregion

        #region Quest Completion Checking

        /// <summary>True when every objective is complete.</summary>
        public static bool IsQuestComplete(QuestDefinition quest, ActiveQuestData activeEntry)
        {
            if (quest == null || activeEntry == null)
                return false;

            if (quest.QuestTargets == null || quest.QuestTargets.Count == 0)
                return activeEntry.Progress > 0;

            foreach (var target in quest.QuestTargets)
            {
                int progress = GetProgressForTarget(quest, activeEntry, target);
                if (progress < target.Amount)
                    return false;
            }

            return true;
        }

        /// <summary>Overall completion percentage (0-100).</summary>
        public static int GetCompletionPercentage(QuestDefinition quest, ActiveQuestData activeEntry)
        {
            if (quest == null || activeEntry == null)
                return 0;

            if (quest.QuestTargets == null || quest.QuestTargets.Count == 0)
                return activeEntry.Progress > 0 ? 100 : 0;

            int totalRequired = 0;
            int totalProgress = 0;
            foreach (var target in quest.QuestTargets)
            {
                totalRequired += target.Amount;
                totalProgress += Math.Min(GetProgressForTarget(quest, activeEntry, target), target.Amount);
            }

            if (totalRequired == 0)
                return 0;

            return (int)((float)totalProgress / totalRequired * 100);
        }

        #endregion
    }
}
