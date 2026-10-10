using System;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// kg 9.7.9 faction chat parity: `/f &lt;message&gt;` (or console `f &lt;message&gt;`) broadcasts to every
    /// online member of the sender's faction. kg's exact presentation (Factions_Client.cs:141-155):
    /// orange sender name + green text in the chat window, plus an in-world speech bubble unless the
    /// receiver is map-closed and beyond the no-map ping distance. Faction identity is our string
    /// faction id (kg used the synced list index; ids survive list reordering, which is strictly better).
    /// </summary>
    public static class FactionChat
    {
        private const string RpcName = "Fires_FactionChat";

        private static bool _registered;
        private static ZRoutedRpc _registeredOn;

        public static void EnsureRegistered()
        {
            var current = ZRoutedRpc.instance;
            if (current == null) return;
            if (_registered && _registeredOn == current) return;
            current.Register<ZPackage>(RpcName, new Action<long, ZPackage>(RPC_FactionChat));
            _registered = true;
            _registeredOn = current;
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class RegisterCommand
        {
            private static bool _cmdRegistered;

            [HarmonyPostfix]
            private static void Register()
            {
                if (_cmdRegistered) return;
                _cmdRegistered = true;
                new Terminal.ConsoleCommand("f", "<message> - write a message in faction chat", args =>
                {
                    if (args.FullLine.Length <= 2) return;
                    SendMessageToFaction(args.FullLine.Substring(2).Trim());
                });
            }
        }

        private static void SendMessageToFaction(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var player = Player.m_localPlayer;
            if (player == null || ZRoutedRpc.instance == null) return;
            string factionId = FactionMembership.LocalFactionId();
            if (string.IsNullOrEmpty(factionId))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "You are not in a faction.");
                return;
            }
            EnsureRegistered();

            var pkg = new ZPackage();
            pkg.Write(factionId);
            pkg.Write(player.GetPlayerName() ?? "");
            pkg.Write(text);
            pkg.Write(player.transform.position);
            // Everybody (0L) loops back through HandleRoutedRPC locally (ZRoutedRpc.InvokeRoutedRPC),
            // so the sender hears their own message through the same receive path — no manual echo.
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pkg);
        }

        private static void RPC_FactionChat(long senderId, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                string factionId = pkg.ReadString();
                string name = pkg.ReadString();
                string text = pkg.ReadString();
                Vector3 pos = pkg.ReadVector3();
                ShowIncoming(senderId, factionId, name, text, pos);
            }
            catch (Exception ex) { Debug.LogWarning($"[FactionChat] receive failed: {ex.Message}"); }
        }

        private static void ShowIncoming(long senderId, string factionId, string name, string text, Vector3 pos)
        {
            if (Chat.instance == null || Player.m_localPlayer == null) return;
            // Only members of the sender's faction hear it (kg: index != MyFactionIndex → drop).
            string mine = FactionMembership.LocalFactionId();
            if (string.IsNullOrEmpty(mine) || !string.Equals(mine, factionId, StringComparison.OrdinalIgnoreCase)) return;

            Chat.instance.AddString($"<color=orange>{name}</color>: <color=green>{text}</color>");
            Chat.instance.m_hideTimer = 0f;

            // kg's in-world bubble rule: skip when the position is unknown, or the receiver has the map
            // closed and the sender is beyond the no-map ping distance.
            if (pos == Vector3.zero) return;
            if (Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.None
                && Vector3.Distance(Player.m_localPlayer.transform.position, pos) > Minimap.instance.m_nomapPingDistance) return;
            try
            {
                // Chat.AddInworldText is private (kg reached it via publicized refs); reflect once.
                var userInfo = new UserInfo { Name = name, UserId = new Splatform.PlatformUserID() };
                _addInworldText ??= AccessTools.Method(typeof(Chat), "AddInworldText",
                    new[] { typeof(GameObject), typeof(long), typeof(Vector3), typeof(Talker.Type), typeof(UserInfo), typeof(string) });
                _addInworldText?.Invoke(Chat.instance, new object[] { null, senderId, pos, Talker.Type.Normal, userInfo, $"<color=green>{text}</color>" });
            }
            catch { }
        }

        private static System.Reflection.MethodInfo _addInworldText;
    }
}
