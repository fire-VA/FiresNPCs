using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Dialogues;
using FiresRPGmaker.Modules.Vault;

namespace FiresRPGmaker.Modules.Quest
{
    /// <summary>
    /// KG-parity quest lifecycle: when a quest changes state (accepted / cancelled / completed /
    /// timer expired / player died), look at the quest's <see cref="QuestDefinition.LifecycleEvents"/>
    /// dictionary and run every command attached to that event through
    /// <see cref="DialogueCommandEngine.ExecuteAll"/> (same engine dialogue options use).
    ///
    /// Also handles <see cref="QuestDefinition.TransferComplition"/> chain-quest follow-ups: when a
    /// quest auto-completes and TransferComplition is set, the named follow-up quest is auto-accepted
    /// and a MessageHud popup with <c>TransferComplitionText</c> is shown.
    /// </summary>
    public static class QuestLifecycleDispatcher
    {
        public const string EventOnAccept   = "OnAcceptQuest";
        public const string EventOnCancel   = "OnCancelQuest";
        public const string EventOnComplete = "OnCompleteQuest";
        public const string EventOnTimeout  = "OnQuestTimeout";
        public const string EventOnDeath    = "OnDeath";

        /// <summary>Fires the named event for a single quest (executes its attached command strings).
        /// Called from the accept/cancel/complete/timeout/death hooks below.</summary>
        public static void Fire(QuestDefinition def, string eventName)
        {
            if (def == null || def.LifecycleEvents == null) return;
            if (!def.LifecycleEvents.TryGetValue(eventName, out var commands)) return;
            if (commands == null || commands.Count == 0) return;
            try { DialogueCommandEngine.ExecuteAll(commands.ToArray()); }
            catch (Exception ex) { Debug.LogWarning("[QuestLifecycle] " + eventName + " for quest '" + def.QuestId + "' threw: " + ex.Message); }
        }

        /// <summary>Convenience: called when a quest auto-completes. Runs OnCompleteQuest, then triggers
        /// the chain-quest follow-up (if TransferComplition is set on the definition).</summary>
        public static void OnCompleted(QuestDefinition def, PlayerVaultData data, Player player)
        {
            if (def == null) return;
            Fire(def, EventOnComplete);
            if (player != null) RpgMakerApi.FireQuestCompleted(player.GetPlayerID(), def.QuestId);
            // kg Quest webhook ("**{0}** finished quest **{1}**") — server-side only, so the link and
            // the post never depend on which client completed it.
            try
            {
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    string who = player != null ? player.GetPlayerName() : null;
                    if (!string.IsNullOrEmpty(who))
                        global::FiresRPGmaker.Modules.Marketplace.DiscordWebhooks.PostQuestFinished(who, def.Name ?? def.QuestId);
                }
            }
            catch { }
            // Chain-quest follow-up
            if (string.IsNullOrEmpty(def.TransferComplition)) return;
            var next = QuestManager.Instance?.GetQuestDefinition(def.TransferComplition);
            if (next == null)
            {
                Debug.LogWarning("[QuestLifecycle] TransferComplition target '" + def.TransferComplition + "' not found in quest registry");
                return;
            }
            // kg-parity: chain follow-up is a client-local per-quest accept (idempotent).
            if (!global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.HasQuest(next.QuestId))
            {
                global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.Accept(next.QuestId, next);
                Fire(next, EventOnAccept); // chain-accept counts as an Accept for the follow-up
            }
            if (!string.IsNullOrEmpty(def.TransferComplitionText))
                try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, def.TransferComplitionText); } catch { }
        }

        /// <summary>Fire OnDeath for every active quest. Hooked from Player.OnDeath.</summary>
        public static void FireOnDeathForActiveQuests(PlayerVaultData data)
        {
            if (data == null || QuestManager.Instance == null) return;
            foreach (var active in data.ActiveQuests)
            {
                var def = QuestManager.Instance.GetQuestDefinition(active.QuestId);
                if (def != null) Fire(def, EventOnDeath);
            }
        }
    }

    /// <summary>Lifecycle events fired from the actual gameplay sites.</summary>
    public static class QuestLifecyclePatches
    {
        /// <summary>OnDeath — fire OnDeath for every active quest the player has, pulled from the vault. Called
        /// from <see cref="Modules.RpgVanillaHooks.Player_OnDeath"/> (postfix).</summary>
        internal static void OnLocalPlayerDeath(Player __instance)
        {
            if (__instance != Player.m_localPlayer || VaultOfKnowledge.Instance == null) return;
            try
            {
                long pid = __instance.GetPlayerID();
                var data = VaultOfKnowledge.Instance.GetPlayerData(pid);
                if (data == null) return;
                QuestLifecycleDispatcher.FireOnDeathForActiveQuests(data);
            }
            catch (Exception ex) { Debug.LogWarning("[QuestLifecycle] OnDeath failed: " + ex.Message); }
        }
    }
}
