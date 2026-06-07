using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.Modules.Progression;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;

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
                Debug.LogWarning($"[DialogueConditionEngine] Error evaluating '{condition}': {ex.Message}");
                return ConditionResult.Pass();
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
                default:
                    Debug.LogWarning($"[DialogueConditionEngine] Unknown condition type: {typeName}");
                    return ConditionResult.Pass();
            }
        }

        #region Condition Implementations

        private static ConditionResult CheckHasItem(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 3) return ConditionResult.Pass();
            string prefabName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int requiredAmount)) return ConditionResult.Pass();
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
            if (parts.Length < 2) return ConditionResult.Pass();
            string questId = parts[1].Trim();
            long playerId = player.GetPlayerID();
            var qm = QuestManager.Instance;
            if (qm == null) return ConditionResult.Pass();

            bool hasQuest = qm.HasQuest(playerId, questId);
            if (invert)
                return hasQuest ? ConditionResult.Fail($"Already have quest: {questId}") : ConditionResult.Pass();
            return hasQuest ? ConditionResult.Pass() : ConditionResult.Fail($"Quest not taken: {questId}");
        }

        private static ConditionResult CheckQuestFinished(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 2) return ConditionResult.Pass();
            string questId = parts[1].Trim();
            long playerId = player.GetPlayerID();
            var qm = QuestManager.Instance;
            if (qm == null) return ConditionResult.Pass();

            bool completed = qm.HasCompletedQuest(playerId, questId);
            if (invert)
                return completed ? ConditionResult.Fail($"Quest already completed: {questId}") : ConditionResult.Pass();
            return completed ? ConditionResult.Pass() : ConditionResult.Fail($"Must complete quest: {questId}");
        }

        private static ConditionResult CheckQuestProgress(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 2) return ConditionResult.Pass();
            string questId = parts[1].Trim();
            long playerId = player.GetPlayerID();
            var qm = QuestManager.Instance;
            if (qm == null) return ConditionResult.Pass();

            bool hasQuest = qm.HasQuest(playerId, questId);
            if (!hasQuest) return invert ? ConditionResult.Pass() : ConditionResult.Fail($"Quest not active: {questId}");

            // Check if all targets are met for the quest
            var def = qm.GetQuestDefinition(questId);
            if (def == null) return ConditionResult.Pass();

            var vaultData = VaultOfKnowledge.Instance?.GetPlayerData(playerId);
            if (vaultData == null) return ConditionResult.Pass();

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
            if (parts.Length < 2) return ConditionResult.Pass();
            string key = parts[1].Trim();
            bool has = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
            if (invert)
                return has ? ConditionResult.Fail($"Global key must not be set: {key}") : ConditionResult.Pass();
            return has ? ConditionResult.Pass() : ConditionResult.Fail($"Requires global key: {key}");
        }

        private static ConditionResult CheckPlayerKey(Player player, string[] parts, bool invert)
        {
            if (parts.Length < 2) return ConditionResult.Pass();
            string key = parts[1].Trim();
            // Check Valheim's unique keys AND our private keys
            bool has = player.HaveUniqueKey(key) || PrivateKeyManager.HasPrivateKey(key);
            if (invert)
                return has ? ConditionResult.Fail($"Must not have key: {key}") : ConditionResult.Pass();
            return has ? ConditionResult.Pass() : ConditionResult.Fail($"Requires key: {key}");
        }

        private static ConditionResult CheckSkill(Player player, string[] parts, bool moreOrEqual)
        {
            if (parts.Length < 3) return ConditionResult.Pass();
            string skillName = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int level)) return ConditionResult.Pass();

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
            if (parts.Length < 2) return ConditionResult.Pass();
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
            if (parts.Length < 3) return ConditionResult.Pass();
            string key = parts[1].Trim();
            if (!int.TryParse(parts[2].Trim(), out int threshold)) return ConditionResult.Pass();

            int current = 0;
            var nview = player.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
                current = nview.GetZDO().GetInt(key);

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
            if (parts.Length < 2) return ConditionResult.Pass();
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float threshold))
                return ConditionResult.Pass();

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
