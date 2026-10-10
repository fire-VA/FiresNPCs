using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Vault;

namespace FiresRPGmaker.Modules.Quest
{
    /// <summary>
    /// KG parity: adds the 6 quest types beyond Kill (Collect, Harvest, Craft, Build, Move, Talk).
    /// Each Harmony patch detects the gameplay event, then calls
    /// <see cref="QuestProgressClient.SendProgress"/> which either sends the new
    /// FiresRPGmaker_QuestProgressEvent RPC (dedicated server) or does an inline local increment
    /// (single-player / listen server). Kill stays in QuestProgressPatches.cs unchanged.
    /// </summary>
    internal static class QuestProgressPatches_NewTypes
    {
        /// <summary>"Collect" — KG fires this on item-add-to-inventory. We hook Player.OnInventoryChanged.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.OnInventoryChanged))]
        public static class Player_OnInventoryChanged_Patch
        {
            private static int _lastSeenSlots;
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || __instance.GetInventory() == null) return;
                // Cheap dedup: only fire when the inventory actually gained items (avoid OnInventoryChanged
                // spam from equip/unequip/move).
                int n = 0;
                foreach (var it in __instance.GetInventory().GetAllItems())
                    if (it != null && it.m_dropPrefab != null) n += it.m_stack;
                if (n <= _lastSeenSlots) { _lastSeenSlots = n; return; }
                int delta = n - _lastSeenSlots;
                _lastSeenSlots = n;
                // We don't know WHICH item was added without diffing; KG calls TryAddRewardPickup
                // without prefab specificity, so we send a wildcard event and let the server match
                // any "Collect" active quest whose target matches the latest-added stack.
                var latest = FindLatestAddedPrefab(__instance);
                if (!string.IsNullOrEmpty(latest))
                    QuestProgressClient.SendProgress("Collect", latest, Mathf.Max(1, delta));
            }

            private static string FindLatestAddedPrefab(Player p)
            {
                var all = p.GetInventory().GetAllItems();
                if (all == null || all.Count == 0) return null;
                // Use the most-recently-added stack (Valheim appends to the list).
                var last = all[all.Count - 1];
                return last?.m_dropPrefab?.name;
            }
        }

        /// <summary>"Harvest" — Pickable.RPC_Pick fires when berries/mushrooms/flowers are picked.</summary>
        [HarmonyPatch(typeof(Pickable), "RPC_Pick")]
        public static class Pickable_RPC_Pick_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(Pickable __instance)
            {
                if (__instance == null || __instance.m_itemPrefab == null) return;
                // RPC fires on every peer; only count for the local player who actually picked it.
                if (Player.m_localPlayer == null) return;
                string prefab = __instance.m_itemPrefab.gameObject?.name;
                if (string.IsNullOrEmpty(prefab)) return;
                QuestProgressClient.SendProgress("Harvest", prefab, 1);
            }
        }

        /// <summary>"Craft" — InventoryGui.DoCrafting finalizes a craft action.</summary>
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        public static class InventoryGui_DoCrafting_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(InventoryGui __instance, Player player)
            {
                if (player != Player.m_localPlayer) return;
                Recipe recipe = null;
                try
                {
                    var f = AccessTools.Field(typeof(InventoryGui), "m_craftRecipe");
                    recipe = f?.GetValue(__instance) as Recipe;
                }
                catch { }
                string prefab = recipe?.m_item?.gameObject?.name;
                if (string.IsNullOrEmpty(prefab)) return;
                QuestProgressClient.SendProgress("Craft", prefab, 1);
            }
        }

        /// <summary>"Build" — Player.PlacePiece confirms a built structure. In this Valheim version
        /// PlacePiece is a void method, so we read the piece arg directly without checking __result.
        /// (Hooking <c>Piece.SetCreator</c> as a belt-and-suspenders backup since it's only called
        /// after the place succeeds.)</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        public static class Player_PlacePiece_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance, Piece piece)
            {
                if (__instance != Player.m_localPlayer || piece == null) return;
                string prefab = piece.gameObject?.name;
                if (string.IsNullOrEmpty(prefab)) return;
                int paren = prefab.IndexOf("(Clone)", StringComparison.Ordinal);
                if (paren > 0) prefab = prefab.Substring(0, paren);
                QuestProgressClient.SendProgress("Build", prefab, 1);
            }
        }

        /// <summary>"Move" — KG checks distance to a target location every 3 seconds. We do the same via
        /// a small driver on Player.Update.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        public static class Player_Update_MoveQuest_Patch
        {
            private static float _moveCounter;
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                _moveCounter -= Time.deltaTime;
                if (_moveCounter > 0f) return;
                _moveCounter = 3f;
                // Move quests use the target Prefab as the SERIALIZED coordinate "x,y,z" or as a known
                // location key. Server resolves match against position; client just announces position.
                Vector3 pos = __instance.transform.position;
                QuestProgressClient.SendMoveCheck(pos);
            }
        }
    }

    /// <summary>
    /// Public entry point dialogue code calls when a Talk-quest target NPC finishes its interaction.
    /// (We can't generically Harmony-patch the dialogue layer — too many entry shapes — so the Dialogue
    /// system calls this directly from its completion handler.)
    /// </summary>
    public static class TalkQuestBridge
    {
        public static void OnTalkedTo(string npcName)
        {
            if (string.IsNullOrEmpty(npcName)) return;
            QuestProgressClient.SendProgress("Talk", npcName, 1);
        }
    }

    /// <summary>Client→server progress dispatch. Single funnel so server can route every quest type
    /// through the same handler instead of one RPC per type.</summary>
    public static class QuestProgressClient
    {
        /// <summary>Fire a quest-progress event for the current local player. Server-side
        /// RPC_QuestProgressEvent resolves matching active quests and increments their progress.</summary>
        public static void SendProgress(string questType, string prefab, int amount)
        {
            try
            {
                var local = Player.m_localPlayer;
                if (local == null || string.IsNullOrEmpty(questType) || string.IsNullOrEmpty(prefab)) return;

                // Collect is inventory-based — QuestProgressTracker.GetCollectProgress reads live inventory, so bumping
                // a stored counter here would double-count and stay stuck after a drop. Every OTHER type is event-driven.
                if (string.Equals(questType, "Collect", StringComparison.OrdinalIgnoreCase)) return;

                // kg-parity: active quests are CLIENT-LOCAL (ActiveQuestStore, per-quest m_customData). Apply progress
                // directly to that store. The old path routed to the server (or inline server data), which held NO copy
                // of these quests — so craft/harvest/build/talk objectives silently never counted.
                VaultOfKnowledge.ApplyQuestProgressClientLocal(questType, prefab, amount);
            }
            catch (Exception ex) { Debug.LogWarning("[FiresRPGmaker] SendProgress failed: " + ex.Message); }
        }

        /// <summary>Move-quest distance check (x/z within ~10 m of the quest's "x,y,z" target), applied on the player's
        /// own client like every other objective type. 0.0.532: it used to go to the server, which read the vault blob of
        /// a remote Player; vanilla never sends Player.m_customData to the server, so Move quests never progressed on a
        /// dedicated server (and on a host it read the vault blob, not the active-quest store the accept writes).</summary>
        public static void SendMoveCheck(Vector3 position)
        {
            try
            {
                if (Player.m_localPlayer == null) return;
                VaultOfKnowledge.ApplyMoveQuestCheckClientLocal(position);
            }
            catch (Exception ex) { Debug.LogWarning("[FiresRPGmaker] SendMoveCheck failed: " + ex.Message); }
        }
    }
}
