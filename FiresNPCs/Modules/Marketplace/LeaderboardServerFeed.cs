using System;
using HarmonyLib;
using UnityEngine;
using FiresCore.Bridge;

namespace FiresRPGmaker.Modules.Marketplace
{
    public enum LeaderboardStat
    {
        KilledCreature = 0,
        KilledPlayer = 1,
        Built = 2,
        Crafted = 3,
        Death = 4,
        Harvested = 5,
        MapExplored = 6,
    }

    /// <summary>
    /// Server-authoritative leaderboard feed. Client capture patches send structured stat events via the
    /// <c>Fires_Leaderboard_RecordStat</c> routed RPC; the server records them into Core's
    /// <see cref="LeaderboardBridge"/>/<c>LeaderboardRepository</c> (the shared per-world LiteDB vault), the
    /// single source of truth for the cross-player board. This replaces the client-local
    /// <see cref="LeaderboardTracker"/> CSV — which stays only until the panel is repointed at the server board.
    ///
    /// Kills are NOT captured here: <see cref="FiresRPGmaker.Modules.Quest.QuestProgressPatches"/> already
    /// credits every kill server-side (with companion attribution) and now also calls <see cref="Send"/>, so
    /// this file's death patch records only the local player's deaths — no double counting.
    /// </summary>
    public static class LeaderboardServerFeed
    {
        public const string RpcName = "Fires_Leaderboard_RecordStat";

        public static void RegisterRpc(ZRoutedRpc rpc)
        {
            rpc?.Register(RpcName, new Action<long, ZPackage>(RPC_RecordStat));
        }

        /// <summary>Owner key for the leaderboard row. The vault DB is already per-world, so the player name
        /// alone is a sufficient, stable-enough key (matches the existing name-keyed board).</summary>
        public static string OwnerKey(string playerName) => (playerName ?? string.Empty).Trim();

        // ── Client → server ─────────────────────────────────────────────────────────────────────
        public static void Send(LeaderboardStat stat, string playerName, string prefab, int amount)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(playerName) || amount == 0) return;
            try
            {
                var pkg = new ZPackage();
                pkg.Write(playerName);
                pkg.Write((int)stat);
                pkg.Write(prefab ?? string.Empty);
                pkg.Write(amount);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcName, pkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Leaderboard Send failed: {ex.Message}"); }
        }

        // ── Server: record into Core's authoritative repo ───────────────────────────────────────
        private static void RPC_RecordStat(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;
            try
            {
                string playerName = pkg.ReadString();
                var stat = (LeaderboardStat)pkg.ReadInt();
                string prefab = pkg.ReadString();
                int amount = pkg.ReadInt();
                if (string.IsNullOrEmpty(playerName)) return;

                // [LeaderboardFeedDiag] TEMP — remove after the feed is verified in a test log.
                Debug.Log($"[LeaderboardFeedDiag] record {stat} prefab='{prefab}' x{amount} for '{playerName}'");

                string ownerKey = OwnerKey(playerName);
                switch (stat)
                {
                    case LeaderboardStat.KilledCreature: LeaderboardBridge.RecordKilledCreature(ownerKey, playerName, prefab, amount); break;
                    // KilledPlayers is read off KilledCreatures["Player"] on LeaderboardEntry, so PvP kills go there.
                    case LeaderboardStat.KilledPlayer:   LeaderboardBridge.RecordKilledCreature(ownerKey, playerName, "Player", amount); break;
                    case LeaderboardStat.Built:          LeaderboardBridge.RecordBuilt(ownerKey, playerName, prefab, amount); break;
                    case LeaderboardStat.Crafted:        LeaderboardBridge.RecordCrafted(ownerKey, playerName, prefab, amount); break;
                    case LeaderboardStat.Death:          LeaderboardBridge.RecordDeath(ownerKey, playerName); break;
                    case LeaderboardStat.Harvested:      LeaderboardBridge.RecordHarvested(ownerKey, playerName, prefab, amount); break;
                    case LeaderboardStat.MapExplored:    LeaderboardBridge.SetMapExplored(ownerKey, playerName, Mathf.Clamp01(amount / 10000f)); break;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] RPC_RecordStat failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Client-side capture for the server leaderboard feed. Mirrors <see cref="LeaderboardTrackerPatches"/>'s
    /// proven event hooks (and their dedicated-server caveats) but forwards structured events to the server
    /// instead of writing a local CSV. Kills are intentionally absent (sourced from QuestProgressPatches).
    /// </summary>
    public static class LeaderboardFeedPatches
    {
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        public static class Character_OnDeath
        {
            [HarmonyPostfix]
            private static void Postfix(Character __instance)
            {
                var local = Player.m_localPlayer;
                if (__instance == null || local == null) return;
                if (__instance == (Character)local)
                    LeaderboardServerFeed.Send(LeaderboardStat.Death, local.GetPlayerName(), null, 1);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        public static class InventoryGui_DoCrafting
        {
            [HarmonyPostfix]
            private static void Postfix(Player player)
            {
                if (player == null || player != Player.m_localPlayer) return;
                LeaderboardServerFeed.Send(LeaderboardStat.Crafted, player.GetPlayerName(), null, 1);
            }
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        public static class Piece_SetCreator
        {
            [HarmonyPostfix]
            private static void Postfix(Piece __instance, long uid)
            {
                var local = Player.m_localPlayer;
                if (local == null || __instance == null) return;
                if (uid != local.GetPlayerID()) return;
                LeaderboardServerFeed.Send(LeaderboardStat.Built, local.GetPlayerName(), __instance.m_category.ToString(), 1);
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Explore), typeof(int), typeof(int))]
        public static class Minimap_Explore
        {
            [HarmonyPostfix]
            private static void Postfix(bool __result, Minimap __instance)
            {
                if (!__result || __instance == null) return;
                var local = Player.m_localPlayer;
                if (local == null) return;
                // Sample every ~200 calls — sending per revealed pixel would flood the RPC. SetMapExplored uses
                // max() on the server, so sending the current fraction periodically converges correctly.
                if (UnityEngine.Random.Range(0, 200) != 0) return;
                int total = __instance.m_textureSize * __instance.m_textureSize;
                if (total <= 0 || __instance.m_explored == null) return;
                int explored = 0;
                var arr = __instance.m_explored;
                for (int i = 0; i < arr.Length; i++) if (arr[i]) explored++;
                int basisPoints = Mathf.RoundToInt((float)explored / total * 10000f);
                LeaderboardServerFeed.Send(LeaderboardStat.MapExplored, local.GetPlayerName(), null, basisPoints);
            }
        }

        // Pickable.Interact's return is the use-animation flag, not a did-pick signal. Credit a non-repeat
        // interact with an un-picked pickable (prefix __state == false) by the local player — the same
        // optimistic, dedi-safe approach the legacy tracker documents (a contested pick can over-credit by 1).
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        public static class Pickable_Interact
        {
            [HarmonyPrefix]
            private static void Prefix(Pickable __instance, out bool __state)
                => __state = __instance != null && __instance.GetPicked();

            [HarmonyPostfix]
            private static void Postfix(Pickable __instance, Humanoid character, bool repeat, bool __state)
            {
                if (repeat || __state || __instance == null || character == null) return;
                var local = Player.m_localPlayer;
                if (local == null || character != (Humanoid)local) return;
                string name = __instance.gameObject.name.Replace("(Clone)", "").Trim();
                LeaderboardServerFeed.Send(LeaderboardStat.Harvested, local.GetPlayerName(), name, 1);
            }
        }
    }
}
