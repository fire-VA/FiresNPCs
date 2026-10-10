using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.Modules.ChatSystem
{
    /// <summary>
    /// Harmony patches for the RPG chat channel system.
    ///
    /// Patch points:
    /// 1. Chat.Awake (postfix)  - initialize our channel UI
    /// 2. Chat.InputText (prefix) - intercept outgoing text and route by active channel
    /// 3. Chat.OnNewChatMessage (postfix) - color incoming messages by type (future: filter group/clan)
    /// 4. Chat.Update (postfix) - handle channel-switch hotkey when chat isn't focused
    /// 5. Chat.OnDestroy (postfix) - clean up media embed overlay
    /// </summary>
    [HarmonyPatch]
    public static class ChatPatches
    {
        /// <summary>
        /// After Chat initializes, create our channel UI buttons.
        /// </summary>
        [HarmonyPatch(typeof(Chat), "Awake")]
        [HarmonyPostfix]
        private static void Chat_Awake_Postfix()
        {
            ChatChannelUI.EnsureCreated();
        }

        /// <summary>
        /// Intercepts outgoing chat text BEFORE vanilla processing.
        /// 
        /// If the player typed a slash command (starts with '/'), let vanilla handle it.
        /// Otherwise, route the message based on the active channel:
        ///   Local  -> "say {text}"     -> Talker.Type.Normal (vanilla default)
        ///   Global -> "s {text}"       -> Talker.Type.Shout (auto /s)
        ///   Group  -> blocked (stub)
        ///   Clan   -> blocked (stub)
        ///
        /// Returns false to skip vanilla InputText when we handle it ourselves.
        /// Returns true to let vanilla process slash commands normally.
        /// </summary>
        private static bool IsLocalPlayerMuted()
        {
            string id = null, name = null;
            try { id = global::FiresRPGmaker.Modules.Dialogues.EwpDataBridge.GetLocalSteamId(); } catch { }
            try { name = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null; } catch { }
            return global::FiresRPGmaker.Modules.Marketplace.MarketplaceGlobals.IsMutedChatUser(id, name);
        }

        [HarmonyPatch(typeof(Chat), "InputText")]
        [HarmonyPrefix]
        private static bool Chat_InputText_Prefix(Chat __instance)
        {
            string text = __instance.m_input.text;
            if (string.IsNullOrEmpty(text))
                return false; // Empty - skip

            // kg BlockedChatUsers (MarketPlace.cfg, synced): a muted player's own chat input is
            // swallowed — kg's exact enforcement point (its KG_Chat InputText prefix does the same).
            // Slash commands stay allowed (matching kg, which only gates its chat send path).
            if (IsLocalPlayerMuted() && text[0] != '/')
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "You are muted on this server.");
                __instance.m_input.text = "";
                return false;
            }

            // If the player explicitly typed a slash command, let vanilla handle it
            // This preserves /s, /w, /die, /resetspawn, emotes, etc.
            if (text[0] == '/')
                return true;

            // Route based on active channel
            var channel = ChatChannelManager.ActiveChannel;

            switch (channel)
            {
                case ChatChannelManager.Channel.Local:
                    // Vanilla default behavior: "say {text}" -> Normal
                    return true;

                case ChatChannelManager.Channel.Global:
                    // Auto-shout: route through "s" command instead of "say"
                    __instance.TryRunCommand("s " + text, false, false);
                    return false; // Skip vanilla InputText

                case ChatChannelManager.Channel.Group:
                    // Stub: not wired yet
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        "Group chat coming soon!");
                    return false;

                case ChatChannelManager.Channel.Clan:
                    // Guild chat routes through the Core GuildBridge — FiresGuilds owns the guild system now.
                    if (FiresCore.Bridge.GuildBridge.LocalGuildName() == null)
                        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "You are not in a guild.");
                    else
                        FiresCore.Bridge.GuildBridge.SendGuildChat(text);
                    __instance.m_input.text = "";
                    return false;

                default:
                    return true;
            }
        }

        /// <summary>
        /// After a chat message is received, apply Discord-blue coloring
        /// to messages relayed from Discord, and handle future channel filtering.
        /// </summary>
        [HarmonyPatch(typeof(Chat), "OnNewChatMessage")]
        [HarmonyPostfix]
        private static void Chat_OnNewChatMessage_Postfix(
            GameObject go, long senderID, Vector3 pos,
            Talker.Type type, UserInfo sender, string text)
        {
            // Detect image/GIF URLs in any chat message and queue embeds
            List<ChatMediaDetector.DetectedMedia> mediaUrls = null;
            try
            {
                mediaUrls = ChatMediaDetector.ExtractImageUrls(text);
            }
            catch { }

            bool hasMedia = mediaUrls != null && mediaUrls.Count > 0;
            bool isDiscord = text != null && text.IndexOf("[Discord]", System.StringComparison.OrdinalIgnoreCase) >= 0;

            // Re-color Discord relay messages and/or strip media URLs from the chat buffer
            if (isDiscord || hasMedia)
            {
                try
                {
                    if (Chat.instance != null)
                    {
                        var chatBuffer = Chat.instance.m_chatBuffer;
                        if (chatBuffer != null && chatBuffer.Count > 0)
                        {
                            int lastIdx = chatBuffer.Count - 1;
                            string lastEntry = chatBuffer[lastIdx];

                            // Strip media URLs from the chat buffer entry text
                            if (hasMedia)
                            {
                                lastEntry = ChatMediaDetector.StripMediaUrls(lastEntry);
                            }

                            // Apply Discord blue color to the entire entry
                            // Use case-insensitive check: vanilla uppercases Shout text
                            // so [Discord] becomes [DISCORD] in the buffer
                            if (isDiscord && lastEntry.IndexOf("[DISCORD]", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Remove any existing color tags so we can wrap cleanly
                                lastEntry = System.Text.RegularExpressions.Regex.Replace(
                                    lastEntry, @"<color=[^>]*>", "");
                                lastEntry = lastEntry.Replace("</color>", "");
                                lastEntry = "<color=#7289DA>" + lastEntry + "</color>";
                            }

                            chatBuffer[lastIdx] = lastEntry;
                            Chat.instance.UpdateChat();
                        }
                    }
                }
                catch { }
            }

            // Queue media embeds and show loading note
            if (hasMedia)
            {
                try
                {
                    if (Chat.instance != null)
                    {
                        bool hasGif = false;
                        foreach (var m in mediaUrls)
                        {
                            if (m.Type == ChatMediaDetector.MediaType.AnimatedGif)
                            { hasGif = true; break; }
                        }
                        string note = hasGif
                            ? "<color=#888888><i>Loading GIF\u2026</i></color>"
                            : "<color=#888888><i>Loading image\u2026</i></color>";
                        Chat.instance.m_chatBuffer?.Add(note);
                        Chat.instance.UpdateChat();
                    }
                }
                catch { }

                foreach (var media in mediaUrls)
                {
                    ChatMediaEmbedManager.QueueEmbed(media.Url, media.Type);
                }
            }
        }

        /// <summary>
        /// Ensures our channel UI stays alive if Chat is re-initialized.
        /// Tab-key channel cycling is handled in ChatChannelUI.HandleTabCycle().
        /// </summary>
        [HarmonyPatch(typeof(Chat), "Update")]
        [HarmonyPostfix]
        private static void Chat_Update_Postfix()
        {
            // Ensure UI is created if Chat was re-initialized
            if (ChatChannelUI.Instance == null && Chat.instance != null)
            {
                ChatChannelUI.EnsureCreated();
            }

            // Prune destroyed media embeds
            ChatMediaEmbedManager.PruneDestroyed();
        }

        /// <summary>
        /// Clean up the media embed overlay when Chat is destroyed
        /// (scene change, logout to menu).
        /// </summary>
        [HarmonyPatch(typeof(Chat), "OnDestroy")]
        [HarmonyPostfix]
        private static void Chat_OnDestroy_Postfix()
        {
            try
            {
                ChatMediaEmbedManager.ClearAll();
                ChatMediaEmbedManager.ResetOverlay();
            }
            catch { }
        }
    }
}
