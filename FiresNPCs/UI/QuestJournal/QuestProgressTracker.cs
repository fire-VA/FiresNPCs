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
    /// Tracks and calculates quest progress for the player's active quests.
 /// This class centralizes all progress tracking logic for different quest types.
 /// </summary>
    public static class QuestProgressTracker
    {
        /// <summary>
        /// Gets a short summary of quest progress for display in the quest list.
        /// </summary>
        /// <param name="quest">The quest definition</param>
  /// <param name="activeEntry">The player's active quest data</param>
        /// <returns>A short progress string like "[2/5]" or "In Progress"</returns>
        public static string GetProgressSummary(QuestDefinition quest, ActiveQuestData activeEntry)
{
            if (quest == null || activeEntry == null)
      return "[In Progress]";

       try
    {
        // For single-target quests, show simple progress
  if (quest.QuestTargets != null && quest.QuestTargets.Count == 1)
           {
           var target = quest.QuestTargets[0];
    int current = GetProgressForTarget(quest, activeEntry, target);
    return $"[{current}/{target.Amount}]";
  }

   // For multi-target quests, show overall progress
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

                // Fallback: use the stored progress value
     if (activeEntry.Progress > 0)
      {
  return $"[{activeEntry.Progress}]";
                }
      }
    catch (Exception ex)
            {
    Debug.LogWarning($"[QuestProgressTracker] Error getting progress summary: {ex.Message}");
            }

        return "[In Progress]";
        }

        /// <summary>
        /// Gets detailed progress information for display in the quest details panel.
        /// </summary>
        /// <param name="quest">The quest definition</param>
        /// <param name="activeEntry">The player's active quest data</param>
        /// <returns>Detailed progress text with item/kill counts</returns>
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
     string actionVerb = GetActionVerb(questType);
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

        /// <summary>
        /// Gets the current progress for a specific quest target.
        /// </summary>
        /// <param name="quest">The quest definition</param>
        /// <param name="activeEntry">The player's active quest data</param>
  /// <param name="target">The specific target to check progress for</param>
        /// <returns>Current progress count</returns>
        public static int GetProgressForTarget(QuestDefinition quest, ActiveQuestData activeEntry, QuestTarget target)
        {
     if (quest == null || activeEntry == null || target == null)
      return 0;

        try
            {
   string questType = quest.QuestType?.ToLowerInvariant() ?? "unknown";

    switch (questType)
       {
 case "kill":
      return GetKillProgress(activeEntry, target);

         case "collect":
      case "gather":
        case "harvest":
     return GetCollectProgress(activeEntry, target);

   case "craft":
             return GetCraftProgress(activeEntry, target);

   case "build":
  return GetBuildProgress(activeEntry, target);

     case "talk":
   return GetTalkProgress(activeEntry, target);

           case "move":
          case "travel":
  return GetTravelProgress(activeEntry, target);

         default:
        // For unknown types, use the stored progress value
          // This is a single-value progress, so just return it for the first target
    if (quest.QuestTargets.IndexOf(target) == 0)
        return activeEntry.Progress;
     return 0;
     }
   }
 catch (Exception ex)
  {
                Debug.LogWarning($"[QuestProgressTracker] Error getting progress for target: {ex.Message}");
    return 0;
      }
        }

        #region Quest Type Specific Progress

   /// <summary>
        /// Gets kill progress for a target.
        /// Kill quests track kills via the quest tracking system.
        /// </summary>
        private static int GetKillProgress(ActiveQuestData activeEntry, QuestTarget target)
      {
          // Kill progress is typically stored in the Progress field or tracked via ZDO
      // For now, we use the stored Progress value
            // In the future, this could read from more detailed tracking
      return GetStoredProgress(activeEntry, target);
        }

    /// <summary>
        /// Gets collect/gather progress for a target.
        /// For collect quests, we can check the player's current inventory.
   /// </summary>
        private static int GetCollectProgress(ActiveQuestData activeEntry, QuestTarget target)
     {
    // For collect quests, check if we should count inventory items
            // This provides real-time progress even if the tracking hasn't been updated
          try
     {
     int inventoryCount = PlayerInventoryService.GetPlayerItemCount(target.Prefab);
        int storedProgress = GetStoredProgress(activeEntry, target);

      // Return the higher of the two (inventory might have more items than tracked)
       return Math.Max(inventoryCount, storedProgress);
        }
   catch
            {
    return GetStoredProgress(activeEntry, target);
            }
        }

        /// <summary>
  /// Gets craft progress for a target.
        /// </summary>
        private static int GetCraftProgress(ActiveQuestData activeEntry, QuestTarget target)
        {
            return GetStoredProgress(activeEntry, target);
      }

        /// <summary>
 /// Gets build progress for a target.
        /// </summary>
        private static int GetBuildProgress(ActiveQuestData activeEntry, QuestTarget target)
     {
     return GetStoredProgress(activeEntry, target);
        }

        /// <summary>
        /// Gets talk progress for a target.
        /// Talk quests are typically binary (talked or not).
        /// </summary>
        private static int GetTalkProgress(ActiveQuestData activeEntry, QuestTarget target)
   {
    return GetStoredProgress(activeEntry, target);
        }

        /// <summary>
        /// Gets travel progress for a target.
        /// Travel quests are typically binary (reached location or not).
        /// </summary>
        private static int GetTravelProgress(ActiveQuestData activeEntry, QuestTarget target)
        {
            return GetStoredProgress(activeEntry, target);
     }

        #endregion

        #region Helper Methods

        /// <summary>
   /// Gets the stored progress value for a target from the active quest entry.
        /// For single-target quests, this is simply the Progress field.
  /// For multi-target quests, you would need to expand this to track per-target progress.
/// </summary>
        private static int GetStoredProgress(ActiveQuestData activeEntry, QuestTarget target)
        {
         // Currently, ActiveQuestData only has a single Progress field.
 // For multi-target quests, you may want to extend this to track per-target progress.
      // For now, we return the overall progress for the first target.
  return activeEntry?.Progress ?? 0;
   }

        /// <summary>
        /// Gets the appropriate action verb for a quest type.
        /// </summary>
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

        /// <summary>
 /// Gets a display-friendly name for a prefab.
 /// Tries to get the localized name from the game first, then falls back to formatting.
      /// </summary>
private static string GetDisplayName(string prefabName)
   {
   if (string.IsNullOrEmpty(prefabName))
    return "Unknown";

  // Try to get the item's display name from the game (items)
      string displayName = PlayerInventoryService.GetItemDisplayName(prefabName);

  // If we got a different name, use it
   if (!string.IsNullOrEmpty(displayName) && displayName != prefabName)
       return displayName;

    // Try to get creature/character display name from ZNetScene
   try
    {
        if (ZNetScene.instance != null)
     {
 var prefab = ZNetScene.instance.GetPrefab(prefabName);
   if (prefab != null)
     {
   // Try Character component (for creatures/enemies)
     var character = prefab.GetComponent<Character>();
   if (character != null)
      {
       // Get m_name field which contains the localization key
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

     // Try Humanoid component as fallback
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

         // Try Piece component (for buildables)
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

   // Fallback: make the prefab name more readable
    // Convert camelCase/PascalCase to spaces and capitalize
    displayName = System.Text.RegularExpressions.Regex.Replace(prefabName, "([a-z])([A-Z])", "$1 $2");
   displayName = System.Text.RegularExpressions.Regex.Replace(displayName, "([A-Z]+)([A-Z][a-z])", "$1 $2");

// Capitalize first letter
    if (displayName.Length > 0)
      displayName = char.ToUpper(displayName[0]) + displayName.Substring(1);

 return displayName;
    }

  /// <summary>
        /// Gets the display name for a quest target, preferring the NameFilter (M_Name) if set.
  /// </summary>
        /// <param name="target">The quest target</param>
        /// <returns>Display-friendly name for the target</returns>
        public static string GetTargetDisplayName(QuestTarget target)
    {
        if (target == null)
     return "Unknown";

  // If NameFilter is set (from M_Name config), use that as the display name
       if (!string.IsNullOrEmpty(target.NameFilter))
        {
      return target.NameFilter;
         }

    // Otherwise use the prefab name with localization/formatting
 return GetDisplayName(target.Prefab);
        }

        #endregion

     #region Quest Completion Checking

        /// <summary>
        /// Checks if a quest is complete based on current progress.
        /// </summary>
   /// <param name="quest">The quest definition</param>
        /// <param name="activeEntry">The player's active quest data</param>
        /// <returns>True if all objectives are complete</returns>
        public static bool IsQuestComplete(QuestDefinition quest, ActiveQuestData activeEntry)
        {
     if (quest == null || activeEntry == null)
           return false;

 if (quest.QuestTargets == null || quest.QuestTargets.Count == 0)
  {
     // No targets defined, consider complete if progress > 0
  return activeEntry.Progress > 0;
        }

            foreach (var target in quest.QuestTargets)
            {
                int progress = GetProgressForTarget(quest, activeEntry, target);
                if (progress < target.Amount)
            return false;
   }

            return true;
        }

    /// <summary>
    /// Gets the overall completion percentage for a quest.
    /// </summary>
    /// <param name="quest">The quest definition</param>
    /// <param name="activeEntry">The player's active quest data</param>
    /// <returns>Completion percentage (0-100)</returns>
        public static int GetCompletionPercentage(QuestDefinition quest, ActiveQuestData activeEntry)
        {
            if (quest == null || activeEntry == null)
    return 0;

        if (quest.QuestTargets == null || quest.QuestTargets.Count == 0)
     {
            return activeEntry.Progress > 0 ? 100 : 0;
            }

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
