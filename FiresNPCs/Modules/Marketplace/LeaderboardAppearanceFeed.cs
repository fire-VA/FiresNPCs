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
                ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcName), RpcName, pkg);
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
                // Key by the routed sender's own name: a client may only save its own look.
                if (!global::FiresRPGmaker.Managers.RpcSender.IsServerSelf(sender))
                {
                    string senderName = global::FiresRPGmaker.Managers.RpcSender.SenderName(sender);
                    if (string.IsNullOrEmpty(senderName)) { global::FiresRPGmaker.Managers.RpcSender.Refuse(RpcName, sender, "unknown sender name"); return; }
                    // A mismatched name is dropped, never rewritten.
                    if (!string.Equals(senderName.Trim(), appearance.PlayerName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[FiresRPGmaker] {RpcName}: dropped appearance for '{appearance.PlayerName}' sent by peer {sender} ('{senderName}')");
                        return;
                    }
                }

                // Key off the board's normalizer so rows and appearances join on the same Owner value.
                appearance.Owner = LeaderboardServerFeed.OwnerKey(appearance.PlayerName);
                if (string.IsNullOrEmpty(appearance.Owner)) return;

                PlayerAppearanceBridge.Save(appearance);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] RPC_SaveAppearance failed: {ex.Message}"); }
        }
    }
}
