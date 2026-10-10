using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using FiresRPGmaker.Modules.Quest;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Represents a single line of dialogue with optional branching and quest triggers.
    /// </summary>
    public class DialogueLine
    {
        public string Id { get; set; }
   public string Text { get; set; }
        public string Response { get; set; }
        public string NextBranchId { get; set; }
        public string TriggerQuestId { get; set; }
        public string RequiredQuestState { get; set; } // e.g., "quest_id:completed" or "quest_id:active"
        public int Priority { get; set; }
    }

    /// <summary>
    /// Represents a dialogue profile containing multiple dialogue entries.
    /// </summary>
    public class DialogueProfile
    {
        public string Name { get; set; }
        public List<DialogueLine> Lines { get; set; } = new();
      public string DefaultGreeting { get; set; }
        public string DefaultFarewell { get; set; }
    }

    /// <summary>
    /// [DEPRECATED] Legacy dialogue manager using simple key=value .cfg format.
    /// 
    /// This class is superseded by <see cref="FiresRPGmaker.Modules.Dialogues.DialogueCodex"/>
    /// and <see cref="FiresRPGmaker.Modules.Dialogues.DialogueUI"/> which support the full
    /// Marketplace-compatible dialogue format with conditions, commands, branching, icons,
    /// and colors.
    /// 
    /// Kept for backward compatibility - NpcController falls through to this if
    /// DialogueCodex has no matching dialogue UID.
    /// New dialogues should be authored using the Marketplace .cfg format and placed in
    /// Config/FiresRPGmaker/Dialogues/ (or created via the in-game Dialogue Editor).
    /// </summary>
    [Obsolete("Use DialogueCodex + DialogueUI instead. This legacy manager will be removed in a future version.")]
    public static class DialogueManager
    {
        private static readonly Dictionary<string, DialogueProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized;

      /// <summary>
        /// Initializes the dialogue manager and loads all profiles from config.
        /// </summary>
public static void Initialize()
        {
          if (_initialized) return;
            _initialized = true;

            LoadAllProfiles();
    Debug.Log($"[DialogueManager] Loaded {_profiles.Count} dialogue profiles");
    }

        /// <summary>
        /// Reloads all dialogue profiles from config files.
        /// </summary>
        public static void Reload()
        {
      _profiles.Clear();
            LoadAllProfiles();
     Debug.Log($"[DialogueManager] Reloaded {_profiles.Count} dialogue profiles");
        }

        /// <summary>
    /// Gets a dialogue profile by name.
        /// </summary>
        public static DialogueProfile GetProfile(string profileName)
        {
     if (!_initialized) Initialize();

        if (string.IsNullOrEmpty(profileName))
           return null;

   _profiles.TryGetValue(profileName, out var profile);
        return profile;
        }

        /// <summary>
        /// Gets all dialogue lines for a profile, filtered by quest state if applicable.
     /// </summary>
        public static List<DialogueLine> GetDialogueLinesForProfile(string profileName)
        {
            var profile = GetProfile(profileName);
    if (profile == null)
                return new List<DialogueLine>();

  // Filter by quest requirements
         var filtered = profile.Lines
    .Where(line => CheckQuestRequirement(line.RequiredQuestState))
   .OrderByDescending(line => line.Priority)
   .ToList();

            return filtered;
   }

        /// <summary>
      /// Gets dialogue lines for a specific branch within a profile.
    /// </summary>
        public static List<DialogueLine> GetDialogueBranch(string profileName, string branchId)
        {
        var profile = GetProfile(profileName);
       if (profile == null)
                return new List<DialogueLine>();

            return profile.Lines
       .Where(line => string.Equals(line.Id, branchId, StringComparison.OrdinalIgnoreCase) ||
 line.Id?.StartsWith(branchId + "_", StringComparison.OrdinalIgnoreCase) == true)
      .Where(line => CheckQuestRequirement(line.RequiredQuestState))
             .OrderByDescending(line => line.Priority)
  .ToList();
        }

 /// <summary>
        /// Gets all available profile names.
        /// </summary>
    public static List<string> GetAllProfileNames()
        {
   if (!_initialized) Initialize();
            return _profiles.Keys.ToList();
        }

        #region Profile Loading

   private static void LoadAllProfiles()
        {
        string dialoguePath = Path.Combine(FiresCore.Storage.FiresConfigPaths.Dialogues);

            if (!Directory.Exists(dialoguePath))
  {
          try
{
    Directory.CreateDirectory(dialoguePath);
         CreateExampleProfile(dialoguePath);
     }
  catch (Exception ex)
      {
                Debug.LogWarning($"[DialogueManager] Failed to create dialogues folder: {ex.Message}");
                }
  return;
       }

var files = Directory.GetFiles(dialoguePath, "*.cfg");
            foreach (var file in files)
            {
      try
                {
       var profile = LoadProfile(file);
         if (profile != null && !string.IsNullOrEmpty(profile.Name))
       {
          _profiles[profile.Name] = profile;
      }
     }
     catch (Exception ex)
           {
   Debug.LogWarning($"[DialogueManager] Failed to load profile {file}: {ex.Message}");
 }
    }
        }

        private static DialogueProfile LoadProfile(string filePath)
        {
     var profile = new DialogueProfile
  {
          Name = Path.GetFileNameWithoutExtension(filePath)
            };

    var lines = File.ReadAllLines(filePath);
            DialogueLine currentLine = null;

         foreach (var rawLine in lines)
  {
                var line = rawLine.Trim();

  // Skip comments and empty lines
if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
           continue;

  // New dialogue entry: [EntryId]
   if (line.StartsWith("[") && line.EndsWith("]"))
  {
 if (currentLine != null)
               profile.Lines.Add(currentLine);

         currentLine = new DialogueLine
        {
   Id = line.Substring(1, line.Length - 2)
      };
continue;
  }

        // Key=Value pairs
      var eqIndex = line.IndexOf('=');
      if (eqIndex <= 0 || currentLine == null)
          continue;

      var key = line.Substring(0, eqIndex).Trim().ToLowerInvariant();
    var value = line.Substring(eqIndex + 1).Trim();

       switch (key)
      {
  case "text":
             currentLine.Text = value;
       break;
       case "response":
           currentLine.Response = value;
       break;
      case "nextbranch":
    case "next":
     currentLine.NextBranchId = value;
break;
                 case "triggerquest":
   case "quest":
 currentLine.TriggerQuestId = value;
  break;
 case "requires":
             case "requirement":
       currentLine.RequiredQuestState = value;
  break;
     case "priority":
       if (int.TryParse(value, out int priority))
    currentLine.Priority = priority;
      break;
     case "defaultgreeting":
             profile.DefaultGreeting = value;
   break;
           case "defaultfarewell":
 profile.DefaultFarewell = value;
      break;
}
            }

       // Add the last entry
      if (currentLine != null)
        profile.Lines.Add(currentLine);

       return profile;
        }

        private static void CreateExampleProfile(string folderPath)
        {
    string examplePath = Path.Combine(folderPath, "example_merchant.cfg");

            var content = @"# Example Dialogue Profile for FiresRPGmaker
# Each dialogue entry starts with [EntryId]
# Supported fields:
#   Text = The dialogue line shown to the player
#   Response = What the NPC says after player selects this option
#   NextBranch = ID of the next dialogue branch to show
#   TriggerQuest = Quest ID to start when this dialogue is selected
#   Requires = Quest requirement (e.g., ""quest_id:completed"" or ""quest_id:active"")
#   Priority = Higher priority entries appear first (default: 0)

DefaultGreeting = Welcome, traveler! What can I do for you today?
DefaultFarewell = Safe travels, friend!

[greeting_main]
Text = Hello there!
Response = Welcome to my shop. Feel free to browse.
Priority = 10

[ask_about_goods]
Text = What goods do you have?
Response = I have the finest wares from across the realm. Take a look!
Priority = 5

[ask_about_rumors]
Text = Heard any rumors lately?
Response = They say there's treasure in the old ruins to the east...
TriggerQuest = ruins_exploration
Priority = 3

[quest_complete_thanks]
Text = I found the treasure you mentioned!
Response = Excellent work! Here's your reward.
Requires = ruins_exploration:completed
Priority = 100

[farewell]
Text = Goodbye.
Response = Safe travels!
Priority = 0
";

  try
     {
  File.WriteAllText(examplePath, content);
   Debug.Log($"[DialogueManager] Created example profile at {examplePath}");
          }
    catch (Exception ex)
 {
    Debug.LogWarning($"[DialogueManager] Failed to create example profile: {ex.Message}");
            }
 }

        #endregion

        #region Quest State Checking

        private static bool CheckQuestRequirement(string requirement)
        {
       if (string.IsNullOrEmpty(requirement))
     return true;

            // Format: "quest_id:state" where state is "active", "completed", "not_started", etc.
var parts = requirement.Split(':');
if (parts.Length != 2)
 return true;

          string questId = parts[0].Trim();
        string requiredState = parts[1].Trim().ToLowerInvariant();

var questManager = QuestManager.Instance;
          if (questManager == null)
     return true;

   // Get player ID for quest state checks
            long playerId = Player.m_localPlayer?.GetPlayerID() ?? 0;
    if (playerId == 0)
          return true;

            bool isActive = questManager.HasQuest(playerId, questId);
            bool isCompleted = questManager.HasCompletedQuest(playerId, questId);

        switch (requiredState)
  {
    case "active":
         return isActive;
      case "completed":
 return isCompleted;
      case "not_started":
                case "notstarted":
          return !isActive && !isCompleted;
      case "in_progress":
       case "inprogress":
     return isActive && !isCompleted;
      default:
      return true;
            }
   }

        #endregion
    }
}
