using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Modules.Vault;

namespace FiresRPGmaker.Modules.Quest
{
    /// <summary>
    /// KG-parity quest-reward dispatcher. KG's <c>QuestRewardType</c> enum has 12 values; ours uses a
    /// free-form <see cref="QuestReward.Type"/> string. This dispatcher recognises every KG name
    /// (case-insensitive) plus our existing "item" / "skill_exp" aliases and routes the reward to the
    /// right delivery channel (bank, immediate skill grant, companion tame, custom-data write,
    /// mod-bridge log, etc.).
    ///
    /// Delivery channels:
    ///   • BANK — reward is queued in the player's BankItems and shown in the Banker UI ("Item" / "RandomItem")
    ///   • IMMEDIATE — applied to the live player (Skill, Skill_EXP, Pet, SetCustomValue, AddCustomValue)
    ///   • MOD-BRIDGE — stubbed log line until the partner mod ships (EpicMMO/Cozyheim/Battlepass/RustyClasses/Guild)
    /// </summary>
    public static class QuestRewardDispatcher
    {
        /// <summary>Grants one reward. <paramref name="data"/> is the vault row to bank into;
        /// <paramref name="player"/> is the live player for immediate delivery (may be null for offline grants).</summary>
        public static void Grant(PlayerVaultData data, Player player, QuestReward reward)
        {
            if (reward == null) return;
            string type = (reward.Type ?? "item").Trim().ToLowerInvariant();
            try
            {
                switch (type)
                {
                    case "item":
                        BankItem(data, reward.Target, reward.Amount, reward.Level);
                        break;
                    case "randomitem":
                        BankRandomItem(data, reward.Target, reward.Amount, reward.Level);
                        break;
                    case "skill":
                        GrantSkillLevel(player, reward.Target, reward.Amount);
                        break;
                    case "skill_exp":
                        GrantSkillExp(player, reward.Target, reward.Amount);
                        break;
                    case "pet":
                        GrantPet(player, reward.Target);
                        break;
                    case "setcustomvalue":
                        SetCustomValue(player, reward.Target, reward.Amount);
                        break;
                    case "addcustomvalue":
                        AddCustomValue(player, reward.Target, reward.Amount);
                        break;
                    case "guildaddlevel":
                        ModBridgeStub("GuildAddLevel", reward, "Guild leveling system not implemented");
                        break;
                    case "epicmmo_exp":
                        ModBridgeStub("EpicMMO_EXP", reward, "EpicMMO bridge not wired");
                        break;
                    case "cozyheim_exp":
                        ModBridgeStub("Cozyheim_EXP", reward, "Cozyheim bridge not wired");
                        break;
                    case "battlepass_exp":
                        ModBridgeStub("Battlepass_EXP", reward, "Battlepass bridge not wired");
                        break;
                    case "rustyclasses_exp":
                        ModBridgeStub("RustyClasses_EXP", reward, "RustyClasses bridge not wired");
                        break;
                    default:
                        // Unknown reward type — bank as a plain item so the player still gets SOMETHING.
                        Debug.LogWarning("[QuestReward] Unknown reward type '" + reward.Type + "' — falling back to item bank.");
                        BankItem(data, reward.Target, reward.Amount, reward.Level);
                        break;
                }
            }
            catch (Exception ex) { Debug.LogError("[QuestReward] Grant '" + type + "' threw: " + ex.Message); }
        }

        /// <summary>Convenience: grant every reward in a quest.</summary>
        public static void GrantAll(PlayerVaultData data, Player player, IEnumerable<QuestReward> rewards)
        {
            if (rewards == null) return;
            foreach (var r in rewards) Grant(data, player, r);
        }

        // ─── BANK channel ─────────────────────────────────────────────────

        private static void BankItem(PlayerVaultData data, string prefab, int amount, int quality)
        {
            if (data == null || string.IsNullOrEmpty(prefab) || amount <= 0) return;
            data.AddBankItem("item", prefab, amount, Mathf.Max(1, quality), -1f);
        }

        /// <summary>RandomItem: <c>Target</c> is a comma- or semicolon-separated pool of prefab names;
        /// we pick one uniformly at random and bank it (matches KG behaviour).</summary>
        private static void BankRandomItem(PlayerVaultData data, string pool, int amount, int quality)
        {
            if (data == null || string.IsNullOrEmpty(pool) || amount <= 0) return;
            var choices = pool.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (choices.Length == 0) return;
            string pick = choices[UnityEngine.Random.Range(0, choices.Length)].Trim();
            if (string.IsNullOrEmpty(pick)) return;
            data.AddBankItem("item", pick, amount, Mathf.Max(1, quality), -1f);
        }

        // ─── IMMEDIATE channel ────────────────────────────────────────────

        /// <summary>Skill: SET the player's skill level to <c>Amount</c> (KG's "Skill" reward overrides current).</summary>
        private static void GrantSkillLevel(Player player, string skillName, int level)
        {
            if (player == null || string.IsNullOrEmpty(skillName)) return;
            if (!Enum.TryParse(skillName, ignoreCase: true, out Skills.SkillType type))
            { Debug.LogWarning("[QuestReward] Skill type not found: " + skillName); return; }
            var skills = player.GetSkills();
            if (skills == null) return;
            // Skills.Skill is the per-skill record. We force its m_level via the skills dict lookup.
            try
            {
                var data = skills.GetSkill(type);
                if (data != null) data.m_level = Mathf.Clamp(level, 0, 100);
            }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] Skill set failed: " + ex.Message); }
        }

        /// <summary>Skill_EXP: ADD experience to the named skill (KG's standard XP grant).</summary>
        private static void GrantSkillExp(Player player, string skillName, int xp)
        {
            if (player == null || string.IsNullOrEmpty(skillName) || xp <= 0) return;
            if (!Enum.TryParse(skillName, ignoreCase: true, out Skills.SkillType type))
            { Debug.LogWarning("[QuestReward] Skill type not found: " + skillName); return; }
            try { player.RaiseSkill(type, xp); }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] RaiseSkill failed: " + ex.Message); }
        }

        /// <summary>Pet: spawn a Tameable creature at the player's feet, already tamed (KG's "Pet" reward).</summary>
        private static void GrantPet(Player player, string prefab)
        {
            if (player == null || string.IsNullOrEmpty(prefab) || ZNetScene.instance == null) return;
            var pf = ZNetScene.instance.GetPrefab(prefab);
            if (pf == null) { Debug.LogWarning("[QuestReward] Pet prefab missing: " + prefab); return; }
            var spawnPos = player.transform.position + player.transform.forward * 1.5f + Vector3.up * 0.5f;
            var go = UnityEngine.Object.Instantiate(pf, spawnPos, Quaternion.identity);
            var tame = go.GetComponent<Tameable>();
            if (tame != null) tame.Tame();
        }

        /// <summary>SetCustomValue: write <c>Target</c>=<c>Amount</c> to player.m_customData.</summary>
        private static void SetCustomValue(Player player, string key, int value)
        {
            if (player == null || string.IsNullOrEmpty(key)) return;
            try
            {
                player.m_customData[key] = value.ToString();
                RpgMakerApi.FireCustomValue(player.GetPlayerID(), key, value, "set");
            }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] SetCustomValue failed: " + ex.Message); }
        }

        /// <summary>AddCustomValue: increments the int stored at player.m_customData[key] by <c>Amount</c>.</summary>
        private static void AddCustomValue(Player player, string key, int delta)
        {
            if (player == null || string.IsNullOrEmpty(key)) return;
            try
            {
                int current = 0;
                if (player.m_customData.TryGetValue(key, out var raw)) int.TryParse(raw, out current);
                int updated = current + delta;
                player.m_customData[key] = updated.ToString();
                RpgMakerApi.FireCustomValue(player.GetPlayerID(), key, updated, "add");
            }
            catch (Exception ex) { Debug.LogWarning("[QuestReward] AddCustomValue failed: " + ex.Message); }
        }

        // ─── MOD-BRIDGE channel (stubs) ───────────────────────────────────

        private static void ModBridgeStub(string typeName, QuestReward reward, string reason)
        {
            Debug.LogWarning("[QuestReward] " + typeName + " skipped — " + reason
                + ". Target=" + (reward.Target ?? "") + " Amount=" + reward.Amount);
        }
    }
}
