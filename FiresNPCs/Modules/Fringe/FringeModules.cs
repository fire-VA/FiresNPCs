using System.Linq;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Fringe
{
    /// <summary>
    /// In-game feedback → Discord webhook (Marketplace Feedback). `/feedback &lt;message&gt;` posts to the
    /// configured webhook (Feedback/WebhookUrl), with player name + timestamp. Reuses the fire-and-
    /// forget POST the dialogue SendWebhook verb uses.
    /// </summary>
    public static class FeedbackModule
    {
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class RegisterCommand
        {
            private static bool _registered;

            [HarmonyPostfix]
            private static void Register()
            {
                if (_registered) return;
                _registered = true;
                new Terminal.ConsoleCommand("feedback", "<message> - send feedback to the server's Discord", args =>
                {
                    if (args.Length < 2) { args.Context.AddString("usage: feedback <message>"); return; }
                    string msg = string.Join(" ", args.Args.Skip(1));
                    Submit(msg, args);
                });
            }
        }

        private static void Submit(string message, Terminal.ConsoleEventArgs args)
        {
            string url = ConfigManager.Instance?.configFeedbackWebhook?.Value;
            if (string.IsNullOrWhiteSpace(url))
            {
                args.Context.AddString("[feedback] no webhook configured (set Feedback/WebhookUrl).");
                return;
            }
            string who = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Player";
            string body = $"**Feedback from {who}**: {message}";
            global::FiresRPGmaker.Modules.Dialogues.DialogueCommandEngine.PostWebhook(url, body);
            args.Context.AddString("[feedback] sent — thank you.");
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Feedback sent — thank you!");
        }
    }

    /// <summary>
    /// DistancedUI (Marketplace) — lifts the local player's interaction range so NPCs/objects can be
    /// used from further away. Driven by the UI/ExtendedInteractRange config (0 = vanilla). Applied
    /// every Update so a live config change (or vanilla resetting the field) takes effect immediately.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class DistancedInteractionPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            float range = ConfigManager.Instance?.configExtendedInteractRange?.Value ?? 0f;
            if (range > 0f && __instance.m_maxInteractDistance < range)
                __instance.m_maxInteractDistance = range;
        }
    }
}
