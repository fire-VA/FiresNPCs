using UnityEngine;
using BepInEx.Configuration;

namespace FiresRPGmaker.Modules.ChatSystem
{
    /// <summary>
    /// RPG-style chat channel system.
    /// Tracks which channel the player has selected so outgoing messages
    /// are automatically routed without needing slash commands.
    ///
    /// The selected channel is persisted in the BepInEx config so it
    /// survives log-out / game restart.
    ///
    /// Channels:
    ///   Local  - Talker.Type.Normal  (default, 15m range)
    ///   Global - Talker.Type.Shout   (server-wide, auto-prepends /s)
    ///   Group  - stub, will wire to clan/group system later
    ///   Clan   - stub, will wire to clan/group system later
    /// </summary>
    public static class ChatChannelManager
    {
        public enum Channel
        {
            Local,
            Global,
            Group,
            Clan
        }

        private static Channel _activeChannel = Channel.Local;
        private static ConfigEntry<Channel> _savedChannel;

        /// <summary>Chat panel size multiplier — bigger for readability. Hot-reloads live.</summary>
        public static ConfigEntry<float> UiScale;
        /// <summary>Horizontal / vertical nudge of the chat panel, in pixels. Hot-reloads live.</summary>
        public static ConfigEntry<float> OffsetX;
        public static ConfigEntry<float> OffsetY;
        /// <summary>Hotkey to toggle chat arrange mode — frees the cursor to drag / resize the panel.</summary>
        public static ConfigEntry<KeyboardShortcut> ArrangeKey;
        /// <summary>Replace the vanilla chat window with the Fires chat panel. Off = untouched vanilla chat.</summary>
        public static ConfigEntry<bool> CustomWindow;

        /// <summary>
        /// Call once during plugin init (after Config is available) to
        /// bind the persistent config entry and restore the saved channel.
        /// </summary>
        public static void InitConfig(ConfigFile config)
        {
            _savedChannel = config.Bind("Chat", "ActiveChannel", Channel.Local,
                "Last-selected chat channel. Persisted between sessions.");

            UiScale = config.Bind("Chat", "PanelScale", 1.0f,
                new ConfigDescription("Chat panel size multiplier — larger is easier to read.",
                    new AcceptableValueRange<float>(0.6f, 2.5f)));
            OffsetX = config.Bind("Chat", "PanelOffsetX", 0f, "Horizontal nudge of the chat panel (pixels).");
            OffsetY = config.Bind("Chat", "PanelOffsetY", 0f, "Vertical nudge of the chat panel (pixels).");
            ArrangeKey = config.Bind("Chat", "ArrangeHotkey", new KeyboardShortcut(KeyCode.C, KeyCode.LeftAlt),
                "Toggle chat arrange mode — frees the cursor to drag the panel and drag its corner grip to resize.");
            CustomWindow = config.Bind("Chat", "CustomChatWindow", true,
                "Replace the vanilla chat window with the Fires chat panel (tabs, styling, drag/resize). Off = untouched vanilla chat.");

            // Restore without triggering the screen message - just set the
            // backing field directly so we don't flash a notification on load.
            _activeChannel = _savedChannel.Value;
        }

        /// <summary>Current active chat channel.</summary>
        public static Channel ActiveChannel
        {
            get => _activeChannel;
            set
            {
                if (_activeChannel != value)
                {
                    _activeChannel = value;

                    // Persist to config file.
                    if (_savedChannel != null)
                        _savedChannel.Value = value;

                    OnChannelChanged?.Invoke(value);
                    ShowChannelNotification(value);
                    Debug.Log($"[ChatChannel] Switched to {value}");
                }
            }
        }

        /// <summary>Fired when the active channel changes. UI subscribes to update visuals.</summary>
        public static event System.Action<Channel> OnChannelChanged;

        /// <summary>Cycles to the next channel (keyboard shortcut).</summary>
        public static void CycleChannel()
        {
            ActiveChannel = ActiveChannel switch
            {
                Channel.Local => Channel.Global,
                Channel.Global => Channel.Group,
                Channel.Group => Channel.Clan,
                Channel.Clan => Channel.Local,
                _ => Channel.Local
            };
        }

        /// <summary>
        /// Shows a brief center-screen notification when the channel changes.
        /// </summary>
        private static void ShowChannelNotification(Channel channel)
        {
            if (MessageHud.instance == null) return;

            string name = GetChannelDisplayName(channel);
            if (!IsChannelAvailable(channel))
                name += "  (coming soon)";

            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"Chat: {name}");
        }

        /// <summary>
        /// Maps the current channel to a Talker.Type for sending.
        /// Group/Clan return null - they need custom RPC routing.
        /// </summary>
        public static Talker.Type? GetTalkerType()
        {
            return _activeChannel switch
            {
                Channel.Local => Talker.Type.Normal,
                Channel.Global => Talker.Type.Shout,
                // Group and Clan will use custom RPCs when wired in
                _ => null
            };
        }

        /// <summary>
        /// Display name for the active channel (shown in UI label).
        /// </summary>
        public static string GetChannelDisplayName(Channel channel)
        {
            return channel switch
            {
                Channel.Local => "Local",
                Channel.Global => "Global",
                Channel.Group => "Group",
                Channel.Clan => "Guild",
                _ => "Local"
            };
        }

        /// <summary>
        /// Color used for the channel indicator and button highlight.
        /// </summary>
        public static Color GetChannelColor(Channel channel)
        {
            return channel switch
            {
                Channel.Local => Color.white,
                Channel.Global => new Color(1f, 0.85f, 0.2f),        // Gold/yellow
                Channel.Group => new Color(0.4f, 0.8f, 1f),          // Light blue
                Channel.Clan => new Color(0.5f, 1f, 0.5f),           // Light green
                _ => Color.white
            };
        }

        /// <summary>
        /// Returns true if the channel is functional (has backend wiring).
        /// Group is still a stub; Guild (Channel.Clan) is wired through GuildAPI.
        /// </summary>
        public static bool IsChannelAvailable(Channel channel)
        {
            return channel switch
            {
                Channel.Local => true,
                Channel.Global => true,
                Channel.Group => false,  // TODO: wire to group system
                Channel.Clan => true,    // Wired to guild chat (GuildAPI)
                _ => false
            };
        }
    }
}
