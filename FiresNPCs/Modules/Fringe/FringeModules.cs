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
                    if (Submit(msg)) args.Context.AddString("[feedback] sent — thank you.");
                    else args.Context.AddString("[feedback] no webhook configured (set Feedback/WebhookUrl).");
                });
            }
        }

        /// <summary>Posts one feedback message to the configured webhook. Shared by the /feedback command
        /// and the Feedback NPC type. Returns false (with no HUD noise) when no webhook is configured.</summary>
        public static bool Submit(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string url = ConfigManager.Instance?.configFeedbackWebhook?.Value;
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Feedback is not set up on this server.");
                return false;
            }
            string who = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Player";
            string body = $"**Feedback from {who}**: {message}";
            global::FiresRPGmaker.Modules.Dialogues.DialogueCommandEngine.PostWebhook(url, body);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Feedback sent — thank you!");
            return true;
        }

        /// <summary>Opens the vanilla text prompt and posts whatever the player types (kg Feedback-NPC flow).</summary>
        public static void PromptAndSubmit(string npcName)
        {
            if (TextInput.instance == null) { MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Feedback unavailable."); return; }
            TextInput.instance.RequestText(new FeedbackReceiver(), string.IsNullOrEmpty(npcName) ? "Feedback" : $"Feedback — {npcName}", 256);
        }

        private class FeedbackReceiver : TextReceiver
        {
            public string GetText() => "";
            public void SetText(string text) => Submit(text);
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
