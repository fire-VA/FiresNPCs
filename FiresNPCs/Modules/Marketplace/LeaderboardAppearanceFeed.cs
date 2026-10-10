using System;
using UnityEngine;
using FiresCore.Bridge;
using FiresCore.Storage;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Server-authoritative appearance WRITE path for the leaderboard mannequin preview. The owner client
    /// captures its resolved look (Core <c>PlayerAppearanceCapture</c>) and sends it via the
    /// <c>Fires_Leaderboard_SaveAppearance</c> routed RPC; the server normalizes the key with
    /// <see cref="LeaderboardServerFeed.OwnerKey"/> (the SAME key the board uses) and upserts into Core's
    /// authoritative vault via <see cref="PlayerAppearanceBridge.Save"/>. Clone of
    /// <see cref="LeaderboardServerFeed"/>'s Send/RPC shape. Pairs with the lazy fetch in
    /// <see cref="LeaderboardAppearanceBoard"/>.
    /// </summary>
    public static class LeaderboardAppearanceFeed
    {
        public const string RpcName = "Fires_Leaderboard_SaveAppearance";

        public static void RegisterRpc(ZRoutedRpc rpc)
        {
            rpc?.Register(RpcName, new Action<long, ZPackage>(RPC_SaveAppearance));
        }

        // ── Client → server ─────────────────────────────────────────────────────────────────────
        public static void Send(PlayerAppearance appearance)
        {
            if (ZRoutedRpc.instance == null || appearance == null || string.IsNullOrEmpty(appearance.PlayerName)) return;
            try
            {
                var pkg = new ZPackage();
                appearance.Pack(pkg);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcName, pkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Appearance Send failed: {ex.Message}"); }
        }

        // ── Server: upsert into Core's authoritative repo ───────────────────────────────────────
        private static void RPC_SaveAppearance(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;
            try
            {
                var appearance = PlayerAppearance.Unpack(pkg);
                if (appearance == null || string.IsNullOrEmpty(appearance.PlayerName)) return;

                // Key off the board's normalizer so rows and appearances join on the same Owner value.
                appearance.Owner = LeaderboardServerFeed.OwnerKey(appearance.PlayerName);
                if (string.IsNullOrEmpty(appearance.Owner)) return;

                PlayerAppearanceBridge.Save(appearance);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] RPC_SaveAppearance failed: {ex.Message}"); }
        }
    }
}
