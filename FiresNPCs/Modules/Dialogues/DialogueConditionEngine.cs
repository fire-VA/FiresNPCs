using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.Modules.Progression;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Modules.Marketplace;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Evaluates dialogue option conditions at runtime.
    /// 
    /// Condition format: "ConditionType,Param1,Param2,..."
    /// Supports both comma and colon as param separators.
    /// Prefix with ! to negate any condition.
    /// 
    /// Multiple conditions on an option use AND logic between groups, OR within each group.
    /// See <see cref="RawPlayerOption.Conditions"/> for the grouping structure.
    /// </summary>
    public static class DialogueConditionEngine
    {
        /// <summary>
        /// Result of evaluating a single condition.
        /// </summary>
        public struct ConditionResult
        {
            public bool Passed;
            public string Reason;

            public static ConditionResult Pass() => new ConditionResult { Passed = true, Reason = string.Empty };
            public static ConditionResult Fail(string reason) => new ConditionResult { Passed = false, Reason = reason };
        }

        /// <summary>
        /// Evaluates all condition groups for an option.
        /// Groups are AND'd together; conditions within a group are OR'd.
        /// </summary>
        // 0.0.532 (Fire 10-08): conditions fail CLOSED. An unknown type, a missing or unreadable value, an unsupported
        // check, or one that throws used to PASS, so a single typo un-gated the option. Each distinct problem is
        // logged once, naming the condition line, and the option stays locked.
        private static readonly HashSet<string> s_warnedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static ConditionResult Malformed(string line, string problem)
        {
            if (s_warnedConditions.Add(problem + "|" + line))
                Debug.LogWarning($"[DialogueConditionEngine] Condition '{line}': {problem}; the option stays locked.");
            return ConditionResult.Fail("Requirements not met.");
        }
        public static bool EvaluateAll(List<string[]> conditionGroups, out string reason)
        {
            reason = string.Empty;
            if (conditionGroups == null || conditionGroups.Count == 0) return true;

            foreach (var group in conditionGroups)
            {
                if (group == null || group.Length == 0) continue;

                if (group.Length == 1)
                {
                    var result = EvaluateSingle(group[0]);
                    if (!result.Passed)
                    {
                        reason = result.Reason;
                        return false;
                    }
                }
                else
                {
                    bool anyPassed = false;
                    string lastReason = string.Empty;
                    foreach (var condition in group)
                    {
                        var result = EvaluateSingle(condition);
                        if (result.Passed) { anyPassed = true; break; }
                        lastReason = result.Reason;
                    }
                    if (!anyPassed)
                    {
                        reason = lastReason;
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Evaluates a single condition string like "HasItem,SwordBronze,1" or "!HasQuest,my_quest".
        /// </summary>
        public static ConditionResult EvaluateSingle(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return ConditionResult.Pass();

            try
            {
                bool negate = false;
                string trimmed = condition.Trim();
                if (trimmed.StartsWith("!"))
                {
                    negate = true;
                    trimmed = trimmed.Substring(1);
                }

                string[] parts = trimmed.Replace(":", ",").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return ConditionResult.Pass();

                string typeName = ResolveLegacyAlias(parts[0].Trim());
                var result = DispatchCondition(typeName, parts);

                if (negate)
                {
                    result.Passed = !result.Passed;
                    if (!result.Passed && string.IsNullOrEmpty(result.Reason))
                        result.Reason = $"Condition failed: !{trimmed}";
                }

                return result;
            }
            catch (Exception ex)
            {
                return Malformed(condition, "the check threw: " + ex.Message);
            }
        }

        private static string ResolveLegacyAlias(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "otherquest": return "QuestFinished";
                case "notfinished": return "QuestNotFinished";
                case "skill": return "SkillMore";
                default: return name;
            }
        }

        private static ConditionResult DispatchCondition(string typeName, string[] parts)
        {
            var player = Player.m_localPlayer;
            if (player == null)
                return ConditionResult.Fail("No local player");

            switch (typeName.ToLowerInvariant())
            {
                case "hasitem": return CheckHasItem(player, parts, false);
                case "nothasitem": return CheckHasItem(player, parts, true);
                case "hasquest": return CheckHasQuest(player, parts, false);
                case "nothasquest": return CheckHasQuest(player, parts, true);
                case "questfinished": return CheckQuestFinished(player, parts, false);
                case "questnotfinished": return CheckQuestFinished(player, parts, true);
                case "questprogressdone": return CheckQuestProgress(player, parts, false);
                case "questprogressnotdone": return CheckQuestProgress(player, parts, true);
                case "globalkey": return CheckGlobalKey(parts, false);
                case "notglobalkey": return CheckGlobalKey(parts, true);
                case "hasplayerkey": return CheckPlayerKey(player, parts, false);
                case "nothasplayerkey": return CheckPlayerKey(player, parts, true);
                case "skillmore": return CheckSkill(player, parts, true);
                case "skillless": return CheckSkill(player, parts, false);
                case "hasbuff": return CheckBuff(player, parts, false);
                case "nothasbuff": return CheckBuff(player, parts, true);
                case "customplayerkeymore": return CheckCustomPlayerKey(player, parts, true);
                case "customplayerkeyless": return CheckCustomPlayerKey(player, parts, false);
                // Legacy aliases
                case "customvaluemore": return CheckCustomPlayerKey(player, parts, true);
                case "customvalueless": return CheckCustomPlayerKey(player, parts, false);
                case "healthmore": return CheckStat(player.GetHealth(), parts, true, "Health");
                case "healthless": return CheckStat(player.GetHealth(), parts, false, "Health");
                case "maxhealthmore": return CheckStat(player.GetMaxHealth(), parts, true, "Max Health");
                case "maxhealthless": return CheckStat(player.GetMaxHealth(), parts, false, "Max Health");
                case "staminamore": return CheckStat(player.GetStamina(), parts, true, "Stamina");
                case "staminaless": return CheckStat(player.GetStamina(), parts, false, "Stamina");
                case "maxstaminamore": return CheckStat(player.GetMaxStamina(), parts, true, "Max Stamina");
                case "maxstaminaless": return CheckStat(player.GetMaxStamina(), parts, false, "Max Stamina");
                case "eitrmore": return CheckStat(player.GetEitr(), parts, true, "Eitr");
                case "eitrless": return CheckStat(player.GetEitr(), parts, false, "Eitr");
                case "maxeitrmore": return CheckStat(player.GetMaxEitr(), parts, true, "Max Eitr");
                case "maxeitrless": return CheckStat(player.GetMaxEitr(), parts, false, "Max Eitr");
                case "none": return ConditionResult.Pass();
                case "unreadabledialoguepart": return ConditionResult.Fail("Requirements not met."); // DialogueCodex.LockUnreadable warned at load
                // ── KG-parity conditions added 2026-06-08 ──
                case "hasachievement":              return CheckHasAchievement(parts, false);
                case "nothasachievement":           return CheckHasAchievement(parts, true);
                case "hasachievementscore":         return CheckHasAchievementScore(parts, false);
                case "nothasachievementscore":      return CheckHasAchievementScore(parts, true);
                case "hasguild":                    return CheckHasGuild(player, parts, false);
                case "nothasguild":                 return CheckHasGuild(player, parts, true);
                case "hasguildwithname":            return CheckHasGuildWithName(player, parts, false);
                case "nothasguildwithname":         return CheckHasGuildWithName(player, parts, true);
                case "guildlevelmore":              return CheckGuildLevel(player, parts, true);
                case "guildlevelless":              return CheckGuildLevel(player, parts, false);
                case "guildhasachievement":         return CheckGuildHasAchievement(player, parts, false);
                case "notguildhasachievement":      return CheckGuildHasAchievement(player, parts, true);
                case "isvip":                       return CheckIsVIP(player, false);
                case "notisvip":                    return CheckIsVIP(player, true);
                case "npcmodelequals":              return CheckNpcModelEquals(parts, false);
                case "notnpcmodelequals":           return CheckNpcModelEquals(parts, true);
                case "npcnameequals":               return CheckNpcNameEquals(parts, false);
                case "notnpcnameequals":            return CheckNpcNameEquals(parts, true);
                case "playerhasallcustomdatakeys":  return CheckCustomDataKeys(player, parts, requireAll: true,  invert: false);
                case "notplayerhasallcustomdatakeys": return CheckCustomDataKeys(player, parts, requireAll: true,  invert: true);
                case "playerhasoneofcustomdatakeys": return CheckCustomDataKeys(player, parts, requireAll: false, invert: false);
                case "notplayerhasoneofcustomdatakeys": return CheckCustomDataKeys(player, parts, requireAll: false, invert: true);
                case "modinstalled":                return CheckModInstalled(parts, false);
                case "notmodinstalled":             return CheckModInstalled(parts, true);
                case "irongatestatmore":            return CheckIronGateStat(parts, true);
                case "irongatestatless":            return CheckIronGateStat(parts, false);
                case "hasfaction":                  return CheckHasFaction(parts, false);
                case "nothasfaction":               return CheckHasFaction(parts, true);
                case "hasanyfaction":               return CheckHasAnyFaction(false);
                case "nothasanyfaction":            return CheckHasAnyFaction(true);
                default:
                    return Malformed(string.Join(",", parts), $"unknown condition type '{typeName}'");
            }
        }

        // ─── KG-parity condition implementations ─────────────────────────────

        /// <summary>Snapshot of the currently-talked-to NPC. Set by DialogueUI on Show; cleared on Hide.
        /// Used by NPCModelEquals / NPCNameEquals which have no other NPC handle from the parts array.</summary>
        public static string CurrentNpcName;
        public static string CurrentNpcModel;

        /// <summary>Walks every loaded achievement profile and yields each definition once.</summary>
        private static IEnumerable<AchievementDef> EnumerateAllAchievements()
        {
            foreach (var profileName in AchievementProfileManager.GetLoadedProfileNames())
                foreach (var def in AchievementProfileManager.GetAchievements(profileName))
                    yield return def;
        }

        private static ConditionResult CheckHasAchievement(string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string achId = parts[1].Trim();
            try
            {
                string playerName = Player.m_localPlayer?.GetPlayerName() ?? "Local";
                foreach (var def in EnumerateAllAchievements())
                {
                    if (!string.Equals(def.Name, achId, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(def.Id.ToString(), achId, StringComparison.OrdinalIgnoreCase)) continue;
                    int progress = LeaderboardTracker.GetProgress(playerName, def.TriggerType, def.Prefab);
                    bool earned = progress >= def.MinAmount;
                    return earned != invert ? ConditionResult.Pass() : ConditionResult.Fail($"Achievement required: {def.Name}");
                }
            }
            catch { }
            return invert ? ConditionResult.Pass() : ConditionResult.Fail($"Achievement not found: {achId}");
        }

        private static ConditionResult CheckHasAchievementScore(string[] parts, bool invert)
        {
            if (parts.Length < 2 || !int.TryParse(parts[1].Trim(), out int needed)) return Malformed(string.Join(",", parts), "needs a whole number");
            try
            {
                string playerName = Player.m_localPlayer?.GetPlayerName() ?? "Local";
                int total = 0;
                foreach (var def in EnumerateAllAchievements())
                {
                    int progress = LeaderboardTracker.GetProgress(playerName, def.TriggerType, def.Prefab);
                    if (progress >= def.MinAmount) total += def.Score;
                }
                bool ok = total >= needed;
                return ok != invert ? ConditionResult.Pass() : ConditionResult.Fail($"Need {needed} achievement score (have {total})");
            }
            catch (Exception ex) { return Malformed(string.Join(",", parts), "the check threw: " + ex.Message); }
        }

        private static ConditionResult CheckHasGuild(Player player, string[] parts, bool invert)
        {
            try
            {
                bool hasGuild = FiresCore.Bridge.GuildBridge.LocalGuildName() != null;
                return hasGuild != invert ? ConditionResult.Pass() : ConditionResult.Fail(invert ? "Must not be in a guild" : "Must be in a guild");
            }
            catch (Exception ex) { return Malformed(string.Join(",", parts), "the check threw: " + ex.Message); }
        }

        private static ConditionResult CheckHasGuildWithName(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string target = parts[1].Trim();
            try
            {
                string guildName = FiresCore.Bridge.GuildBridge.LocalGuildName();
                bool match = guildName != null && string.Equals(guildName, target, StringComparison.OrdinalIgnoreCase);
                return match != invert ? ConditionResult.Pass() : ConditionResult.Fail($"Need to be in guild '{target}'");
            }
            catch (Exception ex) { return Malformed(string.Join(",", parts), "the check threw: " + ex.Message); }
        }

        private static ConditionResult CheckGuildLevel(Player player, string[] parts, bool more)
        {
            // Guild levels don't exist (FiresGuilds has none); 0.0.532 fails closed instead of passing.
            return Malformed(string.Join(",", parts), "guild levels aren't supported");
        }

        private static ConditionResult CheckGuildHasAchievement(Player player, string[] parts, bool invert)
        {
            // Guild-level achievements not yet ported (KG had GuildAchievement_DataTypes). Pass through
            // as no-op so dialogue isn't blocked; revisit when the guild-achievement module lands.
            return invert ? ConditionResult.Pass() : Malformed(string.Join(",", parts), "guild achievements aren't supported");
        }

        private static ConditionResult CheckIsVIP(Player player, bool invert)
        {
            // The real VIP list (MarketPlace.cfg VIPplayersList, synced) — admins are also treated as
            // VIP so a server owner testing a VIP branch always sees it.
            bool isVip = false;
            try { isVip = global::FiresRPGmaker.Modules.Marketplace.VipManager.IsLocalPlayerVip(); } catch { }
            if (!isVip)
            {
                try { isVip = global::FiresRPGmaker.FiresRPGmaker.Instance?.configSync?.IsAdmin == true; } catch { }
                if (ZNet.instance != null && ZNet.instance.IsServer()) isVip = true;
            }
            return isVip != invert ? ConditionResult.Pass() : ConditionResult.Fail(invert ? "Cannot be VIP" : "VIP required");
        }

        private static ConditionResult CheckNpcModelEquals(string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            if (string.IsNullOrEmpty(CurrentNpcModel)) return ConditionResult.Pass();
            string target = parts[1].Trim();
            bool match = string.Equals(CurrentNpcModel, target, StringComparison.OrdinalIgnoreCase);
            return match != invert ? ConditionResult.Pass() : ConditionResult.Fail($"NPC model must {(invert ? "not " : "")}be '{target}'");
        }

        private static ConditionResult CheckNpcNameEquals(string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            if (string.IsNullOrEmpty(CurrentNpcName)) return ConditionResult.Pass();
            string target = parts[1].Trim();
            bool match = string.Equals(CurrentNpcName, target, StringComparison.OrdinalIgnoreCase);
            return match != invert ? ConditionResult.Pass() : ConditionResult.Fail($"NPC name must {(invert ? "not " : "")}be '{target}'");
        }

        private static ConditionResult CheckCustomDataKeys(Player player, string[] parts, bool requireAll, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            if (player == null) return ConditionResult.Fail("No local player");

            // MP semantics first (drop-in): parts[1] names a CustomSpawnData blob whose entries are
            // compared against the player's ZDO; parts[2] is the failure reason shown to the player.
            if (CustomSpawnDataStore.TryGet(parts[1].Trim().ToLowerInvariant(), out var blob))
            {
                var view = player.GetComponent<ZNetView>();
                var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
                bool matches = zdo != null && blob.MatchesZdo(zdo, requireAll);
                string why = parts.Length > 2 ? parts[2].Trim() : $"Requires player data '{parts[1].Trim()}'";
                return matches != invert ? ConditionResult.Pass() : ConditionResult.Fail(why);
            }

            // Fires fallback: a plain key list checked for presence in m_customData.
            var keys = new List<string>();
            for (int i = 1; i < parts.Length; i++)
                keys.AddRange(parts[i].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
            if (keys.Count == 0) return Malformed(string.Join(",", parts), "needs at least one key");

            int hits = 0;
            foreach (var k in keys)
            {
                string trimmed = k.Trim();
                if (player.m_customData != null && player.m_customData.ContainsKey(trimmed)) hits++;
            }
            bool ok = requireAll ? hits == keys.Count : hits > 0;
            return ok != invert ? ConditionResult.Pass() : ConditionResult.Fail($"Custom-data keys {(requireAll ? "(all)" : "(one of)")}: {string.Join(", ", keys)}");
        }

        private static ConditionResult CheckModInstalled(string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string guid = parts[1].Trim();
            bool installed = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(guid);
            return installed != invert ? ConditionResult.Pass() : ConditionResult.Fail($"Mod {(invert ? "must not be" : "required")}: {guid}");
        }

        private static ConditionResult CheckHasFaction(string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string want = parts[1].Trim();
            string have = global::FiresRPGmaker.Modules.Factions.FactionMembership.LocalFactionId();
            bool match = have != null && string.Equals(have, want, StringComparison.OrdinalIgnoreCase);
            var f = global::FiresRPGmaker.Modules.Factions.FactionManager.Get(want);
            string label = f != null ? f.Name : want;
            return match != invert ? ConditionResult.Pass()
                : ConditionResult.Fail(invert ? $"Must not be in {label}" : $"Requires faction: {label}");
        }

        private static ConditionResult CheckHasAnyFaction(bool invert)
        {
            bool any = global::FiresRPGmaker.Modules.Factions.FactionMembership.LocalFaction() != null;
            return any != invert ? ConditionResult.Pass()
                : ConditionResult.Fail(invert ? "Must not be in a faction" : "Must be in a faction");
        }

        private static ConditionResult CheckIronGateStat(string[] parts, bool more)
        {
            // Vanilla IronGate stats are tracked on the active PlayerProfile.m_playerStats dictionary.
            if (parts.Length < 3) return Malformed(string.Join(",", parts), "needs a stat name and a number");
            if (Game.instance == null) return ConditionResult.Fail("No game");
            string statKey = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int threshold)) return Malformed(string.Join(",", parts), "needs a whole number");
            int value = 0;
            try
            {
                if (System.Enum.TryParse(statKey, ignoreCase: true, out PlayerStatType parsed))
                {
                    var profile = Game.instance.GetPlayerProfile();
                    if (profile != null && profile.m_playerStats != null)
                    {
                        // Valheim 1.0 split PlayerProfile.m_playerStats from ONE PlayerStats into a
                        // PlayerStats[10] bucketed by world difficulty. Pre-1.0 this read the single
                        // bucket, so sum every bucket - otherwise "has the player done X" silently
                        // reads 0 for anyone who earned it on a difficulty other than the one we pick.
                        float total = 0f;
                        foreach (var bucket in profile.m_playerStats)
                            if (bucket != null && bucket.m_stats != null && bucket.m_stats.TryGetValue(parsed, out var v))
                                total += v;
                        value = (int)total;
                    }
                }
            }
            catch { }
            bool ok = more ? value >= threshold : value <= threshold;
            return ok ? ConditionResult.Pass() : ConditionResult.Fail($"Stat {statKey} must {(more ? ">=" : "<=")} {threshold} (have {value})");
        }

        #region Condition Implementations

        private static ConditionResult CheckHasItem(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 3) return Malformed(string.Join(",", parts), "needs two values");
            string prefabName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int requiredAmount)) return Malformed(string.Join(",", parts), "needs a whole number");
            int level = parts.Length > 3 && int.TryParse(parts[3].Trim(), out int l) ? l : 0;

            string displayName = prefabName;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            if (prefab != null)
            {
                var drop = prefab.GetComponent<ItemDrop>();
                if (drop != null)
                    displayName = Localization.instance.Localize(drop.m_itemData.m_shared.m_name);
            }

            int count = CountPlayerItems(player, prefabName, level);
            bool has = count >= requiredAmount;

            if (invert)
                return has ? ConditionResult.Fail($"Must not have: {displayName} x{requiredAmount}") : ConditionResult.Pass();
            return has ? ConditionResult.Pass() : ConditionResult.Fail($"Requires: {displayName} x{requiredAmount}");
        }

        private static int CountPlayerItems(Player player, string prefabName, int minLevel)
        {
            var inv = player.GetInventory();
            if (inv == null) return 0;
            int count = 0;
            foreach (var item in inv.GetAllItems())
            {
                bool match = false;
                if (item.m_dropPrefab != null && item.m_dropPrefab.name == prefabName)
                    match = true;
                else if (item.m_shared.m_name == prefabName || item.m_shared.m_name == "$" + prefabName)
                    match = true;

                if (match && (minLevel <= 0 || item.m_quality >= minLevel))
                    count += item.m_stack;
            }
            return count;
        }

        private static ConditionResult CheckHasQuest(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string questId = parts[1].Trim();
            long playerId = player.GetPlayerID();
            var qm = QuestManager.Instance;
            if (qm == null) return ConditionResult.Fail("Quests aren't loaded yet");

            bool hasQuest = qm.HasQuest(playerId, questId);
            if (invert)
                return hasQuest ? ConditionResult.Fail($"Already have quest: {questId}") : ConditionResult.Pass();
            return hasQuest ? ConditionResult.Pass() : ConditionResult.Fail($"Quest not taken: {questId}");
        }

        private static ConditionResult CheckQuestFinished(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string questId = parts[1].Trim();
            long playerId = player.GetPlayerID();
            var qm = QuestManager.Instance;
            if (qm == null) return ConditionResult.Fail("Quests aren't loaded yet");

            bool completed = qm.HasCompletedQuest(playerId, questId);
            if (invert)
                return completed ? ConditionResult.Fail($"Quest already completed: {questId}") : ConditionResult.Pass();
            return completed ? ConditionResult.Pass() : ConditionResult.Fail($"Must complete quest: {questId}");
        }

        private static ConditionResult CheckQuestProgress(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string questId = parts[1].Trim();
            long playerId = player.GetPlayerID();
            var qm = QuestManager.Instance;
            if (qm == null) return ConditionResult.Fail("Quests aren't loaded yet");

            bool hasQuest = qm.HasQuest(playerId, questId);
            if (!hasQuest) return invert ? ConditionResult.Pass() : ConditionResult.Fail($"Quest not active: {questId}");

            // Check if all targets are met for the quest
            var def = qm.GetQuestDefinition(questId);
            if (def == null) return Malformed(string.Join(",", parts), "unknown quest");

            var vaultData = VaultOfKnowledge.Instance?.GetPlayerData(playerId);
            if (vaultData == null) return ConditionResult.Fail("No quest data yet");

            var activeEntry = vaultData.ActiveQuests?.FirstOrDefault(q =>
                string.Equals(q.QuestId, questId, StringComparison.OrdinalIgnoreCase));
            if (activeEntry == null) return invert ? ConditionResult.Pass() : ConditionResult.Fail($"Quest not active: {questId}");

            bool allDone = true;
            if (def.QuestTargets != null)
            {
                foreach (var target in def.QuestTargets)
                {
                    if (activeEntry.Progress < target.Amount)
                    {
                        allDone = false;
                        break;
                    }
                }
            }

            if (invert)
                return allDone ? ConditionResult.Fail($"Quest objectives already done: {questId}") : ConditionResult.Pass();
            return allDone ? ConditionResult.Pass() : ConditionResult.Fail($"Quest objectives not yet done: {questId}");
        }

        private static ConditionResult CheckGlobalKey(string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string key = parts[1].Trim();
            bool has = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
            if (invert)
                return has ? ConditionResult.Fail($"Global key must not be set: {key}") : ConditionResult.Pass();
            return has ? ConditionResult.Pass() : ConditionResult.Fail($"Requires global key: {key}");
        }

        private static ConditionResult CheckPlayerKey(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string key = parts[1].Trim();
            // Check Valheim's unique keys AND our private keys
            bool has = player.HaveUniqueKey(key) || PrivateKeyManager.HasPrivateKey(key);
            if (invert)
                return has ? ConditionResult.Fail($"Must not have key: {key}") : ConditionResult.Pass();
            return has ? ConditionResult.Pass() : ConditionResult.Fail($"Requires key: {key}");
        }

        private static ConditionResult CheckSkill(Player player, string[] parts, bool moreOrEqual)
        {
            if (parts.Length < 3) return Malformed(string.Join(",", parts), "needs two values");
            string skillName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int level)) return Malformed(string.Join(",", parts), "needs a whole number");

            float playerLevel = GetSkillLevel(player, skillName);
            string locSkill = Localization.instance.Localize("$skill_" + skillName.ToLowerInvariant());
            if (locSkill.StartsWith("$")) locSkill = skillName;

            if (moreOrEqual)
                return playerLevel >= level
                    ? ConditionResult.Pass()
                    : ConditionResult.Fail($"Requires {locSkill} level {level} (current: {(int)playerLevel})");
            return playerLevel < level
                ? ConditionResult.Pass()
                : ConditionResult.Fail($"{locSkill} level must be below {level} (current: {(int)playerLevel})");
        }

        private static float GetSkillLevel(Player player, string skillName)
        {
            if (Enum.TryParse(skillName, true, out Skills.SkillType skillType))
                return player.GetSkillLevel(skillType);
            try
            {
                int hash = skillName.GetStableHashCode();
                return player.GetSkillLevel((Skills.SkillType)hash);
            }
            catch { return 0f; }
        }

        private static ConditionResult CheckBuff(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            string buffName = parts[1].Trim();
            bool has = player.GetSEMan().HaveStatusEffect(buffName.GetStableHashCode());

            string displayName = buffName;
            if (ObjectDB.instance != null && ObjectDB.instance.m_StatusEffects != null)
            {
                var se = ObjectDB.instance.m_StatusEffects.Find(s => s.name == buffName);
                if (se != null) displayName = Localization.instance.Localize(se.m_name);
            }

            if (invert)
                return has ? ConditionResult.Fail($"Must not have effect: {displayName}") : ConditionResult.Pass();
            return has ? ConditionResult.Pass() : ConditionResult.Fail($"Requires effect: {displayName}");
        }

        private static ConditionResult CheckCustomPlayerKey(Player player, string[] parts, bool moreOrEqual)
        {
            if (parts.Length < 3) return Malformed(string.Join(",", parts), "needs two values");
            string key = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int threshold)) return Malformed(string.Join(",", parts), "needs a whole number");

            int current = 0;
            var nview = player.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
                current = nview.GetZDO().GetInt(key);
            // MP characters carry their values in m_customData ("kgMarketplaceValue@<key>") — the
            // AddCustomValue/SetCustomValue verbs write there too. Honor whichever store is higher.
            current = Mathf.Max(current, DialogueCommandEngine.GetMpCustomValue(player, key));

            if (moreOrEqual)
                return current >= threshold
                    ? ConditionResult.Pass()
                    : ConditionResult.Fail($"Requires {key} \u2265 {threshold} (current: {current})");
            return current < threshold
                ? ConditionResult.Pass()
                : ConditionResult.Fail($"Requires {key} < {threshold} (current: {current})");
        }

        private static ConditionResult CheckStat(float currentValue, string[] parts, bool moreOrEqual, string statName)
        {
            if (parts.Length < 2) return Malformed(string.Join(",", parts), "needs a value");
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float threshold))
                return Malformed(string.Join(",", parts), "needs a number");

            if (moreOrEqual)
                return currentValue >= threshold
                    ? ConditionResult.Pass()
                    : ConditionResult.Fail($"{statName} must be \u2265 {threshold} (current: {(int)currentValue})");
            return currentValue < threshold
                ? ConditionResult.Pass()
                : ConditionResult.Fail($"{statName} must be < {threshold} (current: {(int)currentValue})");
        }

        #endregion
    }
}
