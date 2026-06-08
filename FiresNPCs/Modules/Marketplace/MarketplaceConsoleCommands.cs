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

            // Copy KG Marketplace .cfg files into our firesnpcs/<module>/ folders. Path arg is the KG
            // Marketplace\Configs folder; defaults to the standard dedicated-server install location.
            new Terminal.ConsoleCommand("firesnpcs_import_kg",
                "[optional path to Marketplace\\Configs] - copy KG Marketplace .cfg files into firesnpcs folders + reload",
                args =>
                {
                    string src = args.Length > 1 ? string.Join(" ", args.Args, 1, args.Args.Length - 1)
                                                 : @"C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\BepInEx\config\Marketplace\Configs";
                    int copied = ImportKGConfigs(src);
                    int reloaded = ReloadModule("all");
                    args.Context.AddString("[firesnpcs] imported " + copied + " KG file(s) from '" + src + "', reloaded " + reloaded + " module(s).");
                });

            // Force-regenerate example.cfg files from the latest in-code templates. Wipes user edits to
            // example.cfg specifically — useful when a sample profile's header (e.g. [basic] -> [example])
            // changes shape and the stale file would otherwise be kept.
            new Terminal.ConsoleCommand("firesnpcs_reset_examples",
                "- delete every config\\firesnpcs\\<module>\\example.cfg and regenerate the latest sample",
                args =>
                {
                    int deleted = DeleteExampleFiles();
                    int reloaded = ReloadModule("all"); // triggers WriteExampleProfile for each missing example
                    args.Context.AddString("[firesnpcs] reset " + deleted + " example.cfg file(s), reloaded " + reloaded + " module(s).");
                });
        }

        private static readonly (string KgFolder, string OurFolder)[] _kgFolderMap = {
            ("Bankers",                  "banker"),
            ("Gamblers",                 "gambler"),
            ("LeaderboardAchievements",  "achievement"),
            ("Transmogrifications",      "transmog"),
        };

        private static int ImportKGConfigs(string srcRoot)
        {
            int n = 0;
            if (string.IsNullOrEmpty(srcRoot) || !System.IO.Directory.Exists(srcRoot))
            {
                UnityEngine.Debug.LogWarning("[firesnpcs_import_kg] source folder not found: " + srcRoot);
                return 0;
            }
            foreach (var (kg, ours) in _kgFolderMap)
            {
                string srcDir = System.IO.Path.Combine(srcRoot, kg);
                if (!System.IO.Directory.Exists(srcDir)) continue;
                string dstDir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs", ours);
                System.IO.Directory.CreateDirectory(dstDir);
                foreach (var f in System.IO.Directory.GetFiles(srcDir, "*.cfg", System.IO.SearchOption.TopDirectoryOnly))
                {
                    string dst = System.IO.Path.Combine(dstDir, System.IO.Path.GetFileName(f));
                    try { System.IO.File.Copy(f, dst, overwrite: true); n++; }
                    catch (System.Exception ex) { UnityEngine.Debug.LogError("[firesnpcs_import_kg] " + f + ": " + ex.Message); }
                }
            }
            return n;
        }

        private static int DeleteExampleFiles()
        {
            int n = 0;
            string root = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs");
            if (!System.IO.Directory.Exists(root)) return 0;
            foreach (var dir in System.IO.Directory.GetDirectories(root))
            {
                var f = System.IO.Path.Combine(dir, "example.cfg");
                if (System.IO.File.Exists(f)) { try { System.IO.File.Delete(f); n++; } catch { } }
            }
            return n;
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
