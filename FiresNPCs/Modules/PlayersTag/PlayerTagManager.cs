using System;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.PlayersTag
{
    /// <summary>
    /// Cosmetic player name titles (Marketplace PlayersTag). A tag is stored on the player's own
    /// character customData (persists) and mirrored to the ZDO so other clients render it on the
    /// nameplate. Self-service (a player sets their own) plus admin-assign (a routed RPC tells the
    /// target client to set theirs — only the owning client can write its ZDO/customData reliably).
    /// </summary>
    public static class PlayerTagManager
    {
        public const string CustomDataKey = "Fires_PlayerTag";
        public const string ZdoKey = "Fires_PlayerTag";
        private const string SetRpc = "FiresRPGmaker_SetPlayerTag";

        public static string LocalTag()
        {
            var p = Player.m_localPlayer;
            if (p == null || p.m_customData == null) return null;
            return p.m_customData.TryGetValue(CustomDataKey, out var t) && !string.IsNullOrEmpty(t) ? t : null;
        }

        public static string TagOf(Character c)
        {
            var nview = c != null ? c.GetComponent<ZNetView>() : null;
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null) return null;
            string t = zdo.GetString(ZdoKey, "");
            return string.IsNullOrEmpty(t) ? null : t;
        }

        public static void SetLocal(string tag)
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            tag = string.IsNullOrWhiteSpace(tag) || tag.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? null : tag.Trim();
            if (tag != null && tag.Length > 32) tag = tag.Substring(0, 32);

            if (p.m_customData != null)
            {
                if (tag == null) p.m_customData.Remove(CustomDataKey);
                else p.m_customData[CustomDataKey] = tag;
            }
            RefreshLocal();
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                tag != null ? $"Title set: <color=#FFD966>{tag}</color>" : "Title cleared");
        }

        /// <summary>Write the local tag onto the ZDO so others see it (call on spawn + change).</summary>
        public static void RefreshLocal()
        {
            var p = Player.m_localPlayer;
            var nview = p != null ? p.GetComponent<ZNetView>() : null;
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo != null) zdo.Set(ZdoKey, LocalTag() ?? "");
        }

        /// <summary>Admin: set another player's tag (routes to that client), or the local one for self/me.</summary>
        public static void AdminSet(string playerName, string tag)
        {
            if (string.IsNullOrEmpty(playerName) || playerName.Equals("me", StringComparison.OrdinalIgnoreCase))
            { SetLocal(tag); return; }

            var pkg = new ZPackage();
            pkg.Write(playerName);
            pkg.Write(tag ?? "");
            // Route to the server; it forwards to the matching peer. Server also handles self.
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), SetRpc, pkg);
        }

        // ── RPC: server matches the target peer by name, forwards a "set your tag" to that client ──
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class RegisterRpc
        {
            private static void Postfix()
            {
                try { ZRoutedRpc.instance?.Register<ZPackage>(SetRpc, OnSetRpc); } catch { }
            }
        }

        private static void OnSetRpc(long sender, ZPackage pkg)
        {
            string playerName = pkg.ReadString();
            string tag = pkg.ReadString();

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                // Admin-gate the request, then forward to the named peer's client.
                if (!FiresCore.Sync.AdminSyncing.IsAdmin(sender)) return;
                foreach (var peer in ZNet.instance.GetPeers())
                {
                    if (!string.Equals(peer.m_playerName, playerName, StringComparison.OrdinalIgnoreCase)) continue;
                    var fwd = new ZPackage();
                    fwd.Write(playerName);
                    fwd.Write(tag);
                    ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, SetRpc, fwd);
                    return;
                }
                return;
            }

            // Client: this is the forwarded "set your tag" — apply if it's for me.
            var p = Player.m_localPlayer;
            if (p != null && string.Equals(p.GetPlayerName(), playerName, StringComparison.OrdinalIgnoreCase))
                SetLocal(tag);
        }
    }
}
