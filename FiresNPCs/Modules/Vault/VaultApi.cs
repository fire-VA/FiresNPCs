using System;
using FiresRPGmaker.Modules.Quest;

namespace FiresRPGmaker.Modules.Vault
{
    /// <summary>
    /// Lightweight external API for interacting with Vault data. Intended for other mods/tools.
    /// Writes are server-only; reads fallback to cache/disk.
    /// </summary>
    public static class VaultApi
    {
        /// <summary>Get authoritative player vault data (server: live; client: cached/offline).</summary>
        public static PlayerVaultData GetPlayerVault(long playerId)
        {
            return VaultOfKnowledge.Instance?.GetPlayerData(playerId) ?? new PlayerVaultData(playerId);
        }

        /// <summary>Add a bank item for a player. Server-only; silently no-ops on client.</summary>
        public static bool AddBankItem(long playerId, StoredBankItem item)
        {
            if (item == null || playerId == 0) return false;
            if (ZNet.instance != null && !ZNet.instance.IsServer()) return false;
            var data = GetPlayerVault(playerId);
            data.AddBankItem(item.Type, item.Prefab, item.Amount, item.Quality, item.Durability);
            data.MergeBankItems();
            Persist(playerId, data);
            return true;
        }

        /// <summary>Take a bank item (amount) from a player. Server-only; returns removed item or null.</summary>
        public static StoredBankItem TakeBankItem(long playerId, string type, string prefab, int amount, int quality = 1)
        {
            if (playerId == 0 || string.IsNullOrEmpty(type) || string.IsNullOrEmpty(prefab) || amount <= 0) return null;
            if (ZNet.instance != null && !ZNet.instance.IsServer()) return null;
            var data = GetPlayerVault(playerId);
            var taken = data.TakeBankItem(type, prefab, amount, quality);
            if (taken != null)
            {
                data.MergeBankItems();
                Persist(playerId, data);
            }
            return taken;
        }

        /// <summary>Increment a stat for a player. Server-only.</summary>
        public static bool IncrementStat(long playerId, string key, int delta)
        {
            if (playerId == 0 || string.IsNullOrEmpty(key) || delta == 0) return false;
            if (ZNet.instance != null && !ZNet.instance.IsServer()) return false;
            var data = GetPlayerVault(playerId);
            if (!data.Stats.ContainsKey(key)) data.Stats[key] = 0;
            data.Stats[key] += delta;
            Persist(playerId, data);
            return true;
        }

        /// <summary>Check if a quest is on cooldown for a player.</summary>
        public static bool IsQuestOnCooldown(long playerId, string questId, out DateTime availableAt)
        {
            availableAt = DateTime.MinValue;
            if (string.IsNullOrEmpty(questId) || playerId == 0) return false;
            var def = QuestManager.Instance?.GetQuestDefinition(questId);
            if (def == null || def.CooldownDays <= 0) return false;
            var data = GetPlayerVault(playerId);
            if (data.CompletedQuests != null && data.CompletedQuests.TryGetValue(questId, out var entry))
            {
                return ChroniclesOfTime.IsQuestOnCooldown(entry, def.CooldownDays, out availableAt);
            }
            return false;
        }

        private static void Persist(long playerId, PlayerVaultData data)
        {
            if (VaultOfKnowledge.Instance == null) return;
            data.IsDirty = true;
            var player = Player.GetPlayer(playerId);
            if (player != null)
            {
                VaultOfKnowledge.Instance.SerializeToCustomData(player, data);
                VaultOfKnowledge.Instance.SavePlayerDataFromZDO(playerId, player);
            }
            else
            {
                VaultOfKnowledge.Instance.SavePlayerData(playerId, data);
            }
        }
    }
}
