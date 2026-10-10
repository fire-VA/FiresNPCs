using HarmonyLib;
using UnityEngine;
using FiresCore.Appearance;
using FiresCore.Storage;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Owner-side capture triggers for the appearance feed. Each hook is gated to
    /// <c>Player.m_localPlayer</c> (so it auto-no-ops on a headless server, where m_localPlayer is null) and
    /// only ever SENDS when the appearance signature actually changed — never per-frame. Triggers:
    /// <list type="bullet">
    /// <item>Initial capture — once the local player + ZNetScene are ready (first eligible tick).</item>
    /// <item>Equip change — postfix on <c>Humanoid.SetupVisEquipment</c>, the single vanilla chokepoint for
    /// any slot/model/hair/beard change.</item>
    /// <item>Periodic backstop — a ~3 s throttled <c>Player.Update</c> tick that catches color-only paths
    /// the equip hook misses.</item>
    /// <item>Exit flush — prefixes on <c>ZNet.Disconnect</c> / <c>Game.Logout</c> / <c>ZNet.Shutdown</c> so
    /// the server's last-known record is current.</item>
    /// </list>
    /// All targets (Humanoid/Player/ZNet/Game) exist server-side, so this class does NOT need the dedi
    /// skip-set — the m_localPlayer gate already makes every path a no-op on a dedicated server.
    /// </summary>
    public static class LeaderboardAppearanceCapturePatches
    {
        private const float BackstopIntervalSeconds = 3f;

        private static int _lastSentSignature;
        private static bool _hasSent;
        private static bool _initialCaptured;
        private static float _backstopTimer;

        // Capture the local player and send only when the signature changed since the last send.
        private static void CaptureAndSendIfChanged()
        {
            var local = Player.m_localPlayer;
            if (local == null || ZNetScene.instance == null) return;

            PlayerAppearance rec = PlayerAppearanceCapture.Capture(local);
            if (rec == null) return;

            int sig = rec.GetSignature();
            if (_hasSent && sig == _lastSentSignature) return;

            LeaderboardAppearanceFeed.Send(rec);
            _lastSentSignature = sig;
            _hasSent = true;
        }

        // Force a flush regardless of signature (used on exit so the last-known record is current).
        private static void CaptureAndSendForce()
        {
            var local = Player.m_localPlayer;
            if (local == null || ZNetScene.instance == null) return;

            PlayerAppearance rec = PlayerAppearanceCapture.Capture(local);
            if (rec == null) return;

            LeaderboardAppearanceFeed.Send(rec);
            _lastSentSignature = rec.GetSignature();
            _hasSent = true;
        }

        [HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
        public static class Humanoid_SetupVisEquipment
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance)
            {
                if (__instance == null || Player.m_localPlayer == null) return;
                if (__instance != (Humanoid)Player.m_localPlayer) return;
                CaptureAndSendIfChanged();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        public static class Player_Update
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;

                if (!_initialCaptured)
                {
                    _initialCaptured = true;
                    CaptureAndSendIfChanged();
                    return;
                }

                _backstopTimer += Time.deltaTime;
                if (_backstopTimer < BackstopIntervalSeconds) return;
                _backstopTimer = 0f;
                CaptureAndSendIfChanged();
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        public static class ZNet_Disconnect
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                if (Player.m_localPlayer == null) return;
                CaptureAndSendForce();
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Logout))]
        public static class Game_Logout
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                if (Player.m_localPlayer == null) return;
                CaptureAndSendForce();
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
        public static class ZNet_Shutdown
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                if (Player.m_localPlayer == null) return;
                CaptureAndSendForce();
            }
        }
    }
}
