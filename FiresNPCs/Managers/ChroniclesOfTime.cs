using System;
using System.Collections.Generic;
using UnityEngine;
using FiresRPGmaker.Modules.Quest;

namespace FiresRPGmaker.Modules.Vault
{
    /// <summary>
    /// ChroniclesOfTime - Sister class to VaultOfKnowledge that handles all time-based quest calculations.
    /// Manages cooldowns, availability windows, and time conversions between Valheim in-game time and real time.
    /// 
    /// Valheim Time Conversion:
    /// - 1 in-game day = 30 real-world minutes
  /// - CooldownDays in quest configs refers to in-game days
    /// </summary>
    public static class ChroniclesOfTime
    {
   /// <summary>
        /// Real-world minutes per Valheim in-game day.
        /// </summary>
        public const double MinutesPerIngameDay = 30.0;

      /// <summary>
    /// Checks if a quest is currently on cooldown for a player.
     /// </summary>
    /// <param name="completedEntry">The player's completion record for this quest</param>
        /// <param name="cooldownDays">The cooldown in Valheim in-game days from the quest definition</param>
 /// <param name="availableAt">Output: When the quest will be available again (UTC)</param>
        /// <returns>True if the quest is still on cooldown, false if it can be started</returns>
        public static bool IsQuestOnCooldown(CompletedQuestEntry completedEntry, int cooldownDays, out DateTime availableAt)
        {
         availableAt = DateTime.MinValue;

            // No cooldown configured or no completion record
     if (completedEntry == null || cooldownDays <= 0)
    {
   return false;
            }

   // Calculate cooldown in real-world minutes
      double cooldownMinutes = cooldownDays * MinutesPerIngameDay;
            availableAt = completedEntry.LastCompletedAt.AddMinutes(cooldownMinutes);

      // Check if cooldown has expired
       return DateTime.UtcNow < availableAt;
        }

        /// <summary>
 /// Checks if a quest is on cooldown using player vault data and quest definition.
        /// </summary>
        /// <param name="playerId">The player's ID</param>
 /// <param name="questId">The quest ID to check</param>
  /// <param name="availableAt">Output: When the quest will be available again (UTC)</param>
        /// <returns>True if on cooldown, false if available</returns>
 public static bool IsQuestOnCooldown(long playerId, string questId, out DateTime availableAt)
      {
            availableAt = DateTime.MinValue;

         if (string.IsNullOrEmpty(questId)) return false;

         try
      {
      // Get quest definition for cooldown days
    var def = QuestManager.Instance?.GetQuestDefinition(questId);
           if (def == null || def.CooldownDays <= 0)
       {
  return false; // No cooldown defined
                }

     // Get player's vault data
         var vaultData = VaultOfKnowledge.Instance?.GetPlayerData(playerId);
    if (vaultData == null || vaultData.CompletedQuests == null)
         {
      return false; // No completion record
            }

    // Check if player has completed this quest
          if (!vaultData.CompletedQuests.TryGetValue(questId, out var completedEntry))
    {
         // Try case-insensitive lookup
      completedEntry = null;
          foreach (var kv in vaultData.CompletedQuests)
   {
      if (string.Equals(kv.Key, questId, StringComparison.OrdinalIgnoreCase))
          {
              completedEntry = kv.Value;
          break;
 }
        }

         if (completedEntry == null)
     {
       return false; // Never completed
        }
   }

    return IsQuestOnCooldown(completedEntry, def.CooldownDays, out availableAt);
          }
    catch (Exception ex)
            {
         Debug.LogWarning($"[FiresRPGmaker] ChroniclesOfTime.IsQuestOnCooldown error for player {playerId}, quest {questId}: {ex.Message}");
  return false;
 }
        }

        /// <summary>
        /// Gets a human-readable string describing when a quest will be available.
        /// </summary>
        /// <param name="availableAt">The UTC time when the quest becomes available</param>
        /// <returns>A formatted string like "in 15 minutes" or "at 3:45 PM"</returns>
        public static string GetAvailabilityMessage(DateTime availableAt)
        {
  if (availableAt == DateTime.MinValue)
            {
     return "now";
    }

            var remaining = availableAt - DateTime.UtcNow;

          if (remaining.TotalSeconds <= 0)
       {
           return "now";
    }

    if (remaining.TotalMinutes < 1)
            {
              return $"in {(int)remaining.TotalSeconds} seconds";
            }

            if (remaining.TotalMinutes < 60)
        {
        int mins = (int)remaining.TotalMinutes;
       return $"in {mins} minute{(mins != 1 ? "s" : "")}";
            }

   if (remaining.TotalHours < 24)
            {
             int hours = (int)remaining.TotalHours;
    int mins = (int)(remaining.TotalMinutes % 60);
           if (mins > 0)
          return $"in {hours} hour{(hours != 1 ? "s" : "")} {mins} minute{(mins != 1 ? "s" : "")}";
                return $"in {hours} hour{(hours != 1 ? "s" : "")}";
        }

   // More than a day - show local time
    var localTime = availableAt.ToLocalTime();
      return $"at {localTime:g}"; // Short date/time format
        }

        /// <summary>
        /// Builds a rejection message for a quest that is on cooldown.
        /// </summary>
  /// <param name="questId">The quest ID</param>
        /// <param name="questName">The display name of the quest (optional)</param>
        /// <param name="availableAt">When the quest becomes available</param>
        /// <returns>A user-friendly message</returns>
        public static string BuildCooldownMessage(string questId, string questName, DateTime availableAt)
        {
          string name = !string.IsNullOrEmpty(questName) ? questName : questId;
  string when = GetAvailabilityMessage(availableAt);
   return $"Quest '{name}' is on cooldown. Available again {when}.";
        }

    /// <summary>
     /// Converts Valheim in-game days to real-world TimeSpan.
        /// </summary>
  /// <param name="ingameDays">Number of Valheim in-game days</param>
        /// <returns>Equivalent real-world TimeSpan</returns>
        public static TimeSpan IngameDaysToRealTime(int ingameDays)
  {
            return TimeSpan.FromMinutes(ingameDays * MinutesPerIngameDay);
   }

      /// <summary>
        /// Converts real-world TimeSpan to approximate Valheim in-game days.
        /// </summary>
        /// <param name="realTime">Real-world duration</param>
   /// <returns>Approximate number of in-game days (can be fractional)</returns>
 public static double RealTimeToIngameDays(TimeSpan realTime)
        {
            return realTime.TotalMinutes / MinutesPerIngameDay;
        }

      /// <summary>
        /// Checks if a completed quest's cooldown has expired and can be removed from completed list
    /// to allow a fresh start (optional cleanup behavior).
        /// </summary>
        /// <param name="completedEntry">The completion entry</param>
    /// <param name="cooldownDays">Cooldown in Valheim days</param>
 /// <returns>True if cooldown has fully expired</returns>
        public static bool HasCooldownExpired(CompletedQuestEntry completedEntry, int cooldownDays)
      {
          if (completedEntry == null) return true;
  if (cooldownDays <= 0) return true; // No cooldown means always "expired"

            double cooldownMinutes = cooldownDays * MinutesPerIngameDay;
  var expiryTime = completedEntry.LastCompletedAt.AddMinutes(cooldownMinutes);
            return DateTime.UtcNow >= expiryTime;
    }
    }
}
