using HarmonyLib;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Registers <c>firesnpcs_reload &lt;module&gt;</c> on Valheim's Terminal so admins can hot-reload .cfg
    /// data after editing without restarting the game. Patches Terminal.InitTerminal to install the command
    /// once on startup.
    ///
    /// Usage:
    ///   firesnpcs_reload gambler
    ///   firesnpcs_reload marketplace
    ///   firesnpcs_reload achievement
    ///   firesnpcs_reload leaderboard
    ///   firesnpcs_reload mail
    ///   firesnpcs_reload transmog
    ///   firesnpcs_reload banker
    ///   firesnpcs_reload all
    /// </summary>
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    public static class MarketplaceConsoleCommands
    {
        private static bool _registered;

        [HarmonyPostfix]
        private static void Register()
        {
            if (_registered) return;
            _registered = true;
            new Terminal.ConsoleCommand("firesnpcs_reload",
                "[gambler|marketplace|achievement|leaderboard|mail|transmog|banker|all] - reload Marketplace-module .cfg files from disk",
                args =>
                {
                    string which = args.Length > 1 ? args[1].ToLowerInvariant() : "all";
                    int count = ReloadModule(which);
                    args.Context.AddString("[firesnpcs] reloaded " + count + " module(s) for '" + which + "'.");
                },
                optionsFetcher: () => new System.Collections.Generic.List<string>
                { "gambler", "marketplace", "achievement", "leaderboard", "mail", "transmog", "banker", "all" });
        }

        private static int ReloadModule(string which)
        {
            int n = 0;
            void Run(System.Action a) { try { a(); n++; } catch (System.Exception ex) { UnityEngine.Debug.LogError(ex); } }

            switch (which)
            {
                case "gambler":     Run(GamblerProfileManager.ReloadAll); break;
                case "marketplace": Run(MarketplaceListingManager.ReloadAll); break;
                case "achievement": Run(AchievementProfileManager.ReloadAll); break;
                case "leaderboard": Run(LeaderboardProfileManager.ReloadAll); break;
                case "mail":        Run(MailProfileManager.ReloadAll); break;
                case "transmog":    Run(TransmogProfileManager.ReloadAll); break;
                case "banker":      Run(BankerProfileManager.ReloadAll); break;
                case "all":
                default:
                    Run(GamblerProfileManager.ReloadAll);
                    Run(MarketplaceListingManager.ReloadAll);
                    Run(AchievementProfileManager.ReloadAll);
                    Run(LeaderboardProfileManager.ReloadAll);
                    Run(MailProfileManager.ReloadAll);
                    Run(TransmogProfileManager.ReloadAll);
                    Run(BankerProfileManager.ReloadAll);
                    break;
            }
            return n;
        }
    }
}
