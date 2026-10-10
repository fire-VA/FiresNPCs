using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using FiresRPGmaker;
using FiresRPGmaker.Modules.Quest;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Represents a quest profile that groups quests available from an NPC.
    /// </summary>
    public class QuestProfile
    {
        public string Name { get; set; }
        public List<string> QuestIds { get; set; } = new();
        public string Description { get; set; }
        public int MinLevel { get; set; }
        public string RequiredItem { get; set; }
        public string SourceFile { get; set; } // Track which file this profile came from
    }

    /// <summary>
    /// Manages quest profiles for NPCs.
    /// Quest profiles group quests that a specific NPC can offer.
    /// 
    /// On SERVER: Loads from all .cfg files in the QuestProfiles folder.
    /// On CLIENT: Only loads from data received from the server via RPC.
    /// 
    /// Format: [profile_name] followed by quest IDs on each line.
    /// </summary>
    public static class QuestProfileManager
    {
  private static readonly Dictionary<string, QuestProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
private static bool _initialized;

        private static string QuestProfilesFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.QuestProfiles);
      private static string DefaultProfilesFile => Path.Combine(QuestProfilesFolder, "QuestProfiles.cfg");

        /// <summary>
   /// Returns true if we are the server (dedicated server or host).
        /// </summary>
        private static bool IsServer => ZNet.instance?.IsServer() == true;

        public static void Initialize()
        {
     if (_initialized) return;
      _initialized = true;

 // Only load from local files if we are the server
            // Clients will receive profile data from server via RPC
                if (IsServer)
           {
             LoadAllProfiles();
             Debug.Log($"[QuestProfileManager] Server loaded {_profiles.Count} quest profiles from local files");
       }
            else
      {
                // Client: read the server-synced in-memory config store (with disk fallback) — the SAME path
                // QuestManager.LoadQuestDatabase uses for "Quests". The old "wait for an RPC push" left clients
                // with zero profiles when the push didn't land (bounty board "no quests available").
                LoadFromSyncedStore();
                Debug.Log($"[QuestProfileManager] Client loaded {_profiles.Count} quest profiles from synced store");
           }
        }

        // Loads profiles from the server-synced in-memory config store (falls back to client disk if the RAM
        // copy isn't populated yet). Mirrors QuestManager.LoadQuestDatabase's "Quests" read so profiles reach
        // clients the same reliable way the quest DB does.
        private static void LoadFromSyncedStore()
        {
            try
            {
                foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines("QuestProfiles"))
                {
                    try { ParseProfileLines(kv.Value, System.IO.Path.GetFileName(kv.Key) ?? string.Empty); }
                    catch { }
                }
            }
            catch (System.Exception ex) { Debug.LogWarning($"[QuestProfileManager] LoadFromSyncedStore failed: {ex.Message}"); }
        }

    public static void Reload()
        {
 _profiles.Clear();
 _initialized = true;

            // On clients, profile data comes from the server via ApplyProfileDataFromServer.
            // Do NOT re-read every cfg from disk on a client reload - that causes double-parse
            // spam and races with in-memory RPC data.
            if (!IsServer)
            {
                LoadFromSyncedStore();
                FiresLogger.LogVerbose($"[QuestProfileManager] Client reloaded {_profiles.Count} profiles from synced store");
                return;
            }

      LoadAllProfiles();
      Debug.Log($"[QuestProfileManager] Reloaded {_profiles.Count} quest profiles from local files");
        }

        /// <summary>
        /// Called when receiving profile data from the server.
        /// Parses the file content and updates profiles in memory.
    /// </summary>
public static void ApplyProfileDataFromServer(string fileName, string fileContent)
    {
            if (string.IsNullOrEmpty(fileContent))
         {
      Debug.LogWarning($"[QuestProfileManager] Received empty profile data for {fileName}");
        return;
    }

            FiresLogger.LogVerbose($"[QuestProfileManager] Applying profile data from server: {fileName} ({fileContent.Length} chars)");

            // Parse the content directly (don't write to disk on client)
var lines = fileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
         ParseProfileLines(lines, fileName);

  Debug.Log($"[QuestProfileManager] Applied in-memory profile file {fileName}; profiles={_profiles.Count}");
        }

   /// <summary>
      /// Parses profile lines from content (used by both server file loading and client RPC receiving).
        /// </summary>
        // Splits "id1, id2 ; id3" (or a single bare id) into individual quest IDs, de-duped. Returns count added.
        private static int AddQuestIds(QuestProfile profile, string raw)
        {
            if (profile == null || string.IsNullOrEmpty(raw)) return 0;
            int added = 0;
            foreach (var part in raw.Split(',', ';'))
            {
                var id = part.Trim();
                if (id.Length == 0) continue;
                if (!profile.QuestIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    profile.QuestIds.Add(id);
                    added++;
                }
            }
            return added;
        }

        /// <summary>
        /// Resolves the profile an NPC offers (strict kg parity — mirrors kg's <c>GET_NPC_Profile</c>, which
        /// returns "default" when the ZDO key is empty). Explicit profile if set (with any kg "=Flag" suffix
        /// stripped, e.g. "botanistbuy_mp=True" → "botanistbuy_mp"); otherwise "default". The migration seeds
        /// this at swap time, so this runtime pass mainly covers NPCs placed before the pipeline fix.
        /// </summary>
        public static string ResolveProfileName(string explicitProfile, string npcName)
        {
            if (string.IsNullOrEmpty(explicitProfile)) return "default";
            int eq = explicitProfile.IndexOf('=');
            return (eq > 0 ? explicitProfile.Substring(0, eq) : explicitProfile).Trim();
        }

        private static void ParseProfileLines(string[] lines, string sourceName)
        {
   QuestProfile currentProfile = null;
        int profilesLoaded = 0;
            int questsAdded = 0;

          foreach (var rawLine in lines)
    {
                var line = rawLine.Trim();

            // Skip empty lines and comments
     if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
         continue;

      // Check for section header [profile_name]
           if (line.StartsWith("[") && line.EndsWith("]"))
       {
  // Log quest count for previous profile
            if (currentProfile != null)
        {
        FiresLogger.LogVerbose($"[QuestProfileManager] Profile '{currentProfile.Name}' now has {currentProfile.QuestIds.Count} quest IDs");
          }

      string profileName = line.Substring(1, line.Length - 2).Trim();
         if (!string.IsNullOrEmpty(profileName))
           {
    // Check if profile already exists
         if (_profiles.TryGetValue(profileName, out var existingProfile))
                {
   // Use existing profile, merge quests
  FiresLogger.LogVerbose($"[QuestProfileManager] Profile '{profileName}' already exists, merging quests from {sourceName}");
      currentProfile = existingProfile;
   }
        else
         {
    currentProfile = new QuestProfile
  {
      Name = profileName,
   SourceFile = sourceName
           };
     _profiles[profileName] = currentProfile;
       profilesLoaded++;
 FiresLogger.LogVerbose($"[QuestProfileManager] Created new profile: '{profileName}'");
   }
         }
   continue;
      }

 // If we have a current profile, process the line
     if (currentProfile != null)
              {
        // Handle key=value format for optional metadata
     var eqIndex = line.IndexOf('=');
         if (eqIndex > 0)
    {
       var key = line.Substring(0, eqIndex).Trim().ToLowerInvariant();
       var value = line.Substring(eqIndex + 1).Trim();

     switch (key)
     {
               case "description":
     currentProfile.Description = value;
           break;
                 case "minlevel":
          case "min_level":
    if (int.TryParse(value, out int level))
       currentProfile.MinLevel = level;
    break;
      case "requireditem":
            case "required_item":
                currentProfile.RequiredItem = value;
 break;
            case "quests":
            case "quest":
                // kg format: "Quests = id1, id2, id3". Split into individual IDs.
                questsAdded += AddQuestIds(currentProfile, value);
 break;
      }
       }
     else
                 {
            // Plain quest ID(s): one per line, OR comma/semicolon-separated on one line
            // (kg's QuestProfiles.cfg uses "id1, id2, id3" with no "Quests=" prefix). Split either way.
                            questsAdded += AddQuestIds(currentProfile, line);
      }
      }
            }

     // Log final profile's quest count
         if (currentProfile != null)
       {
       FiresLogger.LogVerbose($"[QuestProfileManager] Profile '{currentProfile.Name}' final count: {currentProfile.QuestIds.Count} quest IDs");
      }

   if (profilesLoaded > 0 || questsAdded > 0)
      {
        FiresLogger.LogVerbose($"[QuestProfileManager] Parsed {profilesLoaded} profile(s), {questsAdded} quest(s) from {sourceName}");
              }
        }

        public static QuestProfile GetProfile(string profileName)
        {
            if (!_initialized) Initialize();
            // Client lazy-retry: the synced store may not have been populated when Initialize first ran.
            if (!IsServer && _profiles.Count == 0) LoadFromSyncedStore();

   if (string.IsNullOrEmpty(profileName))
            return null;

    _profiles.TryGetValue(profileName, out var profile);
    return profile;
        }

        public static List<QuestDefinition> GetQuestsForProfile(string profileName)
        {
         var profile = GetProfile(profileName);
            if (profile == null)
        return new List<QuestDefinition>();

            var questManager = QuestManager.Instance;
            if (questManager == null)
     return new List<QuestDefinition>();

            long playerId = Player.m_localPlayer?.GetPlayerID() ?? 0;

   var quests = new List<QuestDefinition>();
 foreach (var questId in profile.QuestIds)
     {
             var quest = questManager.GetQuestDefinition(questId);
           if (quest != null)
   {
    bool isCompleted = playerId > 0 && questManager.HasCompletedQuest(playerId, questId);
       if (!isCompleted)
         {
         quests.Add(quest);
          }
       }
       }

            return quests;
        }

      public static List<string> GetAllProfileNames()
        {
            if (!_initialized) Initialize();
            if (!IsServer && _profiles.Count == 0) LoadFromSyncedStore();
            return _profiles.Keys.ToList();
}

    public static bool MeetsRequirements(string profileName, Player player)
        {
  var profile = GetProfile(profileName);
            if (profile == null)
     return false;

            if (!string.IsNullOrEmpty(profile.RequiredItem) && player != null)
 {
                var inventory = player.GetInventory();
          if (inventory != null)
         {
         var item = inventory.GetItem(profile.RequiredItem);
           if (item == null)
      return false;
         }
            }

          return true;
   }

        /// <summary>
        /// Adds a quest to a profile and saves. Creates the profile if it doesn't exist.
        /// Only works on server - clients should not modify profiles directly.
        /// </summary>
     public static void AddQuestToProfile(string profileName, string questId)
        {
    if (!_initialized) Initialize();

            if (string.IsNullOrEmpty(profileName) || string.IsNullOrEmpty(questId))
         return;

  // Only server can modify profiles
          // (client edits are pushed to the server, which validates admin)

        // Get or create profile
          if (!_profiles.TryGetValue(profileName, out var profile))
     {
           profile = new QuestProfile
       {
             Name = profileName,
          SourceFile = DefaultProfilesFile // New profiles go to default file
        };
     _profiles[profileName] = profile;
  }

            // Add quest if not already present
if (!profile.QuestIds.Contains(questId, StringComparer.OrdinalIgnoreCase))
   {
       profile.QuestIds.Add(questId);
            }

            // Save profiles to their respective files
    SaveProfilesToFile(profile.SourceFile ?? DefaultProfilesFile);
        }

        /// <summary>Removes a quest from a profile (admin book right-click). Persists + pushes to the
        /// server through the same SaveProfilesToFile path as adds. Returns false if it wasn't there.</summary>
        public static bool RemoveQuestFromProfile(string profileName, string questId)
        {
            if (!_initialized) Initialize();
            if (string.IsNullOrEmpty(profileName) || string.IsNullOrEmpty(questId)) return false;
            if (!_profiles.TryGetValue(profileName, out var profile) || profile?.QuestIds == null) return false;
            int removed = profile.QuestIds.RemoveAll(q => string.Equals(q, questId, StringComparison.OrdinalIgnoreCase));
            if (removed <= 0) return false;
            SaveProfilesToFile(profile.SourceFile ?? DefaultProfilesFile);
            Debug.Log($"[QuestProfileManager] Removed '{questId}' from profile '{profileName}'");
            return true;
        }

  /// <summary>
        /// Ensures a profile exists. Creates it if it doesn't.
        /// Only works on server.
        /// </summary>
  public static void EnsureProfileExists(string profileName)
        {
 if (!_initialized) Initialize();

            if (string.IsNullOrEmpty(profileName))
     return;

         // Only server can create profiles
          // (client edits are pushed to the server, which validates admin)

            if (!_profiles.ContainsKey(profileName))
            {
_profiles[profileName] = new QuestProfile
            {
  Name = profileName,
     SourceFile = DefaultProfilesFile
       };
 SaveProfilesToFile(DefaultProfilesFile);
            }
        }

        /// <summary>
        /// Clears all profiles. Used before receiving fresh data from server.
        /// </summary>
        public static void ClearProfiles()
      {
    _profiles.Clear();
             FiresLogger.LogVerbose("[QuestProfileManager] Cleared all profiles");
        }

     #region Profile Loading (Server Only)

        private static void LoadAllProfiles()
        {
     _profiles.Clear();

      // Ensure directory exists
            if (!Directory.Exists(QuestProfilesFolder))
         {
     try
         {
   Directory.CreateDirectory(QuestProfilesFolder);
   CreateExampleFile();
         }
     catch (Exception ex)
 {
            Debug.LogWarning($"[QuestProfileManager] Failed to create directory: {ex.Message}");
                }
return;
   }

         // Load from all .cfg files in the folder
        var files = Directory.GetFiles(QuestProfilesFolder, "*.cfg");

     if (files.Length == 0)
            {
                CreateExampleFile();
       files = Directory.GetFiles(QuestProfilesFolder, "*.cfg");
            }

            foreach (var file in files)
 {
                try
   {
            LoadProfilesFromFile(file);
              }
   catch (Exception ex)
 {
           Debug.LogWarning($"[QuestProfileManager] Failed to load profiles from {Path.GetFileName(file)}: {ex.Message}");
      }
         }

   Debug.Log($"[QuestProfileManager] Loaded {_profiles.Count} profiles from {files.Length} file(s)");
        }

        private static void LoadProfilesFromFile(string filePath)
        {
            if (!File.Exists(filePath))
      return;

                     var lines = File.ReadAllLines(filePath);
                     FiresLogger.LogVerbose($"[QuestProfileManager] Loading file: {filePath} ({lines.Length} lines)");

            ParseProfileLines(lines, Path.GetFileName(filePath));
        }

        /// <summary>
        /// Saves all profiles that belong to a specific file.
 /// Server only operation.
        /// </summary>
     private static void SaveProfilesToFile(string filePath)
        {
            // client writes locally + pushes; the server validates admin in RPC_SyncFiles

        try
  {
           var sb = new System.Text.StringBuilder();
         sb.AppendLine("# Quest Profiles for FiresRPGmaker");
 sb.AppendLine("# Format: [profile_name] followed by quest IDs on each line");
 sb.AppendLine("# Optional metadata: description=, minlevel=, requireditem=");
           sb.AppendLine();

       // Get all profiles that belong to this file. Match by FILENAME, not full path — profiles parsed
       // from the server sync store carry a bare filename in SourceFile, while server-loaded ones carry
       // a full path; a strict path compare split them into disjoint sets and dropped blocks on save.
       string targetFile = Path.GetFileName(filePath);
        var profilesForFile = _profiles.Values
  .Where(p => string.Equals(Path.GetFileName(p.SourceFile ?? string.Empty), targetFile, StringComparison.OrdinalIgnoreCase))
         .OrderBy(p => p.Name);

          foreach (var profile in profilesForFile)
      {
sb.AppendLine($"[{profile.Name}]");

          // Write optional metadata
 if (!string.IsNullOrEmpty(profile.Description))
          sb.AppendLine($"description={profile.Description}");
          if (profile.MinLevel > 0)
              sb.AppendLine($"minlevel={profile.MinLevel}");
                  if (!string.IsNullOrEmpty(profile.RequiredItem))
        sb.AppendLine($"requireditem={profile.RequiredItem}");

      // Write quest IDs (just the ID, no prefix)
             foreach (var questId in profile.QuestIds)
          {
     sb.AppendLine(questId);
   }

            sb.AppendLine();
 }

 // Pure clients keep NO profile files on disk (they read the server sync store) — and their
 // SourceFile is a bare filename, so Path.GetDirectoryName("") threw "Path cannot be the empty
 // string" here and KILLED the save before the server push ever ran (the admin-book
 // "save quest to profile" failure). Client: push only. Server/host: write disk + reload.
 bool pureClient = ZNet.instance != null && !ZNet.instance.IsServer();
 if (!pureClient)
 {
     string fullPath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(QuestProfilesFolder, targetFile);
     string directory = Path.GetDirectoryName(fullPath);
     if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
         Directory.CreateDirectory(directory);
     File.WriteAllText(fullPath, sb.ToString());
 }
            global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("QuestProfiles", targetFile, sb.ToString());
       Debug.Log($"[QuestProfileManager] Saved profiles to {targetFile} ({(pureClient ? "pushed to server" : "disk + push")})");
            }
            catch (Exception ex)
            {
    Debug.LogError($"[QuestProfileManager] Failed to save profiles to {filePath}: {ex.Message}");
  }
        }

        private static void CreateExampleFile()
    {
  var content = @"# Quest Profiles for FiresRPGmaker
# Format: [profile_name] followed by quest IDs on each line
# Optional metadata: description=, minlevel=, requireditem=
#
# You can create multiple .cfg files in this folder.
# All files will be loaded and profiles merged.
#
# Example:
# [village_elder]
# description=Quests from the village elder
# minlevel=5
# kill_wolves
# gather_herbs
# deliver_message

[example_merchant]
description=Quests offered by the traveling merchant
merchant_delivery_1
merchant_delivery_2
merchant_rare_find

[example_hunter]
description=Hunting quests
minlevel=10
hunt_boars
hunt_deer
hunt_wolves
";

            try
         {
    if (!Directory.Exists(QuestProfilesFolder))
    Directory.CreateDirectory(QuestProfilesFolder);

     File.WriteAllText(DefaultProfilesFile, content);
            Debug.Log($"[QuestProfileManager] Created example profile file at {DefaultProfilesFile}");
    }
    catch (Exception ex)
{
    Debug.LogWarning($"[QuestProfileManager] Failed to create example file: {ex.Message}");
    }
  }

        #endregion
    }
}
