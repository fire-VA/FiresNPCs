using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Modules.Guilds;

namespace FiresRPGmaker.Modules.Guilds.GuildChat
{
    /// <summary>
    /// Adds a "/g [text]" guild-only chat command.
    ///
    /// Messages are sent to GuildSyncManager which forwards them only to
    /// online guild members.  Received messages are rendered in the vanilla
    /// chat window with a coloured "[Guild]" prefix matching the guild colour.
    ///
    /// NOTE: namespace is GuildChat (not Chat) to avoid shadowing Valheim's Chat class.
    /// </summary>
    public static class GuildChatChannel
    {
        private const string CMD_PREFIX = "/g ";
        private const string CMD_SHORT  = "/g";

        public static void Initialize()
        {
            GuildAPI.OnGuildChatReceived += OnChatReceived;
        }

        public static void Shutdown()
        {
            GuildAPI.OnGuildChatReceived -= OnChatReceived;
        }

        // Called by the chat input patch below.
        public static bool TryHandleInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            string trimmed = input.Trim();
            string text    = null;

            if (trimmed.StartsWith(CMD_PREFIX))
                text = trimmed.Substring(CMD_PREFIX.Length).Trim();
            else if (trimmed == CMD_SHORT)
                text = "";

            if (text == null) return false;

            if (string.IsNullOrWhiteSpace(text))
            {
                global::Chat.instance?.AddString("<color=#aaa>[Guild]</color> Usage: /g [message]");
                return true;
            }

            var guild = GuildAPI.GetOwnGuild();
            if (guild == null)
            {
                global::Chat.instance?.AddString("<color=#aaa>[Guild]</color> You are not in a guild.");
                return true;
            }

            GuildAPI.SendChatMessage(text);
            return true;
        }

        private static void OnChatReceived(string senderName, string guildName, string text)
        {
            if (global::Chat.instance == null) return;

            var guild = GuildAPI.GetOwnGuild();
            string colorHex = guild != null
                ? ColorUtility.ToHtmlStringRGB(guild.GuildColor)
                : "C8AA50";

            global::Chat.instance.AddString(
                $"<color=#{colorHex}>[{guildName}]</color> <b>{senderName}</b>: {text}");
        }

        // ── Harmony patch: intercept vanilla chat submit ──────────────────────

        [HarmonyPatch(typeof(global::Chat), "InputText")]
        private static class Chat_InputText_Patch
        {
            private static bool Prefix(global::Chat __instance)
            {
                var input = __instance.m_input?.text ?? "";
                if (TryHandleInput(input))
                {
                    __instance.m_input.text = "";
                    return false; // suppress vanilla handling
                }
                return true;
            }
        }
    }
}
