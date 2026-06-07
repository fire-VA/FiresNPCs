using System;
using System.Linq;
using UnityEngine;
using FiresRPGmaker.Modules.Vault;

namespace FiresRPGmaker.Modules.Territories
{
    internal static class TerritoryEntryReporter
    {
        internal class TerritoryIdentity
        {
            public long PlayerId;
            public string PlayerName = string.Empty;
            public string PlatformId = string.Empty;
            public string Platform = string.Empty;
        }

        public static TerritoryIdentity ResolveIdentity(Player player)
        {
            var id = player != null ? player.GetPlayerID() : 0L;
            var name = player != null ? (player.GetPlayerName() ?? string.Empty) : string.Empty;
            string platformId = string.Empty;
            string platform = string.Empty;

            try
            {
                // Prefer peer socket host name (SteamID/XboxID/etc.)
                if (player != null && ZNet.instance != null && !string.IsNullOrEmpty(name))
                {
                    var peer = ZNet.instance.GetPeerByPlayerName(name);
                    if (peer != null)
                    {
                        platformId = peer.m_socket?.GetHostName() ?? string.Empty;
                        platform = ZNet.m_onlineBackend.ToString();
                    }
                }
            }
            catch { }

            try
            {
                if (VaultOfKnowledge.Instance != null && id != 0)
                {
                    var data = VaultOfKnowledge.Instance.GetPlayerData(id);
                    if (data != null)
                    {
                        if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(data.PlayerName)) name = data.PlayerName;
                        if (string.IsNullOrEmpty(platformId) && !string.IsNullOrEmpty(data.PlatformId)) platformId = data.PlatformId;
                        if (string.IsNullOrEmpty(platform) && !string.IsNullOrEmpty(data.Platform)) platform = data.Platform;
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(platform))
            {
                try { platform = ZNet.m_onlineBackend.ToString(); } catch { platform = string.Empty; }
            }

            return new TerritoryIdentity
            {
                PlayerId = id,
                PlayerName = name ?? string.Empty,
                PlatformId = platformId ?? string.Empty,
                Platform = platform ?? string.Empty
            };
        }

        public static void SendEntryToServer(Territory terr, Player player)
        {
            if (terr == null || player == null) return;
            if (ZNet.instance == null || ZNet.instance.IsServer()) return;
            if (ZRoutedRpc.instance == null) return;

            var id = ResolveIdentity(player);
            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_Territory_ClientEntered",
                    id.PlayerId,
                    id.PlayerName ?? string.Empty,
                    id.PlatformId ?? string.Empty,
                    id.Platform ?? string.Empty,
                    terr.UID,
                    terr.Name ?? string.Empty);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Territory entry send failed: {ex.Message}");
            }
        }
    }
}
