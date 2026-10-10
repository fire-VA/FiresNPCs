using System;
using System.Collections.Generic;
using UnityEngine;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;

namespace FiresRPGmaker.Services
{
    /// <summary>
    /// Centralized service for checking quest status and requirements.
    /// This service can be used by quest UIs, chat commands, NPCs, and other systems.
    /// </summary>
    public static class QuestStatusService
    {
   #region Quest Status

      /// <summary>
        /// Represents the player's status for a specific quest.
   /// </summary>
        public enum PlayerQuestStatus
        {
    /// <summary>Quest is not started and not completed</summary>
            None,
            /// <summary>Quest is currently active/in progress</summary>
 Active,
      /// <summary>Quest has been completed (may be repeatable)</summary>
            Completed
        }

        /// <summary>
        /// Gets the player's current status for a quest.
   /// </summary>
        /// <param name="questId">The quest ID</param>
        /// <param name="playerId">Optional player ID (uses local player if 0)</param>
        /// <returns>The player's quest status</returns>
        public static PlayerQuestStatus GetPlayerQuestStatus(string questId, long playerId = 0)
        {
     try
            {
           if (playerId <= 0)
          playerId = Player.m_localPlayer?.GetPlayerID() ?? 0;

        if (playerId <= 0) return PlayerQuestStatus.None;

    var questManager = QuestManager.Instance;
 if (questManager == null) return PlayerQuestStatus.None;

       if (questManager.HasQuest(playerId, questId))
      return PlayerQuestStatus.Active;

    if (questManager.HasCompletedQuest(playerId, questId))
          return PlayerQuestStatus.Completed;
            }
      catch { }

            return PlayerQuestStatus.None;
  }

        #endregion

     #region Cooldown

        /// <summary>
        /// Gets the remaining cooldown time for a completed quest with cooldown.
    /// </summary>
        /// <param name="quest">The quest definition</param>
        /// <param name="playerId">Optional player ID (uses local player if 0)</param>
        /// <returns>Formatted string like "Available in 2d 5h" or null if not on cooldown</returns>
        public static string GetCooldownTimeRemaining(QuestDefinition quest, long playerId = 0)
   {
    if (quest == null || quest.CooldownDays <= 0) return null;

            try
       {
         if (playerId <= 0)
          playerId = Player.m_localPlayer?.GetPlayerID() ?? 0;

  if (playerId <= 0) return null;

     var vault = VaultOfKnowledge.Instance;
     if (vault == null) return null;

    var playerData = vault.GetPlayerData(playerId);
     if (playerData?.CompletedQuests == null) return null;

                if (playerData.CompletedQuests.TryGetValue(quest.QuestId, out var completedEntry))
       {
     // Check if still on cooldown
       DateTime availableTime = completedEntry.LastCompletedAt.AddDays(quest.CooldownDays);
       if (DateTime.UtcNow < availableTime)
 {
     TimeSpan remaining = availableTime - DateTime.UtcNow;
       return FormatTimeRemaining(remaining);
         }
       }
          }
         catch { }

            return null;
        }

        /// <summary>
        /// Formats a TimeSpan into a human-readable remaining time string.
      /// </summary>
   /// <param name="remaining">The remaining time</param>
      /// <returns>Formatted string like "2d 5h" or "45m"</returns>
        public static string FormatTimeRemaining(TimeSpan remaining)
      {
     if (remaining.TotalDays >= 1)
    return $"Available in {(int)remaining.TotalDays}d {remaining.Hours}h";
         else if (remaining.TotalHours >= 1)
          return $"Available in {(int)remaining.TotalHours}h {remaining.Minutes}m";
            else
     return $"Available in {remaining.Minutes}m";
        }

        /// <summary>
    /// Checks if a quest is currently on cooldown for a player.
        /// </summary>
        /// <param name="quest">The quest definition</param>
    /// <param name="playerId">Optional player ID (uses local player if 0)</param>
/// <returns>True if the quest is on cooldown</returns>
        public static bool IsQuestOnCooldown(QuestDefinition quest, long playerId = 0)
    {
      return !string.IsNullOrEmpty(GetCooldownTimeRemaining(quest, playerId));
        }

        #endregion

        #region Requirement Checking

    /// <summary>
     /// Checks a single requirement and returns whether it's met.
        /// </summary>
        /// <param name="requirement">The requirement string (e.g., "HasItem,SwordIron,1")</param>
        /// <param name="playerId">Optional player ID (uses local player if 0)</param>
        /// <returns>True if the requirement is met</returns>
    public static bool CheckSingleRequirement(string requirement, long playerId = 0)
   {
     if (string.IsNullOrEmpty(requirement)) return true;

            if (playerId <= 0)
   playerId = Player.m_localPlayer?.GetPlayerID() ?? 0;

        if (playerId <= 0) return false;

  var questManager = QuestManager.Instance;
    var vault = VaultOfKnowledge.Instance;

  return CheckSingleRequirementInternal(requirement, playerId, questManager, vault);
        }

        /// <summary>
        /// Gets the status of all requirements for a quest.
        /// </summary>
  /// <param name="quest">The quest definition</param>
        /// <param name="playerId">Optional player ID (uses local player if 0)</param>
        /// <returns>Dictionary mapping requirement strings to their met status</returns>
    public static Dictionary<string, bool> GetRequirementStatus(QuestDefinition quest, long playerId = 0)
        {
      var result = new Dictionary<string, bool>();
            if (quest?.Requirements == null || quest.Requirements.Count == 0)
      return result;

   try
            {
       if (playerId <= 0)
                    playerId = Player.m_localPlayer?.GetPlayerID() ?? 0;

       if (playerId <= 0) return result;

         var questManager = QuestManager.Instance;
  var vault = VaultOfKnowledge.Instance;

             foreach (var req in quest.Requirements)
      {
           bool isMet = CheckSingleRequirementInternal(req, playerId, questManager, vault);
             result[req] = isMet;
          }
    }
            catch (Exception ex)
            {
       Debug.LogWarning($"[QuestStatusService] Error checking requirement status: {ex.Message}");
       }

  return result;
        }

   private static bool CheckSingleRequirementInternal(string requirement, long playerId, QuestManager questManager, VaultOfKnowledge vault)
        {
   if (string.IsNullOrEmpty(requirement)) return true;

            var req = requirement.Trim();
            var reqLower = req.ToLowerInvariant();

         try
            {
      PlayerVaultData playerData = null;
     try { playerData = vault?.GetPlayerData(playerId); } catch { playerData = null; }

    if (reqLower.StartsWith("hascompletedquest"))
      {
   var parts = req.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
           if (parts.Length >= 2)
        {
             var questId = parts[1].Trim();
            return CheckCompletedQuest(questId, playerId, questManager, playerData);
     }
         return false;
    }

        if (reqLower.StartsWith("completed:") || reqLower.StartsWith("quest:"))
     {
       var questId = req.Substring(req.IndexOf(':') + 1).Trim();
      return CheckCompletedQuest(questId, playerId, questManager, playerData);
   }

    if (reqLower.StartsWith("hasitem"))
  {
      var parts = req.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length >= 2)
   {
        var prefab = parts[1].Trim();
         int requiredAmount = 1;
  if (parts.Length >= 3) int.TryParse(parts[2].Trim(), out requiredAmount);
       return PlayerInventoryService.HasItem(prefab, requiredAmount, playerId, playerData);
     }
        return false;
           }

                if (reqLower.StartsWith("item:"))
     {
     var itemPart = req.Substring(5).Trim();
      var parts = itemPart.Split(',');
      var prefab = parts[0].Trim();
             int requiredAmount = 1;
        if (parts.Length > 1) int.TryParse(parts[1].Trim(), out requiredAmount);
return PlayerInventoryService.HasItem(prefab, requiredAmount, playerId, playerData);
     }

      if (reqLower.StartsWith("skill:"))
    {
        var skillPart = req.Substring(6).Trim();
        var parts = skillPart.Split(',');
              var skillName = parts[0].Trim();
    int requiredLevel = 1;
         if (parts.Length > 1) int.TryParse(parts[1].Trim(), out requiredLevel);

      var player = Player.m_localPlayer;
       if (player != null)
              {
          if (Enum.TryParse<Skills.SkillType>(skillName, true, out var skillType))
      {
             float level = player.GetSkillLevel(skillType);
       if (level >= requiredLevel) return true;
    }
            }
              return false;
        }

    return false;
            }
catch (Exception ex)
        {
     Debug.LogWarning($"[QuestStatusService] CheckSingleRequirement error: {ex.Message}");
     return false;
          }
        }

        private static bool CheckCompletedQuest(string questIdOrName, long playerId, QuestManager questManager, PlayerVaultData playerData)
    {
      if (string.IsNullOrEmpty(questIdOrName)) return false;

     string questId = questIdOrName;
            if (questManager != null)
          {
                var resolvedId = questManager.FindQuestIdByName(questIdOrName);
    if (!string.IsNullOrEmpty(resolvedId))
    questId = resolvedId;
          }

 if (playerData?.CompletedQuests != null)
            {
          foreach (var key in playerData.CompletedQuests.Keys)
     {
         if (string.Equals(key, questId, StringComparison.OrdinalIgnoreCase) ||
       string.Equals(key, questIdOrName, StringComparison.OrdinalIgnoreCase))
        {
        return true;
      }
      }
            }

  if (questManager != null && questManager.HasCompletedQuest(playerId, questId))
   return true;

 return false;
   }

        #endregion

        #region Requirement Formatting

        /// <summary>
        /// Formats a requirement string for display in the UI.
   /// Converts internal formats like "HasItem,SwordIron,1" to "Item: Iron Sword x1"
        /// </summary>
        /// <param name="requirement">The raw requirement string</param>
        /// <returns>Human-readable requirement text</returns>
        public static string FormatRequirementForDisplay(string requirement)
        {
   if (string.IsNullOrEmpty(requirement)) return requirement;

            var req = requirement.Trim();
            var reqLower = req.ToLowerInvariant();

            try
      {
    if (reqLower.StartsWith("hascompletedquest"))
{
         var parts = req.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
  if (parts.Length >= 2)
    {
    var questId = parts[1].Trim();
      var questManager = QuestManager.Instance;
       if (questManager != null)
        {
             var questDef = questManager.GetQuestDefinition(questId);
           if (questDef != null && !string.IsNullOrEmpty(questDef.Name))
         return $"Quest: {questDef.Name}";
   }
   return $"Quest: {questId}";
       }
  }

if (reqLower.StartsWith("completed:") || reqLower.StartsWith("quest:"))
     {
          var questId = req.Substring(req.IndexOf(':') + 1).Trim();
      var questManager = QuestManager.Instance;
 if (questManager != null)
         {
              var questDef = questManager.GetQuestDefinition(questId);
             if (questDef != null && !string.IsNullOrEmpty(questDef.Name))
          return $"Quest: {questDef.Name}";
    }
                 return $"Quest: {questId}";
           }

          if (reqLower.StartsWith("hasitem"))
      {
           var parts = req.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
         if (parts.Length >= 2)
            {
       var prefab = parts[1].Trim();
      string displayName = PlayerInventoryService.GetItemDisplayName(prefab);
   int amount = 1;
       if (parts.Length >= 3) int.TryParse(parts[2].Trim(), out amount);
             return amount > 1 ? $"Item: {displayName} x{amount}" : $"Item: {displayName}";
   }
        }

      if (reqLower.StartsWith("item:"))
    {
    var itemPart = req.Substring(5).Trim();
            var parts = itemPart.Split(',');
                    var prefab = parts[0].Trim();
        string displayName = PlayerInventoryService.GetItemDisplayName(prefab);
        int amount = 1;
           if (parts.Length > 1) int.TryParse(parts[1].Trim(), out amount);
    return amount > 1 ? $"Item: {displayName} x{amount}" : $"Item: {displayName}";
     }

         if (reqLower.StartsWith("skill:"))
  {
       var skillPart = req.Substring(6).Trim();
           var parts = skillPart.Split(',');
         var skillName = parts[0].Trim();
        int level = 1;
         if (parts.Length > 1) int.TryParse(parts[1].Trim(), out level);
        return $"Skill: {skillName} Lv{level}";
         }
  }
            catch { }

      return requirement;
        }

        /// <summary>
        /// Formats a missing requirement reason for display.
        /// Converts internal formats to human-readable text.
        /// </summary>
    /// <param name="reason">The raw missing requirement reason</param>
    /// <returns>Human-readable missing requirement text</returns>
        public static string FormatMissingRequirementForDisplay(string reason)
        {
     if (string.IsNullOrEmpty(reason)) return reason;

          var reasonLower = reason.ToLowerInvariant();

            try
    {
    if (reasonLower.StartsWith("completed quest"))
                {
 int startQuote = reason.IndexOf('\'');
       int endQuote = reason.LastIndexOf('\'');
               if (startQuote >= 0 && endQuote > startQuote)
           {
      var questId = reason.Substring(startQuote + 1, endQuote - startQuote - 1);
       var questManager = QuestManager.Instance;
            if (questManager != null)
   {
          var questDef = questManager.GetQuestDefinition(questId);
            if (questDef != null && !string.IsNullOrEmpty(questDef.Name))
         return $"Quest: {questDef.Name}";
      }
       return $"Quest: {questId}";
          }
       }

    if (reasonLower.StartsWith("item "))
          {
               var itemPart = reason.Substring(5).Trim();
        // Try to extract prefab name and get display name
               string prefab = itemPart;
                    string countSuffix = "";
          int xIndex = itemPart.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
  if (xIndex > 0)
        {
    prefab = itemPart.Substring(0, xIndex).Trim();
              countSuffix = itemPart.Substring(xIndex).Trim();
         }
     string displayName = PlayerInventoryService.GetItemDisplayName(prefab);
           return string.IsNullOrEmpty(countSuffix) ? $"Item: {displayName}" : $"Item: {displayName} {countSuffix}";
    }

   if (reasonLower.Contains("quest"))
   {
     return $"Quest: {reason.Replace("quest", "").Replace("Quest", "").Trim()}";
    }
            }
catch { }

            return reason;
   }

    #endregion

        #region Quest Availability

        /// <summary>
    /// Gets comprehensive availability information for a quest.
        /// </summary>
        /// <param name="quest">The quest definition</param>
        /// <param name="playerId">Optional player ID (uses local player if 0)</param>
        /// <returns>Quest availability info including whether it's available and any missing requirements</returns>
 public static QuestAvailabilityInfo GetQuestAvailabilityInfo(QuestDefinition quest, long playerId = 0)
   {
            var info = new QuestAvailabilityInfo { IsAvailable = true, MissingRequirements = new List<string>() };

 if (quest == null)
            {
        info.IsAvailable = false;
                return info;
            }

            try
    {
           if (playerId <= 0)
          playerId = Player.m_localPlayer?.GetPlayerID() ?? 0;

         if (playerId <= 0)
      {
               info.IsAvailable = false;
        info.MissingRequirements.Add("Player not found");
       return info;
       }

          var questManager = QuestManager.Instance;
       if (questManager == null)
    {
            info.IsAvailable = false;
 info.MissingRequirements.Add("Quest system unavailable");
    return info;
     }

         if (questManager.HasQuest(playerId, quest.QuestId))
     {
            info.IsAvailable = false;
      info.MissingRequirements.Add("Quest already in progress");
      return info;
             }

       if (questManager.HasCompletedQuest(playerId, quest.QuestId))
                {
              string cooldownInfo = GetCooldownTimeRemaining(quest, playerId);
    if (!string.IsNullOrEmpty(cooldownInfo))
      {
          info.IsAvailable = false;
   info.CooldownInfo = cooldownInfo;
         info.MissingRequirements.Add($"On cooldown - {cooldownInfo}");
              return info;
 }
   }

      List<string> missing;
     if (!questManager.CheckRequirements(playerId, quest, out missing))
 {
      info.IsAvailable = false;
      if (missing != null && missing.Count > 0)
          {
                info.MissingRequirements.AddRange(missing);
          }
        else
  {
           info.MissingRequirements.Add("Requirements not met");
      }
  }
     }
         catch (Exception ex)
            {
      Debug.LogWarning($"[QuestStatusService] Error checking quest availability: {ex.Message}");
       }

            return info;
        }

#endregion
    }

    /// <summary>
    /// Data transfer object representing quest availability information.
    /// </summary>
    public class QuestAvailabilityInfo
    {
        /// <summary>Whether the quest is available to accept</summary>
        public bool IsAvailable { get; set; }

        /// <summary>List of missing requirements (human-readable)</summary>
   public List<string> MissingRequirements { get; set; }

        /// <summary>Cooldown info if on cooldown, null otherwise</summary>
        public string CooldownInfo { get; set; }
    }
}
