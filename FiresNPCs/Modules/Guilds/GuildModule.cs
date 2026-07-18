using UnityEngine;
using FiresRPGmaker.Modules.Guilds.Achievements;
using FiresRPGmaker.Modules.Guilds.GuildChat;
using FiresRPGmaker.Modules.Guilds.Network;
using FiresRPGmaker.Modules.Guilds.UI;

namespace FiresRPGmaker.Modules.Guilds
{
    /// <summary>
    /// Top-level entry point for the Guild system.
    ///
    /// Call <see cref="Initialize"/> once from <c>FiresRPGmaker.FinishInit()</c>
    /// after Harmony patches are applied and ZRoutedRpc is available.
    ///
    /// Call <see cref="InitializeClientUI"/> from the UIInitializer / Hud.Awake
    /// postfix (client-side only) to create the persistent screen manager.
    /// </summary>
    public static class GuildModule
    {
        public const string LogPrefix = "[GuildModule]";

        private static bool _initialized;
        private static bool _uiInitialized;

        // ── Server + shared init (called on both server and client) ───────────

        public static void Initialize()
        {
            FiresLogger.LogVerbose($"{LogPrefix} Initialize() entered — _initialized={_initialized}, ZRoutedRpc.instance={(ZRoutedRpc.instance != null ? "ready" : "NULL")}, ZNet.instance={(ZNet.instance != null ? (ZNet.instance.IsServer() ? "server" : "client") : "NULL")}");

            // Always call through to GuildSyncManager.Initialize — it owns the
            // logic for "did the ZRoutedRpc instance change since last
            // registration?" Short-circuiting here on _initialized would skip
            // re-registration after a world change / server reconnect, which is
            // exactly the bug that prevented the FullSync echo from reaching
            // the client. Achievements only need to register once.
            try
            {
                GuildSyncManager.Initialize();

                if (!_initialized)
                {
                    GuildAchievements.RegisterDefaults();
                    _initialized = true;
                    Debug.Log($"{LogPrefix} Initialized.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{LogPrefix} Initialize failed: {ex}");
            }
        }

        // ── Client-only UI init ───────────────────────────────────────────────

        public static void InitializeClientUI()
        {
            if (_uiInitialized) return;
            _uiInitialized = true;

            // VikingLands.Core (TDV) owns guilds — don't stand up our guild UI (screen + G hotkey) or
            // chat channel; they'd duplicate TDV's. Our guild-dependent features read TDV via RpgMakerApi.
            if (global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.GuildsDeferred)
            {
                Debug.Log($"{LogPrefix} Client UI SKIPPED — an external guild mod (TDV/FiresGuilds) owns guilds.");
                return;
            }

            try
            {
                GuildChatChannel.Initialize();
                GuildScreenManager.EnsureInstance();
                Debug.Log($"{LogPrefix} Client UI initialized.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{LogPrefix} InitializeClientUI failed: {ex}");
            }
        }

        // ── World change ──────────────────────────────────────────────────────

        public static void OnWorldLoaded()
        {
            // Server reloads from disk; client waits for FullSync RPC.
            if (ZNet.instance != null && ZNet.instance.IsServer())
                GuildSyncManager.ServerLoad();
        }

        public static void Shutdown()
        {
            GuildChatChannel.Shutdown();
        }

        // ── Console commands ──────────────────────────────────────────────────

        public static void HandleConsoleCommand(string[] args)
        {
            if (args == null || args.Length == 0) return;

            switch (args[0].ToLower())
            {
                case "guild":
                    GuildScreenManager.Instance?.Open();
                    break;
                case "gcreate":
                    if (args.Length >= 2) GuildAPI.CreateGuild(args[1], args.Length >= 3 ? args[2] : "");
                    break;
                case "ginvite":
                    if (args.Length >= 2) HandleInviteByName(args[1]);
                    break;
                case "gleave":
                    GuildAPI.LeaveGuild();
                    break;
                case "gdisband":
                    GuildAPI.DeleteGuild();
                    break;
                default:
                    global::Chat.instance?.AddString(
                        "<color=#aaa>[Guild]</color> Commands: /guild, /gcreate, /ginvite, /gleave, /gdisband");
                    break;
            }
        }

        private static void HandleInviteByName(string name)
        {
            var list = ZNet.instance?.GetPlayerList();
            if (list == null) return;
            foreach (var entry in list)
            {
                if (string.Equals(entry.m_name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    GuildAPI.InvitePlayer(entry.m_characterID.UserID, entry.m_name);
                    global::Chat.instance?.AddString($"<color=#aaa>[Guild]</color> Invite sent to {entry.m_name}.");
                    return;
                }
            }
            global::Chat.instance?.AddString($"<color=#aaa>[Guild]</color> Player '{name}' not found online.");
        }
    }
}
