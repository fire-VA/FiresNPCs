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
        private Dictionary<string, List<string>> questProfiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private string QuestsDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.QuestDatabase);
        private string ProfilesDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.QuestProfiles);

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

            // Drop the legacy profile-global quest-DB hash (pre per-server fix). The DB
            // fingerprint is now computed live from the in-memory DB, so a stale hash from
            // another server can no longer bleed quests or block a legitimate re-send.
            FiresCore.Sync.FiresServerScope.DeleteLegacy(
                System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "FiresUnifiedCore", "cache", "manifests", "QuestDatabase.hash"));

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
                // Objective-type prefix ("Item:"/"Kill:"/…) overrides the quest-level type for this objective.
                string objType = null;
                int kc = part.IndexOf(':');
                if (kc > 0)
                {
                    var kind = QuestTarget.NormalizeObjectiveType(part.Substring(0, kc));
                    if (kind != null) { objType = kind; part = part.Substring(kc + 1).Trim(); }
                }
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
                if (TryParseMoveCoordinates(part, objType, out var moveAt, out var moveAmount))
                {
                    targets.Add(new QuestTarget { Prefab = moveAt, Amount = moveAmount, Type = "move", NameFilter = nameFilter });
                    continue;
                }
                targets.Add(new QuestTarget
                {
                    Prefab = prefab,
                    Amount = amount,
                    MinLevel = minLevel,
                    NameFilter = nameFilter,
                    Type = objType
                });
            }
            return targets;
        }

        /// <summary>0.0.532: a Move objective's target is a POSITION, "x y z [amount]" (separators: space, ',', ':' or
        /// ';'; decimals and negatives allowed), with or without a "Move:" prefix. It becomes the "x,y,z" Prefab the Move
        /// check reads. Both target parsers used to split it like "Prefab, amount", so no Move target could ever be
        /// written. A target only counts as a position when its first three tokens are numbers, or it has "Move:".</summary>
        internal static bool TryParseMoveCoordinates(string part, string objType, out string prefab, out int amount)
        {
            prefab = null; amount = 1;
            if (string.IsNullOrWhiteSpace(part)) return false;
            var tokens = part.Split(new[] { ' ', ',', ':', ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            bool isMove = string.Equals(objType, "move", StringComparison.OrdinalIgnoreCase);
            // 0.0.544 ([visual]'s bug_move): a "Move: x y z" line reached here with its prefix still on, the "Move" token broke
            // the number scan at once, and the line fell through to the prefab parser (coordinates read as a prefab name).
            if (tokens.Count > 0 && string.Equals(tokens[0].Trim(), "move", StringComparison.OrdinalIgnoreCase)) { tokens.RemoveAt(0); isMove = true; }
            var nums = new List<float>();
            foreach (var tok in tokens)
            {
                if (!float.TryParse(tok.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) break;
                nums.Add(f);
            }
            if (nums.Count < 3 || (!isMove && nums.Count != tokens.Count)) return false;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            prefab = nums[0].ToString("0.##", ci) + "," + nums[1].ToString("0.##", ci) + "," + nums[2].ToString("0.##", ci);
            if (nums.Count >= 4 && nums[3] >= 1f) amount = (int)nums[3];
            return true;
        }

        public void LoadQuestDatabase()
        {
            questDatabase.Clear();
            ForgetSerialisedQuestDb();
            // Pure clients parse the server-synced in-memory store (no config files on client disk);
            // server/host reads its folder as before.
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines("Quests"))
            {
                try
                {
                    var lines = kv.Value;
                    var fname = Path.GetFileName(kv.Key) ?? string.Empty;
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
                                // kg optional 8th line: "TransferQuestName[,PopupText]" chain-completion
                                if (pos < block.Count) ApplyTransferComplitionLine(def, block[pos++]);
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
                                        // NOTE the key normalizer strips "Quest" before matching, so kg's
                                        // OnCancelQuest/OnQuestTimeout arrive here as oncancel/ontimeout.
                                        case "oncancel":
                                            def.LifecycleEvents[global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnCancel] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                        case "ontimeout":
                                            def.LifecycleEvents[global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnTimeout] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                        case "ondeath":
                                            def.LifecycleEvents[global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnDeath] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                        case "transfercomplition":   // kg's spelling
                                        case "transfercompletion":   // corrected-spelling alias
                                            ApplyTransferComplitionLine(def, value);
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
                    Debug.LogError($"[FiresRPGmaker] Failed to load quest file {kv.Key}: {ex.Message}");
                }
            }
            MergeQuestEventFiles();
            ForgetSerialisedQuestDb();
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
            var issues = new List<string>();

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
                                issues.Add($"{def.QuestId}: skill '{reward.Target}'");
                                if (FiresLogger.VerboseEnabled)
                                    FiresLogger.LogWarning($"[QuestManager] Quest '{def.QuestId}' has invalid skill reward: Skill_Exp '{reward.Target}' is not a valid skill type");
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
                                issues.Add($"{def.QuestId}: item '{reward.Target}'");
                                if (FiresLogger.VerboseEnabled)
                                    FiresLogger.LogWarning($"[QuestManager] Quest '{def.QuestId}' has unavailable item reward: '{reward.Target}' not found in ZNetScene/ObjectDB (mod removed?)");
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
                            issues.Add($"{def.QuestId}: target '{target.Prefab}'");
                            if (FiresLogger.VerboseEnabled)
                                FiresLogger.LogWarning($"[QuestManager] Quest '{def.QuestId}' has unavailable target: '{target.Prefab}' not found in ZNetScene (mod removed?)");
                        }
                    }
                }
            }

            if (issues.Count > 0)
                EmitQuestValidationSummary(issues, questDatabase.Count);
            else
                FiresLogger.LogVerbose($"[QuestManager] Quest validation complete: all {questDatabase.Count} quests OK");
            ForgetSerialisedQuestDb();
        }

        // Summarizes unresolved-quest issues into ONE neat box (📖, quest-colored via FiresLogColorPatch) instead of
        // a warning line per issue. Full per-issue detail stays available under verbose logging. Routes through
        // LoadSummary → the mod log source, so it prints once (no Debug.Log stdout-echo duplicate).
        private void EmitQuestValidationSummary(List<string> issues, int totalQuests)
        {
            const int maxRows = 16;
            var lines = new List<string> { $"{issues.Count} issue(s) / {totalQuests} quests" };
            int show = Math.Min(issues.Count, maxRows);
            for (int i = 0; i < show; i++) lines.Add(issues[i]);
            if (issues.Count > show) lines.Add($"…+{issues.Count - show} more");
            lines.Add("(missing prefab = mod removed?)");
            FiresCore.Logging.LoadSummary.EmitMiniBox("📖 QUEST ISSUES", lines.ToArray(), "[FiresRPGmaker] [LoadSummary]");
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
        // Serialising the quest DB cost ~38 ms on the rig dedi, inside the join frame (2026-09-28 R20), and again for every
        // manifest fingerprint. The JSON and its hash are kept until the DB changes; every change (LoadQuestDatabase, a received
        // DB, an applied quest file, ValidateQuestDatabase, the parity test's sentinels) drops them.
        private string _serialisedQuestDb;
        private string _serialisedQuestDbHash;

        internal void ForgetSerialisedQuestDb()
        {
            _serialisedQuestDb = null;
            _serialisedQuestDbHash = null;
        }

        private string SerialiseQuestDb()
        {
            if (_serialisedQuestDb != null) return _serialisedQuestDb;
            var dict = questDatabase.ToDictionary(kv => kv.Key, kv => kv.Value);
            _serialisedQuestDb = JsonConvert.SerializeObject(dict, Formatting.None) ?? string.Empty;
            return _serialisedQuestDb;
        }

        private string SerialisedQuestDbHash() =>
            _serialisedQuestDbHash ?? (_serialisedQuestDbHash = ManifestDiffSync.ComputeSha256(SerialiseQuestDb()));

        /// <summary>
        /// Live fingerprint of the in-memory quest DB. Replaces the old persisted
        /// (profile-global) QuestDatabase.hash so the manifest fast-path tracks the
        /// CURRENT server's DB — it can't bleed another server's quests, and a stale
        /// hash can't block a legitimate re-send (the DB itself was never persisted).
        /// Mirrors the territory live-fingerprint fix.
        /// </summary>
        public static string GetLiveQuestDbFingerprint()
        {
            try { return Instance != null ? Instance.SerialisedQuestDbHash() : ManifestDiffSync.ComputeSha256(string.Empty); }
            catch { return string.Empty; }
        }

        // Send the full in-memory quest database as JSON to a peer. Used to ensure clients have authoritative DB.
        // Diff-sync: the peer reports a LIVE fingerprint of its in-memory DB
        // (GetLiveQuestDbFingerprint); if it already matches this payload the send is skipped.
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
            // Ask for the peer's manifest first and serialise a frame later, so a cold serialisation never lands in the
            // joining peer's RPC_PeerInfo frame.
            ManifestDiffSync.RequestFromPeer(peerUid, QuestDbManifestNamespace);
            yield return null;

            string json;
            string localHash;
            try
            {
                json = SerialiseQuestDb();
                localHash = SerialisedQuestDbHash();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] SerialiseQuestDb failed for peer {peerUid}: {ex.Message}");
                yield break;
            }

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
                        Instance.ForgetSerialisedQuestDb();
                        FiresLogger.LogVerbose($"Received QuestDB from {sender}; loaded {Instance.questDatabase.Count} quests");

                        // No hash persisted: the manifest fingerprint is computed live from
                        // this in-memory DB (GetLiveQuestDbFingerprint), so it always reflects
                        // the current server and can't bleed across servers.

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
        // regardless of whether anything had changed - a double-whammy with
        // SendAllConfigsToClientCoroutine's own traversal of the same folders.
        //
        // If the client doesn't reply with a manifest in time, we fall through to the
        // legacy "send everything" path for correctness.
        //
        // The wait YIELDS (a coroutine on the plugin). It used to poll with Thread.Sleep(50) x5 per folder on the
        // caller's thread, which is the MAIN thread (RPC_PeerInfo postfix, spawn handler). A manifest reply is a
        // routed RPC that only the main thread dispatches, so it could never arrive during that wait: every join
        // blocked the dedi ~500 ms for nothing and then shipped every file anyway (R2 2026-09-28: 512-581 ms inside
        // RPC_PeerInfo on 3 of 3 joins). A manifest reply also proves the client's handlers are registered, which the
        // T+0 send used to race.
        // A joining client answers only once it has loaded, often 5-10 s after RPC_PeerInfo with fresh caches (rig R22b/R23);
        // the config watcher pre-waits 10 s on the same manifests for the same reason.
        private const float QuestManifestWaitSeconds = 10f;

        public void SendAllQuestFilesToPeer(long peerUid)
        {
            try
            {
                // Fire both manifest requests up front so the replies travel in parallel; safe to call even if the
                // main config coroutine has already cached them (the request clears and re-fetches).
                ManifestDiffSync.RequestFromPeer(peerUid, "Cfg:Quests");
                ManifestDiffSync.RequestFromPeer(peerUid, "Cfg:QuestProfiles");

                if (FiresRPGmaker.Instance != null)
                {
                    FiresRPGmaker.Instance.StartCoroutine(SendAllQuestFilesCoroutine(peerUid));
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Could not start the quest-file sync coroutine for peer {peerUid}, sending every file now: {ex.Message}");
            }

            // No coroutine host: send now without waiting (no manifest yet = everything is sent).
            SendQuestFoldersNow(peerUid);
        }

        private System.Collections.IEnumerator SendAllQuestFilesCoroutine(long peerUid)
        {
            yield return ManifestDiffSync.WaitForManifest(peerUid, "Cfg:Quests", QuestManifestWaitSeconds);
            TrySendFolderDiffed(peerUid, QuestsDir, "Quests", "Cfg:Quests");

            // Usually already here: both requests went out together.
            yield return ManifestDiffSync.WaitForManifest(peerUid, "Cfg:QuestProfiles", QuestManifestWaitSeconds);
            TrySendFolderDiffed(peerUid, ProfilesDir, "QuestProfiles", "Cfg:QuestProfiles");
        }

        private void SendQuestFoldersNow(long peerUid)
        {
            TrySendFolderDiffed(peerUid, QuestsDir, "Quests", "Cfg:Quests");
            TrySendFolderDiffed(peerUid, ProfilesDir, "QuestProfiles", "Cfg:QuestProfiles");
        }

        // An exception must not escape into the coroutine (Unity would stop it silently, skipping the next folder).
        private void TrySendFolderDiffed(long peerUid, string folderPath, string folderName, string manifestNamespace)
        {
            try { SendFolderDiffed(peerUid, folderPath, folderName, manifestNamespace); }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Quest folder '{folderName}' sync to peer {peerUid} failed: {ex.Message}"); }
        }

        private void SendFolderDiffed(long peerUid, string folderPath, string folderName, string manifestNamespace)
        {
            if (!Directory.Exists(folderPath)) return;

            // Callers wait for the manifest first (yielding); here it is only looked up, never waited for.
            bool haveManifest = ManifestDiffSync.TryGetManifest(peerUid, manifestNamespace, out _);

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
                    string relPath = fileName; // top-level .cfg - relative path = filename
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
            else if (!haveManifest && sent > 0)
                Debug.Log($"[FiresRPGmaker] Quest folder '{folderName}': no manifest from peer {peerUid} within {QuestManifestWaitSeconds:0} s, sent all {sent} file(s).");
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

        // Reliably ship every quest-profile .cfg to a peer — the blind counterpart to
        // BroadcastQuestDatabaseToPeer. The quest DATABASE has a dedicated always-send broadcast, but
        // profiles only had the manifest-gated folder diff-sync (SendFolderDiffed) which fires immediately
        // at RPC_PeerInfo and races the client's handler registration — leaving clients with ZERO profiles
        // → "no quests available" on every NPC. The DB blob works precisely because it sends on a delayed
        // coroutine; profiles now do the same. Reuses FiresRPGmaker_FileUpdate, whose client handler already
        // stores + parses QuestProfiles (ApplyProfileDataFromServer).
        public void BroadcastQuestProfilesToPeer(long peerUid)
        {
            try
            {
                if (FiresRPGmaker.Instance != null)
                {
                    FiresRPGmaker.Instance.StartCoroutine(BroadcastQuestProfilesCoroutine(peerUid));
                    return;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Could not start quest-profile broadcast coroutine, sending immediately: {ex.Message}"); }
            SendQuestProfilesBlind(peerUid);
        }

        private System.Collections.IEnumerator BroadcastQuestProfilesCoroutine(long peerUid)
        {
            // Give the client a beat to finish registering its routed-RPC handlers — an immediate fire at
            // RPC_PeerInfo can race, which is exactly why the folder diff-sync was silently dropping profiles.
            yield return new WaitForSeconds(1f);
            SendQuestProfilesBlind(peerUid);
        }

        private void SendQuestProfilesBlind(long peerUid)
        {
            try
            {
                string dir = ProfilesDir;
                if (!Directory.Exists(dir))
                {
                    Debug.LogWarning($"[FiresRPGmaker] Quest-profile broadcast: profiles dir missing ({dir})");
                    return;
                }
                int sent = 0;
                foreach (var f in Directory.GetFiles(dir, "*.cfg", SearchOption.AllDirectories))
                {
                    string content;
                    try { content = File.ReadAllText(f); }
                    catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Quest-profile broadcast read {Path.GetFileName(f)} failed: {ex.Message}"); continue; }
                    string rel = f.Length > dir.Length ? f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/') : Path.GetFileName(f);
                    SendFileToPeer(peerUid, "QuestProfiles", rel, content);
                    sent++;
                }
                Debug.Log($"[FiresRPGmaker] Broadcast {sent} quest-profile file(s) to peer {peerUid} (blind reliable send).");
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] SendQuestProfilesBlind failed: {ex.Message}"); }
        }

        /// <summary>
        /// kg QuestEvents family (Configs/QuestEvents/*.cfg + our Quests/Events group): `[questname]`
        /// sections holding `EventName:command` lines (OnAcceptQuest / OnCancelQuest / OnCompleteQuest /
        /// OnQuestTimeout / OnDeath — kg Quests_Main_Server.cs:340 grammar). Merged into the ALREADY
        /// loaded definitions: multiple lines per event append. Full-reload call appends onto fresh
        /// definitions; the per-quest overload serves the in-memory sync path (one fresh def at a time)
        /// so nothing ever double-appends.
        /// </summary>
        private void MergeQuestEventFiles(QuestDefinition onlyInto = null)
        {
            int merged = 0;
            void ParseEventLines(System.Collections.Generic.IEnumerable<string> lines)
            {
                QuestDefinition current = null;
                foreach (var raw in lines)
                {
                    var line = raw?.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//")) continue;
                    if (line.StartsWith("["))
                    {
                        string qname = line.Trim('[', ']').Replace(" ", "").Trim();
                        var def = GetQuestDefinition(qname);
                        current = (onlyInto == null || ReferenceEquals(def, onlyInto)) ? def : null;
                        continue;
                    }
                    if (current == null) continue;
                    int c = line.IndexOf(':');
                    if (c <= 0) continue;
                    string evKey = NormalizeQuestEventKey(line.Substring(0, c));
                    string command = line.Substring(c + 1).Trim();
                    if (evKey == null || command.Length == 0) continue;
                    if (!current.LifecycleEvents.TryGetValue(evKey, out var list) || list == null)
                        current.LifecycleEvents[evKey] = list = new List<string>();
                    list.Add(command);
                    merged++;
                }
            }

            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines("QuestEvents"))
            {
                try { ParseEventLines(kv.Value); }
                catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] QuestEvents file {kv.Key} failed: {ex.Message}"); }
            }
            try
            {
                // kg drop-in tree (config/Marketplace/Configs/QuestEvents) — sibling of the Traders folder.
                string kgFolder = Path.Combine(Path.GetDirectoryName(FiresCore.Compat.MarketplacePaths.TradersFolder) ?? "", "QuestEvents");
                if (Directory.Exists(kgFolder))
                    foreach (var f in Directory.GetFiles(kgFolder, "*.cfg", SearchOption.AllDirectories))
                        ParseEventLines(File.ReadAllLines(f));
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] kg QuestEvents drop-in read failed: {ex.Message}"); }

            if (merged > 0 && onlyInto == null)
                FiresLogger.LogVerbose($"Merged {merged} quest-event command line(s) from the QuestEvents family");
        }

        private static string NormalizeQuestEventKey(string evName)
        {
            switch ((evName ?? "").Replace(" ", "").ToLowerInvariant())
            {
                case "onacceptquest": case "onaccept": return global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnAccept;
                case "oncancelquest": case "oncancel": return global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnCancel;
                case "oncompletequest": case "oncomplete": return global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnComplete;
                case "onquesttimeout": case "ontimeout": return global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnTimeout;
                case "ondeath": return global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnDeath;
                default: return null;
            }
        }

        /// <summary>
        /// kg's optional trailing quest-block line (schema Quests.md offset +7):
        /// "TransferQuestName[,PopupText]" — completing this quest chain-accepts/credits the named
        /// quest and shows the popup text. Shared by both positional parse paths.
        /// </summary>
        private static void ApplyTransferComplitionLine(QuestDefinition def, string line)
        {
            if (def == null || string.IsNullOrEmpty(line)) return;
            var t = line.Trim();
            if (t.Length == 0 || t.Equals("None", StringComparison.OrdinalIgnoreCase)) return;
            int c = t.IndexOf(',');
            def.TransferComplition = (c < 0 ? t : t.Substring(0, c)).Trim();
            if (c >= 0) def.TransferComplitionText = t.Substring(c + 1).Trim();
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

                // Objective-type prefix (kg): "Item: X" (collect), "Kill: X", "Craft: X", … overrides the quest-level
                // type for THIS objective. Strip it so the prefab parses clean and record the per-line verb.
                string objType = null;
                int kindColon = p.IndexOf(':');
                if (kindColon > 0)
                {
                    var kind = QuestTarget.NormalizeObjectiveType(p.Substring(0, kindColon));
                    if (kind != null) { objType = kind; p = p.Substring(kindColon + 1).Trim(); }
                }

                if (TryParseMoveCoordinates(p, objType, out var moveAt, out var moveAmount))
                {
                    targets.Add(new QuestTarget { Prefab = moveAt, Amount = moveAmount, Type = "move" });
                    continue;
                }

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
                    NameFilter = string.IsNullOrEmpty(nameFilter) ? null : nameFilter,
                    Type = objType
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
                    // kg progression gate: "QuestFinished: X" (or "QuestFinished, X") = quest X must already be
                    // completed. This is what chains SnorriOne → SnorriOne2 etc. Handle it before the ',' tokenizer
                    // since the arg is colon-separated.
                    var trimmed = part.TrimStart();
                    if (trimmed.StartsWith("QuestFinished", StringComparison.OrdinalIgnoreCase))
                    {
                        var qraw = trimmed.Substring("QuestFinished".Length).TrimStart(':', ',', ' ').Trim();
                        if (!string.IsNullOrEmpty(qraw))
                        {
                            var qid = GetQuestIdOrName(qraw);
                            bool doneQ = pvd?.CompletedQuests != null && pvd.CompletedQuests.Keys.Any(k => string.Equals(k, qid, StringComparison.OrdinalIgnoreCase));
                            if (FiresLogger.VerboseEnabled) FiresLogger.LogVerbose($"[FiresRPGmaker] Requirement QuestFinished('{qraw}') -> '{qid}' => {doneQ}");
                            if (!doneQ) missing.Add($"Completed quest '{qraw}'");
                        }
                        continue;
                    }
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
                        // 0.0.532: count the live inventory through vanilla's own API. The reflection here called
                        // Inventory.CountItems with 1 argument (it takes 3), and the fallback read m_items / m_inventory as
                        // properties (they are fields), so every HasItem requirement failed from the inventory.
                        try
                        {
                            var player = Player.GetPlayer(playerId);
                            var inv = player != null ? player.GetInventory() : null;
                            if (inv != null)
                            {
                                foreach (var it in inv.GetAllItems())
                                {
                                    if (it == null) continue;
                                    string name = it.m_dropPrefab != null ? it.m_dropPrefab.name : it.m_shared?.m_name;
                                    if (!string.Equals(name, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                                    if (quality > 0 && it.m_quality != quality) continue;
                                    if (minDur >= 0f && it.m_durability < minDur) continue;
                                    found += it.m_stack;
                                }
                            }
                        }
                        catch (Exception ex) { FiresLogger.LogVerbose($"[FiresRPGmaker] HasItem inventory count failed: {ex.Message}"); }

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

                string targetDir = FiresCore.Storage.FiresConfigPaths.GroupedPath(folder);
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
                                // kg optional 8th line: "TransferQuestName[,PopupText]" chain-completion
                                if (pos < block.Count) ApplyTransferComplitionLine(def, block[pos++]);
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
                                        // NOTE the key normalizer strips "Quest" before matching, so kg's
                                        // OnCancelQuest/OnQuestTimeout arrive here as oncancel/ontimeout.
                                        case "oncancel":
                                            def.LifecycleEvents[global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnCancel] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                        case "ontimeout":
                                            def.LifecycleEvents[global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnTimeout] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                        case "ondeath":
                                            def.LifecycleEvents[global::FiresRPGmaker.Modules.Quest.QuestLifecycleDispatcher.EventOnDeath] = value.Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                                            break;
                                        case "transfercomplition":   // kg's spelling
                                        case "transfercompletion":   // corrected-spelling alias
                                            ApplyTransferComplitionLine(def, value);
                                            break;
                                    }
                                }
                            }
                            // Merge into in-memory DB, then re-attach this quest's QuestEvents-family
                            // commands (the def is fresh, so the append can't duplicate).
                            questDatabase[def.QuestId] = def;
                            try { MergeQuestEventFiles(def); } catch { }
                            ForgetSerialisedQuestDb();
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
        // Per-objective type from a "Kind:" prefix on the target line (Item→collect, Kill, Craft, Harvest, Build,
        // Move, Talk). Null = fall back to the quest-level QuestType. This is what lets a mixed quest (e.g. a "Talk"
        // quest whose objectives are "Item:" collects) show the correct verb per line instead of one quest verb.
        public string Type { get; set; } = null;

        /// <summary>Map a target-line prefix token to an objective type string (or null if it's not a known kind).</summary>
        public static string NormalizeObjectiveType(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return null;
            switch (prefix.Trim().ToLowerInvariant())
            {
                case "item":    return "collect";   // kg "Item:" = have/collect the item
                case "collect": return "collect";
                case "kill":    return "kill";
                case "craft":   return "craft";
                case "harvest": return "harvest";
                case "build":   return "build";
                case "move":    return "move";
                case "talk":    return "talk";
                default:        return null;         // unknown prefix → not an objective-type marker
            }
        }
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
