using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using BepInEx;
using Newtonsoft.Json;
using FiresRPGmaker.Modules.Vault;

namespace FiresRPGmaker.Modules.Quest
{
    public class QuestManager
    {
        public static QuestManager Instance { get; private set; }

        private Dictionary<string, QuestDefinition> questDatabase = new Dictionary<string, QuestDefinition>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<string>> questProfiles = new Dictionary<string, List<string>>();

        private string QuestsDir => Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "Quests");
        private string ProfilesDir => Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "QuestProfiles");

        private QuestManager() { }

        public static void Init()
        {
            if (Instance != null) return;
            Instance = new QuestManager();

            Directory.CreateDirectory(Instance.QuestsDir);
            Directory.CreateDirectory(Instance.ProfilesDir);

            // Create sample files if folders are empty
            Instance.CreateSampleQuestIfNeeded();
            Instance.CreateSampleProfileIfNeeded();

            Instance.LoadQuestDatabase();
            Instance.LoadQuestProfiles();

            FiresLogger.LogVerbose("[FiresRPGmaker] QuestManager initialized (hybrid ZDO + JSON)");

            // NOTE: FileUpdate and QuestDB RPCs are registered in VaultOfKnowledge.RegisterRPCs()
            // to avoid duplicate registration errors. Do not register them here.
        }

        // Creates a sample quest file if no .cfg files exist in Quests folder
        private void CreateSampleQuestIfNeeded()
        {
            // Prefer files named Quests*.cfg. If none found, create a sample 'Quests.cfg' containing multiple quests.
            if (Directory.GetFiles(QuestsDir, "Quests*.cfg").Length >0 || Directory.GetFiles(QuestsDir, "*.cfg").Length >0) return;

            string samplePath = Path.Combine(QuestsDir, "Quests.cfg");
            try
            {
                // Create a multi-quest sample file using bracketed IDs. IDs are used by code; Names are for display.
                string sampleContent = @"[KillBoarSample]
QuestType = Kill
Name = Hunt the Boar
Description = Slay4 boars to prove your strength to the village.
QuestTarget = Boar,4
QuestReward = Coins,50|Iron,10
CooldownDays =1
TimeLimitMinutes =0
Requirement = 
Autocomplete = true

[killDeer]
QuestType = Kill
Name = Hunt the Deer
Description = Hunt4 deer to prove yourself
QuestTarget = Deer,4
QuestReward = Coins,50|Iron,10
CooldownDays =0
TimeLimitMinutes =0
Requirement = HasCompletedQuest, KillBoarSample
Autocomplete = true
";

                File.WriteAllText(samplePath, sampleContent);
                FiresLogger.LogVerbose($"Created sample quest file: {samplePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] Failed to create sample quest file: {ex.Message}");
            }
        }

        // Creates a sample profile file if no .cfg files exist in QuestProfiles folder
        private void CreateSampleProfileIfNeeded()
        {
            if (Directory.GetFiles(ProfilesDir, "*.cfg").Length > 0) return;

            string samplePath = Path.Combine(ProfilesDir, "DefaultProfile.cfg");
            try
            {
                string sampleContent = @"
; Sample Quest Profile - DefaultProfile
; This profile groups quests that an NPC might offer

[DefaultProfile]
Quests = KillBoarSample
";

                File.WriteAllText(samplePath, sampleContent);
                FiresLogger.LogVerbose($"Created sample profile file: {samplePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] Failed to create sample profile file: {ex.Message}");
            }
        }

        private List<QuestTarget> ParseTargetsWithNames(string targetsValue, string namesValue, string legacyNamesValue = null, string questId = null)
        {
            if (!string.IsNullOrEmpty(legacyNamesValue))
            {
                FiresLogger.LogWarning($"[FiresRPGmaker] Quest '{questId}': Legacy Names/Name field found for target name filtering. Use M_Name or M_Names instead.");
            }
            var targets = new List<QuestTarget>();
            if (string.IsNullOrEmpty(targetsValue)) return targets;
            var targetParts = targetsValue.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
            var nameParts = string.IsNullOrEmpty(namesValue) ? new List<string>() : namesValue.Split(new[] { '|' }, StringSplitOptions.None).Select(n => n.Trim()).ToList();
            for (int i =0; i < targetParts.Count; i++)
            {
                var part = targetParts[i];
                List<string> pieces = null;
                if (part.Contains(",")) pieces = part.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                else if (part.Contains(":")) pieces = part.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                else pieces = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                string prefab = null;
                int amount =1;
                int minLevel =0;
                foreach (var tok in pieces)
                {
                    if (string.IsNullOrEmpty(tok)) continue;
                    if (prefab == null && !int.TryParse(tok, out _))
                    {
                        prefab = tok;
                        continue;
                    }
                    if (int.TryParse(tok, out int parsed))
                    {
                        if (amount ==1) amount = parsed;
                        else minLevel = parsed;
                        continue;
                    }
                }
                string nameFilter = (i < nameParts.Count) ? (string.IsNullOrWhiteSpace(nameParts[i]) ? null : nameParts[i]) : null;
                targets.Add(new QuestTarget
                {
                    Prefab = prefab,
                    Amount = amount,
                    MinLevel = minLevel,
                    NameFilter = nameFilter
                });
            }
            return targets;
        }

        public void LoadQuestDatabase()
        {
            questDatabase.Clear();
            var files = Directory.GetFiles(QuestsDir, "*.cfg");
            foreach (var file in files)
            {
                try
                {
                    var lines = File.ReadAllLines(file);
                    var fname = Path.GetFileName(file) ?? string.Empty;
                    bool forceKGPositional = false;
                    try
                    {
                        var ln = fname.ToLowerInvariant();
                        if (ln.Contains(".legacy") || ln.StartsWith("quests.legacy") || ln.Contains("kg")) forceKGPositional = true;
                    }
                    catch { forceKGPositional = false; }
                    for (int i =0; i < lines.Length; i++)
                    {
                        var raw = lines[i];
                        var trimmed = raw.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;

                        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                        {
                            var inner = trimmed.Substring(1, trimmed.Length -2);
                            bool autocompleteFlag = false;
                            var questId = inner;
                            if (inner.Contains("="))
                            {
                                var partsHeader = inner.Split(new[] { '=' },2);
                                questId = partsHeader[0].Trim();
                                var flag = partsHeader[1].Trim();
                                if (flag.Equals("autocomplete", StringComparison.OrdinalIgnoreCase)) autocompleteFlag = true;
                            }
                            var def = new QuestDefinition(questId);
                            def.Autocomplete = autocompleteFlag;
                            questDatabase[questId] = def;
                            var block = new List<string>();
                            int j = i +1;
                            while (j < lines.Length)
                            {
                                var t = lines[j].Trim();
                                if (string.IsNullOrEmpty(t) || t.StartsWith(";") || t.StartsWith("#")) { j++; continue; }
                                if (t.StartsWith("[") && t.EndsWith("]")) break;
                                block.Add(t);
                                j++;
                            }
                            i = j -1;

                            // Auto-detect positional format: if the first line of the block
                            // doesn't contain '=' it's likely KG marketplace positional format.
                            bool usePositional = forceKGPositional;
                            if (!usePositional && block.Count >= 4)
                            {
                                // Check if first few lines lack '=' (key=value). Positional format
                                // has bare lines like: Kill / Quest Name / Description / Boar, 10
                                bool hasKeyValue = false;
                                for (int k = 0; k < Math.Min(3, block.Count); k++)
                                {
                                    if (block[k].Contains("="))
                                    {
                                        hasKeyValue = true;
                                        break;
                                    }
                                }
                                if (!hasKeyValue) usePositional = true;
                            }

                            if (usePositional && block.Count >0)
                            {
                                // KG marketplace positional format
                                int pos =0;
                                if (pos < block.Count) def.QuestType = block[pos++].Trim();
                                if (pos < block.Count) def.Name = block[pos++].Trim();
                                // Description: all lines until we find a line that looks like targets (contains ',' and a digit)
                                int descStart = pos;
                                int targetsIdx = -1;
                                for (int k = pos; k < block.Count; k++)
                                {
                                    if (LooksLikeTargetLine(block[k]))
                                    {
                                        targetsIdx = k;
                                        break;
                                    }
                                }
                                if (targetsIdx == -1)
                                {
                                    // fallback: treat next line as description, next as targets if present
                                    if (pos < block.Count) { def.Description = block[pos++].Trim(); }
                                    if (pos < block.Count)
                                    {
                                        try { def.QuestTargets = ParseTargets(block[pos++]); } catch { def.QuestTargets = new List<QuestTarget>(); }
                                    }
                                }
                                else
                                {
                                    // description is all lines between pos and targetsIdx
                                    if (targetsIdx > pos)
                                    {
                                        def.Description = string.Join("\n", block.Skip(pos).Take(targetsIdx - pos));
                                    }
                                    else
                                    {
                                        def.Description = string.Empty;
                                    }
                                    // parse targets
                                    def.QuestTargets = ParseTargets(block[targetsIdx]);
                                    pos = targetsIdx +1;
                                }
                                // rewards
                                if (pos < block.Count)
                                {
                                    def.Rewards = ParseRewards(block[pos++]);
                                }
                                // cooldown/time limit line: "10,1800"
                                if (pos < block.Count)
                                {
                                    var cooldownLine = block[pos++];
                                    var parts = cooldownLine.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                                    if (parts.Length >0 && int.TryParse(parts[0], out int cd)) def.CooldownDays = cd;
                                    if (parts.Length >1 && int.TryParse(parts[1], out int tl)) def.TimeLimitMinutes = tl;
                                }
                                // requirements
                                if (pos < block.Count)
                                {
                                    var req = block[pos++].Trim();
                                    if (!string.IsNullOrEmpty(req) && !req.Equals("None", StringComparison.OrdinalIgnoreCase))
                                    {
                                        def.Requirements = req.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                                    }
                                }
                                // For legacy KG positional files, default to autocomplete if not set
                                try
                                {
                                    if (usePositional && !def.Autocomplete)
                                    {
                                        def.Autocomplete = true;
                                    }
                                }
                                catch { }
                            }
                            else
                            {
                                string targetsValue = null;
                                string mNamesValue = null;
                                string questNameValue = null;
                                string legacyNamesValue = null;
                                foreach (var line in block)
                                {
                                    string key = null;
                                    string value = null;
                                    if (line.Contains("="))
                                    {
                                        var parts = line.Split(new[] { '=' },2);
                                        if (parts.Length !=2) continue;
                                        key = parts[0].Trim();
                                        value = parts[1].Trim();
                                    }
                                    else if (line.Contains(":"))
                                    {
                                        var parts = line.Split(new[] { ':' },2);
                                        if (parts.Length !=2) continue;
                                        key = parts[0].Trim();
                                        value = parts[1].Trim();
                                    }
                                    if (string.IsNullOrEmpty(key)) continue;
                                    var norm = key.Replace(" ", string.Empty).Replace("Quest", string.Empty).Trim().ToLowerInvariant();
                                    if (!usePositional)
                                    {
                                        switch (norm)
                                        {
                                            case "target":
                                            case "questtarget":
                                                targetsValue = value;
                                                break;
                                            case "m_names":
                                            case "m_name":
                                                mNamesValue = value;
                                                break;
                                            case "questname":
                                                questNameValue = value;
                                                break;
                                            // Ignore Name/Names in non-legacy configs
                                        }
                                    }
                                    else
                                    {
                                        // legacy parsing (unchanged)
                                        switch (norm)
                                        {
                                            case "target":
                                            case "questtarget":
                                                targetsValue = value;
                                                break;
                                            case "names":
                                            case "name":
                                                legacyNamesValue = value;
                                                break;
                                        }
                                    }
                                }
                                if (targetsValue != null)
                                {
                                    if (!usePositional)
                                        def.QuestTargets = ParseTargetsWithNames(targetsValue, mNamesValue, null, questId);
                                    else
                                        def.QuestTargets = ParseTargetsWithNames(targetsValue, legacyNamesValue, null, questId);
                                }
                                foreach (var line in block)
                                {
                                    string key = null;
                                    string value = null;
                                    if (line.Contains("="))
                                    {
                                        var parts = line.Split(new[] { '=' },2);
                                        if (parts.Length !=2) continue;
                                        key = parts[0].Trim();
                                        value = parts[1].Trim();
                                    }
                                    else if (line.Contains(":"))
                                    {
                                        var parts = line.Split(new[] { ':' },2);
                                        if (parts.Length !=2) continue;
                                        key = parts[0].Trim();
                                        value = parts[1].Trim();
                                    }
                                    if (string.IsNullOrEmpty(key)) continue;
                                    var norm = key.Replace(" ", string.Empty).Replace("Quest", string.Empty).Replace("Quest", string.Empty).Trim();
                                    norm = norm.ToLowerInvariant();
                                    switch (norm)
                                    {
                                        case "type":
                                        case "questtype":
                                            def.QuestType = value;
                                            break;
                                        case "name":
                                        case "questname":
                                            def.Name = value;
                                            break;
                                        case "description":
                                            def.Description = value;
                                            break;
                                        case "reward":
                                        case "questreward":
                                            def.Rewards = ParseRewards(value);
                                            break;
                                        case "cooldowndays":
                                            if (int.TryParse(value, out int cd)) def.CooldownDays = cd; else def.CooldownDays =0;
                                            break;
                                        case "timelimitminutes":
                                            if (int.TryParse(value, out int tl)) def.TimeLimitMinutes = tl; else def.TimeLimitMinutes =0;
                                            break;
                                        case "requirement":
                                        case "requirements":
                                        case "questrequirements":
                                            def.Requirements = value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                                            break;
                                        case "autocomplete":
                                            if (bool.TryParse(value, out bool ac)) def.Autocomplete = ac;
                                            else if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) def.Autocomplete = true;
                                            break;
                                        case "previewimage":
                                            def.PreviewImage = value;
                                            break;
                                        case "onaccept":
                                        case "onacceptquest":
                                            def.LifecycleEvents["OnAcceptQuest"] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                        case "oncomplete":
                                        case "oncompletequest":
                                            def.LifecycleEvents["OnCompleteQuest"] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                    }
                                }
                            }
                            // Per-quest target logging removed — only failures log (inside ParseTargets)
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FiresRPGmaker] Failed to load quest file {file}: {ex.Message}");
                }
            }
            FiresLogger.LogVerbose($"Loaded {questDatabase.Count} quests");
        }

        /// <summary>
        /// Validates all loaded quest definitions against ZNetScene/ObjectDB.
        /// Checks that reward items exist as prefabs and target mobs exist.
        /// Marks unavailable rewards and logs warnings for missing prefabs.
        /// Should be called after ZNetScene is fully initialized (e.g., after world load).
        /// </summary>
        public void ValidateQuestDatabase()
        {
            if (questDatabase.Count == 0) return;

            var zns = ZNetScene.instance;
            var odb = ObjectDB.instance;
            int issueCount = 0;

            foreach (var kvp in questDatabase)
            {
                var def = kvp.Value;

                // Validate rewards
                if (def.Rewards != null)
                {
                    foreach (var reward in def.Rewards)
                    {
                        if (string.IsNullOrEmpty(reward.Target)) continue;

                        if (reward.IsSkillExp)
                        {
                            // Skill exp rewards - validate skill type exists
                            bool validSkill = false;
                            try
                            {
                                foreach (Skills.SkillType st in System.Enum.GetValues(typeof(Skills.SkillType)))
                                {
                                    if (st.ToString().Equals(reward.Target, StringComparison.OrdinalIgnoreCase))
                                    {
                                        validSkill = true;
                                        break;
                                    }
                                }
                            }
                            catch { }
                            reward.IsAvailable = validSkill;
                            if (!validSkill)
                            {
                                Debug.LogWarning($"[QuestManager] Quest '{def.QuestId}' has invalid skill reward: Skill_Exp '{reward.Target}' is not a valid skill type");
                                issueCount++;
                            }
                        }
                        else
                        {
                            // Item rewards - validate prefab exists in ZNetScene.
                            // Both lookups are case-sensitive in Valheim, so also try a
                            // case-insensitive scan to catch common typos like 'coins' vs 'Coins'.
                            bool exists = false;
                            if (zns != null)
                                exists = zns.GetPrefab(reward.Target) != null;

                            if (!exists && odb != null)
                                exists = odb.GetItemPrefab(reward.Target) != null;

                            // Case-insensitive fallback — scan ZNetScene prefab list.
                            if (!exists && zns != null)
                            {
                                foreach (var prefab in zns.m_prefabs)
                                {
                                    if (prefab != null && string.Equals(prefab.name, reward.Target,
                                            System.StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Canonicalize so the award code uses the real prefab name.
                                        reward.Target = prefab.name;
                                        exists = true;
                                        break;
                                    }
                                }
                            }

                            reward.IsAvailable = exists;
                            if (!exists)
                            {
                                Debug.LogWarning($"[QuestManager] Quest '{def.QuestId}' has unavailable item reward: '{reward.Target}' not found in ZNetScene/ObjectDB (mod removed?)");
                                issueCount++;
                            }
                        }
                    }
                }

                // Validate targets
                if (def.QuestTargets != null)
                {
                    foreach (var target in def.QuestTargets)
                    {
                        if (string.IsNullOrEmpty(target.Prefab)) continue;
                        bool exists = false;
                        if (zns != null)
                            exists = zns.GetPrefab(target.Prefab) != null;
                        if (!exists)
                        {
                            Debug.LogWarning($"[QuestManager] Quest '{def.QuestId}' has unavailable target: '{target.Prefab}' not found in ZNetScene (mod removed?)");
                            issueCount++;
                        }
                    }
                }
            }

            if (issueCount > 0)
                Debug.LogWarning($"[QuestManager] Quest validation complete: {issueCount} issue(s) found across {questDatabase.Count} quests");
            else
                Debug.Log($"[QuestManager] Quest validation complete: all {questDatabase.Count} quests OK");
        }

        public void LoadQuestProfiles()
        {
            questProfiles.Clear();

            // On clients, profile data is delivered by the server via RPC (HandleFileUpdate /
            // ApplyFileContent) and by QuestProfileManager.ApplyProfileDataFromServer. Reading
            // from local disk here causes double-parse spam on login and can race with the
            // server-authoritative state. Servers still load from disk as the source of truth.
            bool isServer = (ZNet.instance != null && ZNet.instance.IsServer());
            if (!isServer)
            {
                FiresLogger.LogVerbose("[FiresRPGmaker] QuestManager.LoadQuestProfiles skipped on client (data comes from server RPC)");
                return;
            }

            foreach (var file in Directory.GetFiles(ProfilesDir, "*.cfg"))
            {
                try
                {
                    var lines = File.ReadAllLines(file);
                    string currentProfile = null;

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;

                        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                        {
                            currentProfile = trimmed.Substring(1, trimmed.Length - 2);
                            questProfiles[currentProfile] = new List<string>();
                            continue;
                        }

                        if (currentProfile == null) continue;

                        var parts = trimmed.Split(new[] { '=' }, 2);
                        if (parts.Length != 2) continue;

                        var key = parts[0].Trim();
                        var value = parts[1].Trim();

                        if (key == "Quests")
                        {
                            List<string> profileQuests = new List<string>();
                            profileQuests.AddRange(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                        .Select(s => s.Trim()));
                            questProfiles[currentProfile] = profileQuests;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FiresRPGmaker] Failed to load profile file {file}: {ex.Message}");
                }
            }
            FiresLogger.LogVerbose($"Loaded {questProfiles.Count} quest profiles");
        }

        // Return list of loaded quests as (id, name)
        public List<(string QuestId, string Name)> GetAllQuests()
        {
            return questDatabase.Values.Select(d => (d.QuestId, d.Name ?? d.QuestId)).ToList();
        }

        // Return a list of quest names that contain the given term (case-insensitive)
        public List<string> SearchQuestNames(string term)
        {
            if (string.IsNullOrEmpty(term)) return questDatabase.Values.Select(v => v.Name ?? v.QuestId).ToList();
            string normTerm = NormalizeForCompare(term);
            var results = new List<string>();
            foreach (var d in questDatabase.Values)
            {
                var name = d.Name ?? d.QuestId;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >=0) results.Add(name);
                else if (!string.IsNullOrEmpty(normTerm) && NormalizeForCompare(name).Contains(normTerm)) results.Add(name);
            }
            return results;
        }

        // Find a quest ID by display name. First tries exact case-insensitive match on def.Name,
        // then falls back to the first partial (contains) match. Returns null if not found.
        public string FindQuestIdByName(string questName)
        {
            if (string.IsNullOrEmpty(questName)) return null;
            // If the caller provided a questId directly (case-insensitive), return it
            var keyMatch = questDatabase.Keys.FirstOrDefault(k => string.Equals(k, questName, StringComparison.OrdinalIgnoreCase));
            if (keyMatch != null) return keyMatch;
            // exact match first
            var exact = questDatabase.Values.FirstOrDefault(d => string.Equals(d.Name, questName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact.QuestId;

            // Normalize search term (remove non-alphanumeric) to allow matching 'killdeer' -> 'Kill Deer'
            string normTerm = NormalizeForCompare(questName);
            if (!string.IsNullOrEmpty(normTerm))
            {
                // exact normalized match
                var normExact = questDatabase.Values.FirstOrDefault(d => NormalizeForCompare(d.Name) == normTerm);
                if (normExact != null) return normExact.QuestId;

                // normalized contains
                var normPartial = questDatabase.Values.FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) && NormalizeForCompare(d.Name).Contains(normTerm));
                if (normPartial != null) return normPartial.QuestId;
            }

            return null;
        }

        // Global quest definition lookup
        public QuestDefinition GetQuestDefinition(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            if (questDatabase.TryGetValue(questId, out var def)) return def;
            // fallback: case-insensitive lookup
            var key = questDatabase.Keys.FirstOrDefault(k => string.Equals(k, questId, StringComparison.OrdinalIgnoreCase));
            if (key != null && questDatabase.TryGetValue(key, out def)) return def;
            return null;
        }

        /// <summary>
        /// Stable namespace key used by <see cref="ManifestDiffSync"/>
        /// so the server can skip re-sending the entire QuestDB when the client already
        /// holds the same build on disk.
        /// </summary>
        public const string QuestDbManifestNamespace = "QuestDatabase";

        /// <summary>
        /// Serialise the current in-memory quest database exactly once with
        /// <see cref="JsonConvert.SerializeObject"/> so both the broadcast and the hash
        /// see byte-identical content. Keyed order is whatever the underlying dictionary
        /// iteration yields; that's fine because server + client both round-trip the same
        /// serialised string, so the hashes still match even if order drifts.
        /// </summary>
        private string SerialiseQuestDb()
        {
            var dict = questDatabase.ToDictionary(kv => kv.Key, kv => kv.Value);
            return JsonConvert.SerializeObject(dict, Formatting.None) ?? string.Empty;
        }

        // Send the full in-memory quest database as JSON to a peer. Used to ensure clients have authoritative DB.
        // Diff-sync: if the peer already has a cached SHA-256 of this exact payload
        // (persisted by HandleQuestDB on its last receive) the send is skipped entirely.
        public void BroadcastQuestDatabaseToPeer(long peerUid)
        {
            try
            {
                if (FiresRPGmaker.Instance != null)
                {
                    FiresRPGmaker.Instance.StartCoroutine(BroadcastQuestDbCoroutine(peerUid));
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Could not start QuestDB diff-sync coroutine, falling back: {ex.Message}");
            }

            // Fallback: blind send. Correct, just skips the "is it actually needed?" check.
            try
            {
                string json = SerialiseQuestDb();
                var pkg = new ZPackage();
                pkg.Write(json);
                VaultOfKnowledge.SafeInvokeRoutedRPC(peerUid, "FiresRPGmaker_QuestDB", pkg);
                FiresLogger.LogVerbose($"Broadcasted QuestDB to peer {peerUid} (blind fallback, entries={questDatabase.Count})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to broadcast QuestDB to {peerUid}: {ex.Message}");
            }
        }

        private System.Collections.IEnumerator BroadcastQuestDbCoroutine(long peerUid)
        {
            string json;
            string localHash;
            try
            {
                json = SerialiseQuestDb();
                localHash = ManifestDiffSync.ComputeSha256(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] SerialiseQuestDb failed for peer {peerUid}: {ex.Message}");
                yield break;
            }

            ManifestDiffSync.RequestFromPeer(peerUid, QuestDbManifestNamespace);
            yield return ManifestDiffSync.WaitForManifest(peerUid, QuestDbManifestNamespace, 1.5f);

            if (ManifestDiffSync.PeerHasBlob(peerUid, QuestDbManifestNamespace, localHash))
            {
                FiresLogger.LogVerbose($"QuestDB diff-sync: peer {peerUid} already has current DB (hash match), skipping send.");
                yield break;
            }

            ZPackage pkg = null;
            try
            {
                pkg = new ZPackage();
                pkg.Write(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] QuestDB pkg build failed for peer {peerUid}: {ex.Message}");
                yield break;
            }

            VaultOfKnowledge.SafeInvokeRoutedRPC(peerUid, "FiresRPGmaker_QuestDB", pkg);
            FiresLogger.LogVerbose($"Broadcasted QuestDB to peer {peerUid} (entries={questDatabase.Count}, {json.Length} chars)");
        }

        // RPC handler: receive full quest DB JSON from server and apply locally
        public static void HandleQuestDB(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg == null) return;
                string json = string.Empty;
                try { json = pkg.ReadString(); } catch { json = string.Empty; }
                if (string.IsNullOrEmpty(json)) return;

                // Ensure instance exists
                if (Instance == null) Init();

                try
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, QuestDefinition>>(json);
                    if (dict != null)
                    {
                        Instance.questDatabase = dict.ToDictionary(kv => kv.Key, kv => kv.Value);
                        FiresLogger.LogVerbose($"Received QuestDB from {sender}; loaded {Instance.questDatabase.Count} quests");

                        // Persist the hash of what we just received so future manifest queries
                        // let the server skip re-sending identical content. Idempotent � writes
                        // only 64 hex chars to disk.
                        try
                        {
                            string hash = ManifestDiffSync.ComputeSha256(json);
                            ManifestDiffSync.RememberBlobHash(QuestDbManifestNamespace, hash);
                        }
                        catch { }

                        // Validate rewards/targets against ZNetScene after receiving full DB
                        if (ZNetScene.instance != null)
                            Instance.ValidateQuestDatabase();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to deserialize QuestDB from {sender}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] HandleQuestDB failed: {ex.Message}");
            }
        }

        public List<string> GetQuestsForProfile(string profileName)
        {
            List<string> quests;
            if (questProfiles.TryGetValue(profileName, out quests))
                return quests;
            return new List<string>();
        }

        // --- File watcher & sync support (server-side) ---
        private FileSystemWatcher questsWatcher;
        private FileSystemWatcher profilesWatcher;
        private readonly Dictionary<string, DateTime> lastEvent = new();

        private void StartFileWatchers()
        {
            try
            {
                Directory.CreateDirectory(QuestsDir);
                Directory.CreateDirectory(ProfilesDir);

                questsWatcher = new FileSystemWatcher(QuestsDir, "*.cfg") { IncludeSubdirectories = false, EnableRaisingEvents = true };
                profilesWatcher = new FileSystemWatcher(ProfilesDir, "*.cfg") { IncludeSubdirectories = false, EnableRaisingEvents = true };

                FileSystemEventHandler onChange = (s, e) => OnFileChanged("Quests", e.FullPath);
                RenamedEventHandler onRenamed = (s, e) => OnFileChanged("Quests", e.FullPath);

                questsWatcher.Changed += onChange;
                questsWatcher.Created += onChange;
                questsWatcher.Deleted += onChange;
                questsWatcher.Renamed += onRenamed;

                FileSystemEventHandler onChangeP = (s, e) => OnFileChanged("QuestProfiles", e.FullPath);
                RenamedEventHandler onRenamedP = (s, e) => OnFileChanged("QuestProfiles", e.FullPath);

                profilesWatcher.Changed += onChangeP;
                profilesWatcher.Created += onChangeP;
                profilesWatcher.Deleted += onChangeP;
                profilesWatcher.Renamed += onRenamedP;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to start quest file watchers: {ex.Message}");
            }
        }

        private void OnFileChanged(string folderName, string fullPath)
        {
            try
            {
                // debounce multiple events
                // debounce multiple events
                string key = fullPath.ToLowerInvariant();
                var now = DateTime.UtcNow;
                lock (lastEvent)
                {
                    if (lastEvent.TryGetValue(key, out var last) && (now - last).TotalMilliseconds <500)
                        return;
                    lastEvent[key] = now;
                }

                string fileName = Path.GetFileName(fullPath);
                string content = string.Empty;
                if (File.Exists(fullPath))
                {
                    try { content = File.ReadAllText(fullPath); } catch { content = string.Empty; }
                }

                // Send to all clients
                try
                {
                    var pkg = new ZPackage();
                    pkg.Write(folderName);
                    pkg.Write(fileName);
                    pkg.Write(content ?? string.Empty);
                    // broadcast
                    VaultOfKnowledge.SafeInvokeRoutedRPC(0L, "FiresRPGmaker_FileUpdate", pkg);
                    FiresLogger.LogVerbose($"[FiresRPGmaker] Pushed file update {folderName}/{fileName} to clients");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to push file update to clients: {ex.Message}");
                }
            }
            catch { }
        }

        // Send all quest-related files to a single peer (used on join).
        //
        // Diff-sync: reuses the same "Cfg:Quests" / "Cfg:QuestProfiles" manifest
        // namespaces that ServerConfigFileWatcher registers at init, so quest files
        // whose client-side SHA-256 matches the server's on-disk copy get skipped.
        // This path used to re-ship every .cfg on every RequestPlayerData round-trip
        // regardless of whether anything had changed � a double-whammy with
        // SendAllConfigsToClientCoroutine's own traversal of the same folders.
        //
        // If the client hasn't replied to a manifest request yet (first call of the
        // session before the main config coroutine ran), the cache look-up simply
        // returns false and we fall through to the legacy "send everything" path for
        // correctness. Once the manifest arrives the next caller benefits.
        public void SendAllQuestFilesToPeer(long peerUid)
        {
            try
            {
                // Fire parallel manifest requests up front � safe to call even if the
                // main config coroutine has already cached them; the request clears
                // and re-fetches, which is still fast.
                ManifestDiffSync.RequestFromPeer(peerUid, "Cfg:Quests");
                ManifestDiffSync.RequestFromPeer(peerUid, "Cfg:QuestProfiles");

                SendFolderDiffed(peerUid, QuestsDir, "Quests", "Cfg:Quests");
                SendFolderDiffed(peerUid, ProfilesDir, "QuestProfiles", "Cfg:QuestProfiles");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] SendAllQuestFilesToPeer failed: {ex.Message}");
            }
        }

        private void SendFolderDiffed(long peerUid, string folderPath, string folderName, string manifestNamespace)
        {
            if (!Directory.Exists(folderPath)) return;

            // Best-effort short wait so a manifest request fired moments ago has a
            // chance to arrive. We can't yield here (non-coroutine caller), so the
            // poll runs on the calling thread � keep it tight (250 ms max).
            bool haveManifest = false;
            for (int i = 0; i < 5; i++)
            {
                if (ManifestDiffSync.TryGetManifest(peerUid, manifestNamespace, out _))
                {
                    haveManifest = true;
                    break;
                }
                System.Threading.Thread.Sleep(50);
            }

            int sent = 0, skipped = 0;
            foreach (var f in Directory.GetFiles(folderPath, "*.cfg"))
            {
                string fileName = Path.GetFileName(f);
                string content;
                try { content = File.ReadAllText(f); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to read {folderName}/{fileName}: {ex.Message}");
                    continue;
                }

                if (haveManifest)
                {
                    string relPath = fileName; // top-level .cfg � relative path = filename
                    string localHash = ManifestDiffSync.ComputeSha256(content);
                    if (ManifestDiffSync.HasSameHash(peerUid, manifestNamespace, relPath, localHash))
                    {
                        skipped++;
                        continue;
                    }
                }

                SendFileToPeer(peerUid, folderName, fileName, content);
                sent++;
            }

            if (haveManifest && (sent > 0 || skipped > 0))
                Debug.Log($"[FiresRPGmaker] Quest folder '{folderName}' diff-sync to peer {peerUid}: sent {sent}, skipped {skipped} unchanged.");
        }

        private void SendFileToPeer(long peerUid, string folderName, string fileName, string content)
        {
            try
            {
                var pkg = new ZPackage();
                pkg.Write(folderName);
                pkg.Write(fileName);
                pkg.Write(content ?? string.Empty);
                VaultOfKnowledge.SafeInvokeRoutedRPC(peerUid, "FiresRPGmaker_FileUpdate", pkg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to send file {folderName}/{fileName} to peer {peerUid}: {ex.Message}");
            }
        }

        /// <summary>
        /// Determines if a line looks like a quest targets specification rather than prose description.
        /// Targets lines look like: "Boar, 10" or "Boar, 10 | Neck, 5, 1"
        /// Description lines are natural prose: "The Meadows pulse with wild boars, their tusks..."
        /// </summary>
        private static bool LooksLikeTargetLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            // Targets lines are typically short (mob name + count)
            // Long lines (>80 chars) are almost certainly descriptions
            if (line.Length > 80) return false;
            // Must have a comma
            if (!line.Contains(",")) return false;
            // Split by | for multi-target lines, then check each segment
            var segments = line.Split('|');
            foreach (var seg in segments)
            {
                var s = seg.Trim();
                if (string.IsNullOrEmpty(s)) continue;
                // Each segment should be like "Boar, 10" or "Boar, 10, 2"
                // Split by comma - last part(s) should be numeric
                var parts = s.Split(',');
                if (parts.Length < 2) return false;
                // The second part (after the mob name) should be a number
                if (!parts[1].Trim().All(c => char.IsDigit(c) || c == ' ')) return false;
            }
            return true;
        }

        private List<QuestTarget> ParseTargets(string value)
        {
            var targets = new List<QuestTarget>();
            if (string.IsNullOrEmpty(value)) return targets;
            // split by | for multiple targets
            foreach (var part in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;

                // Accept separators ',' or ':' or spaces
                List<string> pieces = null;
                if (p.Contains(",")) pieces = p.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                else if (p.Contains(":")) pieces = p.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                else pieces = p.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();

                if (pieces.Count ==0) continue;

                string prefab = null;
                int amount =1;
                int minLevel =0;
                string nameFilter = null;

                foreach (var tok in pieces)
                {
                    if (string.IsNullOrEmpty(tok)) continue;
                    if (tok.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                    {
                        nameFilter = tok.Substring(5).Trim();
                        continue;
                    }
                    if (prefab == null && !int.TryParse(tok, out _))
                    {
                        prefab = tok;
                        continue;
                    }
                    if (int.TryParse(tok, out int parsed))
                    {
                        if (amount ==1) amount = parsed;
                        else minLevel = parsed;
                        continue;
                    }
                }

                if (prefab == null)
                {
                    FiresLogger.LogVerbose($"[FiresRPGmaker] ParseTargets failed: prefab missing in '{part}'");
                    continue;
                }
                targets.Add(new QuestTarget
                {
                    Prefab = prefab,
                    Amount = amount,
                    MinLevel = minLevel,
                    NameFilter = string.IsNullOrEmpty(nameFilter) ? null : nameFilter
                });
            }
            return targets;
        }

        private List<QuestReward> ParseRewards(string value)
        {
            var rewards = new List<QuestReward>();
            foreach (var part in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()))
            {
                // Accept formats like "Coins,50" or "Item: SwordIron,1,3"
                string prefab = null;
                int amount =1;
                int level =1;
                if (part.Contains(":"))
                {
                    // e.g. Item: Iron,10 or Skill_Exp: Clubs, 15
                    var p = part.Split(new[] { ':' },2);
                    string rewardType = p[0].Trim().ToLowerInvariant();
                    prefab = p[1].Split(',')[0].Trim();
                    if (p[1].Split(',').Length >1 && int.TryParse(p[1].Split(',')[1].Trim(), out int parsedAmount))
                        amount = parsedAmount;
                    if (p[1].Split(',').Length >2 && int.TryParse(p[1].Split(',')[2].Trim(), out int parsedLevel))
                        level = parsedLevel;
                    rewards.Add(new QuestReward
                    {
                        Type = rewardType,
                        Target = prefab,
                        Amount = amount,
                        Level = level
                    });
                }
                else
                {
                    // e.g. Coins,50
                    var pieces = part.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    if (pieces.Length >0) prefab = pieces[0];
                    if (pieces.Length >1 && int.TryParse(pieces[1], out int parsedAmount)) amount = parsedAmount;
                    if (pieces.Length >2 && int.TryParse(pieces[2], out int parsedLevel)) level = parsedLevel;
                    rewards.Add(new QuestReward
                    {
                        Type = "item",
                        Target = prefab,
                        Amount = amount,
                        Level = level
                    });
                }
            }
            return rewards;
        }

        // API Methods (now use ZDO runtime data for player-specific queries)
        public bool HasQuest(long playerId, string questId)
        {
            var playerData = VaultOfKnowledge.Instance.GetPlayerQuestData(playerId);
            return playerData.ActiveQuests.Any(q => q.QuestId == questId);
        }

        public bool HasCompletedQuest(long playerId, string questId)
        {
            var playerData = VaultOfKnowledge.Instance.GetPlayerQuestData(playerId);
            return playerData.CompletedQuests != null && playerData.CompletedQuests.ContainsKey(questId);
        }

        // Check quest requirements for a player. Returns true if all met, false otherwise and fills missing list with human-readable missing items.
        public bool CheckRequirements(long playerId, QuestDefinition def, out System.Collections.Generic.List<string> missing, System.Collections.Generic.List<StoredBankItem> inventorySnapshot = null)
        {
            missing = new System.Collections.Generic.List<string>();
            if (def == null) return true;
            if (def.Requirements == null || def.Requirements.Count ==0) return true;

            // Get player vault data (may be null)
            PlayerVaultData pvd = null;
            try { pvd = global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.Instance?.GetPlayerData(playerId); } catch { pvd = null; }

            try
            {
            FiresLogger.LogVerbose($"[FiresRPGmaker] CheckRequirements for player {playerId} quest {def.QuestId}: requirements={string.Join(" | ", def.Requirements ?? new List<string>())}");
 if (pvd != null)
 {
 if (pvd.CompletedQuests != null)
 FiresLogger.LogVerbose($"[FiresRPGmaker] PlayerVault CompletedQuests keys: {string.Join(",", pvd.CompletedQuests.Keys)}");
 else
 FiresLogger.LogVerbose($"[FiresRPGmaker] PlayerVault CompletedQuests keys: (none)");
 FiresLogger.LogVerbose($"[FiresRPGmaker] PlayerVault BankItems count: {pvd.BankItems?.Count ??0}");
 }
 }
 catch { }

            foreach (var reqRaw in def.Requirements)
            {
                if (string.IsNullOrWhiteSpace(reqRaw)) continue;
                // Support multiple requirements separated by '|' in a single entry
                var partsGroup = reqRaw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
                foreach (var part in partsGroup)
                {
                    var tokens = part.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToArray();
                    if (tokens.Length ==0) continue;
                    var key = tokens[0];
                    if (string.Equals(key, "HasCompletedQuest", StringComparison.OrdinalIgnoreCase))
                    {
                        if (tokens.Length <2) continue;
                        var q = tokens[1];
                        // resolve display name to questId if needed
                        var qid = GetQuestIdOrName(q);
                        bool done = false;
                        try
                        {
                            if (pvd != null && pvd.CompletedQuests != null)
                            {
                                // case-insensitive lookup
                                done = pvd.CompletedQuests.Keys.Any(k => string.Equals(k, qid, StringComparison.OrdinalIgnoreCase));
                            }
                        }
                        catch { done = false; }
                        if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"[FiresRPGmaker] Requirement HasCompletedQuest('{q}') -> resolved '{qid}' => {done}");
                        if (!done) missing.Add($"Completed quest '{q}'");
                    }
                    else if (string.Equals(key, "HasNotCompletedQuest", StringComparison.OrdinalIgnoreCase))
                    {
                        if (tokens.Length <2) continue;
                        var q = tokens[1];
                        var qid = GetQuestIdOrName(q);
                        bool done = false;
                        try
                        {
                            if (pvd != null && pvd.CompletedQuests != null)
                            {
                                done = pvd.CompletedQuests.Keys.Any(k => string.Equals(k, qid, StringComparison.OrdinalIgnoreCase));
                            }
                        }
                        catch { done = false; }
                        if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"[FiresRPGmaker] Requirement HasNotCompletedQuest('{q}') -> resolved '{qid}' => {done}");
                        if (done) missing.Add($"Not have completed quest '{q}'");
                    }
                    else if (string.Equals(key, "HasItem", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "HasItemInInventory", StringComparison.OrdinalIgnoreCase))
                    {
                        // Syntax: HasItem, prefabName, amount[, quality[, minDurability]]
                        if (tokens.Length <2) continue;
                        var prefab = tokens.Length >1 ? tokens[1] : string.Empty;
                        int amount =1; if (tokens.Length >2) int.TryParse(tokens[2], out amount);
                        int quality =0; if (tokens.Length >3) int.TryParse(tokens[3], out quality);
                        float minDur = -1f; if (tokens.Length >4) float.TryParse(tokens[4], out minDur);

                        int found =0;
                        try
                        {
                            // Try to check live player's inventory first
                            var player = Player.GetPlayer(playerId);
                            if (player != null)
                            {
                                try
                                {
                                    var mi = typeof(Player).GetMethod("GetInventory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    object inv = null;
                                    if (mi != null) inv = mi.Invoke(player, null);
                                    if (inv != null)
                                    {
                                        // Try common method names to count items
                                        var countMethod = inv.GetType().GetMethod("CountItems") ?? inv.GetType().GetMethod("GetItemCount") ?? inv.GetType().GetMethod("CountItem");
                                        if (countMethod != null)
                                        {
                                            try { found = (int)countMethod.Invoke(inv, new object[] { prefab }); } catch { found =0; }
                                        }
                                        else
                                        {
                                            // Fallback: try to inspect items list property
                                            var itemsProp = inv.GetType().GetProperty("m_items") ?? inv.GetType().GetProperty("m_inventory");
                                            if (itemsProp != null)
                                            {
                                                try
                                                {
                                                    var list = itemsProp.GetValue(inv) as System.Collections.IEnumerable;
                                                    if (list != null)
                                                    {
                                                        foreach (var it in list)
                                                        {
                                                            try
                                                            {
                                                                var drop = it.GetType().GetProperty("m_dropPrefab")?.GetValue(it) ?? it.GetType().GetProperty("m_prefabName")?.GetValue(it);
                                                                string name = drop?.ToString() ?? string.Empty;
                                                                if (string.Equals(name, prefab, StringComparison.OrdinalIgnoreCase))
                                                                {
                                                                    var amountProp = it.GetType().GetProperty("m_stack") ?? it.GetType().GetProperty("m_amount");
                                                                    int stack =1;
                                                                    if (amountProp != null) int.TryParse(amountProp.GetValue(it)?.ToString(), out stack);
                                                                    found += stack;
                                                                }
                                                            }
                                                            catch { }
                                                        }
                                                    }
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }

                        // If inventory check failed or insufficient, check provided snapshot first, then player's bank items as fallback
                        if (found < amount && inventorySnapshot != null)
                        {
                            try
                            {
                                int snapFound =0;
                                foreach (var sb in inventorySnapshot)
                                {
                                    if (sb == null) continue;
                                    if (string.Equals(sb.Prefab, prefab, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (quality >0 && sb.Quality != quality) continue;
                                        if (minDur >=0f && sb.Durability < minDur) continue;
                                        snapFound += sb.Amount;
                                    }
                                }
                                found += snapFound;
                            }
                            catch { }
                        }

                        if (found < amount && pvd != null)
                        {
                            try
                            {
                                int bankFound =0;
                                foreach (var obj in pvd.BankItems)
                                {
                                    try
                                    {
                                        StoredBankItem sb = null;
                                        if (obj is StoredBankItem sbi) sb = sbi;
                                        else if (obj is string s)
                                        {
                                            StoredBankItem.TryParse(s, out sb);
                                        }
                                        else
                                        {
                                            try { sb = JsonConvert.DeserializeObject<StoredBankItem>(JsonConvert.SerializeObject(obj)); } catch { sb = null; }
                                        }

                                        if (sb == null) continue;
                                        if (string.Equals(sb.Prefab, prefab, StringComparison.OrdinalIgnoreCase))
                                        {
                                            // quality and durability checks (if specified)
                                            if (quality >0 && sb.Quality != quality) continue;
                                            if (minDur >=0f && sb.Durability < minDur) continue;
                                            bankFound += sb.Amount;
                                        }
                                    }
                                    catch { }
                                }
                                found += bankFound;
                            }
                            catch { }
                        }

                        if (found < amount)
                        {
                            missing.Add($"Item {prefab} x{amount}");
                        }
                    }
                    else
                    {
                        // Unknown requirement token, treat as simple not met condition
                        // e.g., allow raw strings to appear in missing list
                        bool met = false;
                        // Basic support: HasCompletedQuest by name if token contains 'HasCompletedQuest'
                        if (part.StartsWith("HasCompletedQuest", StringComparison.OrdinalIgnoreCase))
                        {
                            var p = part.Split(new[] { ',' },2);
                            if (p.Length >1)
                            {
                                var q = p[1].Trim();
                                var qid = GetQuestIdOrName(q);
                                if (pvd != null && pvd.CompletedQuests != null && pvd.CompletedQuests.ContainsKey(qid)) met = true;
                            }
                        }

                        if (!met) missing.Add(part);
                    }
                }
            }

            return missing.Count ==0;
        }

        private string GetQuestIdOrName(string q)
        {
            if (string.IsNullOrEmpty(q)) return q;
            // If matches an existing id, return it
            if (questDatabase.ContainsKey(q)) return q;
            // Try to resolve display name
            var id = FindQuestIdByName(q);
            return id ?? q;
        }

        // RPC handler registration: clients receive file updates
        public static void HandleFileUpdate(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg == null) return;
                string folder = string.Empty;
                string fileName = string.Empty;
                string content = string.Empty;
                try { folder = pkg.ReadString(); } catch { folder = string.Empty; }
                try { fileName = pkg.ReadString(); } catch { fileName = string.Empty; }
                try { content = pkg.ReadString(); } catch { content = string.Empty; }

                if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName)) return;

                // If running as client, do not persist each incoming file to disk. Instead apply in-memory so clients can
                // update ZDO/custom data as needed without frequent disk I/O. Servers still write files to disk.
                bool isServer = (ZNet.instance != null && ZNet.instance.IsServer());

                string baseDir = Path.Combine(Paths.ConfigPath, "FiresRPGmaker");
                string targetDir = Path.Combine(baseDir, folder);
                string fullPath = Path.Combine(targetDir, fileName);

                bool wrote = false;
                try
                {
                    if (!isServer)
                    {
                        // Client: apply in-memory only
                        try
                        {
                            if (QuestManager.Instance == null)
                            {
                                QuestManager.Init();
                            }

                            QuestManager.Instance.ApplyFileContent(folder, fileName, content);
                            wrote = false; // not written to disk
                            FiresLogger.LogVerbose($"Applied file update in-memory {folder}/{fileName} from {sender}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[FiresRPGmaker] Failed to apply file update in-memory {folder}/{fileName}: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Server: persist to disk as before
                        Directory.CreateDirectory(targetDir);
                        if (string.IsNullOrEmpty(content))
                        {
                            if (File.Exists(fullPath))
                            {
                                File.Delete(fullPath);
                                wrote = true;
                                FiresLogger.LogVerbose($"Deleted local file {folder}/{fileName}");
                            }
                        }
                        else
                        {
                            File.WriteAllText(fullPath, content);
                            wrote = true;
                            FiresLogger.LogVerbose($"Wrote local file {folder}/{fileName} ({content.Length} bytes)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Failed to write/delete received file {fullPath}: {ex.Message}");
                }

                FiresLogger.LogVerbose($"Received file update {folder}/{fileName} from {sender} (wrote={wrote})");

                // Ensure QuestManager initialized on clients before reloading
                if (QuestManager.Instance == null)
                {
                    try
                    {
                        QuestManager.Init();
                        FiresLogger.LogVerbose("QuestManager.Init() called from HandleFileUpdate");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to init QuestManager on file update: {ex.Message}");
                    }
                }

                // If quests changed, reload local DB
                if (folder.Equals("Quests", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // If server wrote to disk, reload from disk. If client applied in-memory, ApplyFileContent already updated DB.
                        if (isServer)
                        {
                            QuestManager.Instance?.LoadQuestDatabase();
                            FiresLogger.LogVerbose($"Reloaded quest DB; count={QuestManager.Instance?.GetAllQuests().Count ??0}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to reload quest DB after file update: {ex.Message}");
                    }
                }
                else if (folder.Equals("QuestProfiles", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (isServer)
                        {
                            QuestManager.Instance?.LoadQuestProfiles();
                            FiresLogger.LogVerbose($"Reloaded quest profiles; count={QuestManager.Instance?.GetQuestsForProfile("DefaultProfile")?.Count ??0}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FiresRPGmaker] Failed to reload quest profiles after file update: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] HandleFileUpdate failed: {ex.Message}");
            }
        }

        // Apply a single file's content into in-memory quest database or profiles (clients only).
        // This avoids repeated disk writes for frequent updates; servers still persist to disk.
        public void ApplyFileContent(string folder, string fileName, string content)
        {
            try
            {
                if (string.Equals(folder, "Quests", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse the content as if it were a file and merge/update entries in questDatabase
                    var lines = (content ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
                    string fname = fileName ?? "(memory)";
                    bool forceKGPositional = false;
                    try
                    {
                        var ln = fname.ToLowerInvariant();
                        if (ln.Contains(".legacy") || ln.StartsWith("quests.legacy") || ln.Contains("kg")) forceKGPositional = true;
                    }
                    catch { forceKGPositional = false; }

                    // We'll parse blocks similar to LoadQuestDatabase but only for this content
                    for (int i =0; i < lines.Length; i++)
                    {
                        var trimmed = lines[i];
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;
                        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                        {
                            var inner = trimmed.Substring(1, trimmed.Length -2);
                            bool autocompleteFlag = false;
                            var questId = inner;
                            if (inner.Contains("="))
                            {
                                var partsHeader = inner.Split(new[] { '=' },2);
                                questId = partsHeader[0].Trim();
                                var flag = partsHeader[1].Trim();
                                if (flag.Equals("autocomplete", StringComparison.OrdinalIgnoreCase)) autocompleteFlag = true;
                            }

                            var def = new QuestDefinition(questId) { Autocomplete = autocompleteFlag };
                            var block = new List<string>();
                            int j = i +1;
                            while (j < lines.Length)
                            {
                                var t = lines[j].Trim();
                                if (string.IsNullOrEmpty(t) || t.StartsWith(";") || t.StartsWith("#")) { j++; continue; }
                                if (t.StartsWith("[") && t.EndsWith("]")) break;
                                block.Add(t);
                                j++;
                            }
                            i = j -1;

                            // Auto-detect positional format per block (same logic as LoadQuestDatabase)
                            bool usePositional = forceKGPositional;
                            if (!usePositional && block.Count >= 4)
                            {
                                bool hasKeyValue = false;
                                for (int k = 0; k < Math.Min(3, block.Count); k++)
                                {
                                    if (block[k].Contains("="))
                                    {
                                        hasKeyValue = true;
                                        break;
                                    }
                                }
                                if (!hasKeyValue) usePositional = true;
                            }

                            // Reuse parsing logic from LoadQuestDatabase for this block
                            if (usePositional && block.Count >0)
                            {
                                int pos =0;
                                if (pos < block.Count) def.QuestType = block[pos++].Trim();
                                if (pos < block.Count) def.Name = block[pos++].Trim();
                                int descStart = pos;
                                int targetsIdx = -1;
                                for (int k = pos; k < block.Count; k++)
                                {
                                    if (LooksLikeTargetLine(block[k]))
                                    {
                                        targetsIdx = k;
                                        break;
                                    }
                                }
                                if (targetsIdx == -1)
                                {
                                    if (pos < block.Count) { def.Description = block[pos++].Trim(); }
                                    if (pos < block.Count)
                                    {
                                        try { def.QuestTargets = ParseTargets(block[pos++]); } catch { def.QuestTargets = new List<QuestTarget>(); }
                                    }
                                }
                                else
                                {
                                    if (targetsIdx > pos)
                                    {
                                        def.Description = string.Join("\n", block.Skip(pos).Take(targetsIdx - pos));
                                    }
                                    else
                                    {
                                        def.Description = string.Empty;
                                    }
                                    def.QuestTargets = ParseTargets(block[targetsIdx]);
                                    pos = targetsIdx +1;
                                }
                                if (pos < block.Count) def.Rewards = ParseRewards(block[pos++]);
                                if (pos < block.Count)
                                {
                                    var cooldownLine = block[pos++];
                                    var parts = cooldownLine.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                                    if (parts.Length >0 && int.TryParse(parts[0], out int cd)) def.CooldownDays = cd;
                                    if (parts.Length >1 && int.TryParse(parts[1], out int tl)) def.TimeLimitMinutes = tl;
                                }
                                if (pos < block.Count)
                                {
                                    var req = block[pos++].Trim();
                                    if (!string.IsNullOrEmpty(req) && !req.Equals("None", StringComparison.OrdinalIgnoreCase)) def.Requirements = req.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                                }
                                if (usePositional && !def.Autocomplete) def.Autocomplete = true;
                            }
                            else
                            {
                                string targetsValue = null;
                                string mNamesValue = null;
                                string legacyNamesValue = null;
                                foreach (var line in block)
                                {
                                    string key = null; string value = null;
                                    if (line.Contains("=")) { var parts = line.Split(new[] { '=' },2); if (parts.Length !=2) continue; key = parts[0].Trim(); value = parts[1].Trim(); }
                                    else if (line.Contains(":")) { var parts = line.Split(new[] { ':' },2); if (parts.Length !=2) continue; key = parts[0].Trim(); value = parts[1].Trim(); }
                                    if (string.IsNullOrEmpty(key)) continue;
                                    var norm = key.Replace(" ", string.Empty).Replace("Quest", string.Empty).Trim().ToLowerInvariant();
                                    switch (norm)
                                    {
                                        case "target":
                                        case "questtarget":
                                            targetsValue = value;
                                            break;
                                        case "m_names":
                                        case "m_name":
                                            mNamesValue = value;
                                            break;
                                        // Ignore Name/Names in non-legacy configs
                                    }
                                }
                                if (targetsValue != null) def.QuestTargets = ParseTargetsWithNames(targetsValue, mNamesValue, legacyNamesValue, questId);
                                foreach (var line in block)
                                {
                                    string key = null; string value = null;
                                    if (line.Contains("=")) { var parts = line.Split(new[] { '=' },2); if (parts.Length !=2) continue; key = parts[0].Trim(); value = parts[1].Trim(); }
                                    else if (line.Contains(":")) { var parts = line.Split(new[] { ':' },2); if (parts.Length !=2) continue; key = parts[0].Trim(); value = parts[1].Trim(); }
                                    if (string.IsNullOrEmpty(key)) continue;
                                    var norm = key.Replace(" ", string.Empty).Replace("Quest", string.Empty).Replace("Quest", string.Empty).Trim();
                                    norm = norm.ToLowerInvariant();
                                    switch (norm)
                                    {
                                        case "type":
                                        case "questtype":
                                            def.QuestType = value;
                                            break;
                                        case "name":
                                        case "questname":
                                            def.Name = value;
                                            break;
                                        case "description":
                                            def.Description = value;
                                            break;
                                        case "reward":
                                        case "questreward":
                                            def.Rewards = ParseRewards(value);
                                            break;
                                        case "cooldowndays":
                                            if (int.TryParse(value, out int cd)) def.CooldownDays = cd; else def.CooldownDays =0;
                                            break;
                                        case "timelimitminutes":
                                            if (int.TryParse(value, out int tl)) def.TimeLimitMinutes = tl; else def.TimeLimitMinutes =0;
                                            break;
                                        case "requirement":
                                        case "requirements":
                                        case "questrequirements":
                                            def.Requirements = value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                                            break;
                                        case "autocomplete":
                                            if (bool.TryParse(value, out bool ac)) def.Autocomplete = ac;
                                            else if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) def.Autocomplete = true;
                                            break;
                                        case "previewimage":
                                            def.PreviewImage = value;
                                            break;
                                        case "onaccept":
                                        case "onacceptquest":
                                            def.LifecycleEvents["OnAcceptQuest"] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                        case "oncomplete":
                                        case "oncompletequest":
                                            def.LifecycleEvents["OnCompleteQuest"] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                    }
                                }
                            }
                            // Merge into in-memory DB
                            questDatabase[def.QuestId] = def;
                        }
                    }

                    FiresLogger.LogVerbose($"Applied in-memory quest file {fileName}; total quests={questDatabase.Count}");
                }
                else if (string.Equals(folder, "QuestProfiles", StringComparison.OrdinalIgnoreCase))
                {
                    // Forward profile data to QuestProfileManager which handles the client-side profile parsing
    // This ensures clients use server's profile data, not local files
 try
    {
             global::FiresRPGmaker.UI.QuestProfileManager.ApplyProfileDataFromServer(fileName, content);
      int __profileCount = global::FiresRPGmaker.UI.QuestProfileManager.GetAllProfileNames().Count;
      FiresLogger.LogVerbose($"Applied in-memory profile file {fileName}; profiles={__profileCount}");
   }
    catch (Exception ex)
  {
    Debug.LogWarning($"[FiresRPGmaker] Failed to apply profile content via QuestProfileManager: {ex.Message}");
   }
   
   // Also update local QuestManager.questProfiles for backward compatibility
         try
      {
              var lines = (content ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
       string currentProfile = null;
      var tempProfiles = new Dictionary<string, List<string>>();
foreach (var raw in lines)
  {
      var line = raw.Trim();
 if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#")) continue;
    if (line.StartsWith("[") && line.EndsWith("]"))
         {
                   currentProfile = line.Substring(1, line.Length -2);
      tempProfiles[currentProfile] = new List<string>();
          continue;
        }
           if (currentProfile == null) continue;
         var parts = line.Split(new[] { '=' },2);
       if (parts.Length !=2) continue;
   var key = parts[0].Trim(); var value = parts[1].Trim();
      if (key == "Quests")
           {
     var profileQuests = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
          tempProfiles[currentProfile] = profileQuests;
               }
     }
    // merge into questProfiles
           foreach (var kv in tempProfiles) questProfiles[kv.Key] = kv.Value;
     }
          catch (Exception ex)
{
              Debug.LogWarning($"[FiresRPGmaker] Failed to apply profile content to local cache: {ex.Message}");
          }
          }
    }
    catch (Exception ex)
    {
        Debug.LogWarning($"[FiresRPGmaker] ApplyFileContent failed: {ex.Message}");
    }
        }


        private static string NormalizeForCompare(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var arr = s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(arr);
        }
    }

    public class QuestDefinition
    {
        public string QuestId { get; set; }
        public string QuestType { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<QuestTarget> QuestTargets { get; set; } = new List<QuestTarget>();
        public List<QuestReward> Rewards { get; set; } = new List<QuestReward>();
        public int CooldownDays { get; set; }
        public int TimeLimitMinutes { get; set; }
        public List<string> Requirements { get; set; } = new List<string>();
        public bool Autocomplete { get; set; }
     public string PreviewImage { get; set; }  // Optional custom preview image filename (without extension)

        /// <summary>KG-parity: when this quest auto-completes, the quest with this ID is auto-accepted
        /// for the same player (chain-quest follow-up). Empty/null = no chain.</summary>
        public string TransferComplition { get; set; }

        /// <summary>KG-parity: popup text shown when a TransferComplition chain fires.</summary>
        public string TransferComplitionText { get; set; }

        /// <summary>KG-parity lifecycle hook commands. Keys: OnAcceptQuest / OnCancelQuest /
        /// OnCompleteQuest / OnQuestTimeout / OnDeath. Values: command strings run through
        /// <c>DialogueCommandEngine.ExecuteAll</c> when the event fires.</summary>
        public Dictionary<string, List<string>> LifecycleEvents { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    public QuestDefinition() { }

  public QuestDefinition(string questId)
{
            QuestId = questId;
        }
    }


    public class QuestTarget
    {
        public string Prefab { get; set; }
        public int Amount { get; set; }
        public int MinLevel { get; set; } = 0;
        // Optional filter for target display name (Character.m_name or Piece.m_name)
        public string NameFilter { get; set; } = null;
    }

    [Serializable]
    public class PlayerQuestData
    {
        public List<ActiveQuestEntry> ActiveQuests { get; set; } = new List<ActiveQuestEntry>();
        public List<string> CompletedQuests { get; set; } = new List<string>();
        // Add other data later (stats, bank, etc.)
    }

    [Serializable]
    public class ActiveQuestEntry
    {
        public string QuestId { get; set; }
        public int Progress { get; set; } = 0;
        public DateTime StartedAt { get; set; }
    }

    public class QuestReward
    {
        public string Type { get; set; }
        public string Target { get; set; }
        public int Amount { get; set; }
        public int Level { get; set; } = 1;

        /// <summary>
        /// Whether this reward can actually be given. Set during validation on world load.
        /// Item rewards require the prefab to exist in ZNetScene/ObjectDB.
        /// Skill_Exp rewards require the skill type to be valid.
        /// Defaults to true (assume available until validated).
        /// </summary>
        public bool IsAvailable { get; set; } = true;

        /// <summary>
        /// True if this reward grants skill experience rather than an item.
        /// </summary>
        public bool IsSkillExp => Type != null && Type.IndexOf("skill_exp", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
