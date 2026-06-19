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

            // Achievement + Leaderboard are GLOBAL (not NPC-tied as of 2026-06-08). Console openers — we'll
            // also add a journal entry as the proper opener once the journal screen is touched.
            new Terminal.ConsoleCommand("firesnpcs_leaderboard",
                "[optional profile name] - opens the Leaderboard panel on the Stats tab",
                args =>
                {
                    if (global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.Present)
                    {
                        args.Context?.AddString("Leaderboard is handled by VikingLands.Core — open it with its leaderboard hotkey (default L).");
                        return;
                    }
                    string profile = args.Length > 1 ? args[1] : "example";
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.LeaderboardPlayerPanel.Show(
                        profile, "Leaderboard",
                        global::FiresRPGmaker.UI.Screens.PlayerPanels.LeaderboardPlayerPanel.InnerTab.Stats);
                });
            new Terminal.ConsoleCommand("firesnpcs_achievements",
                "[optional profile name] - opens the Leaderboard panel on the Achievements tab",
                args =>
                {
                    if (global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.Present)
                    {
                        args.Context?.AddString("Achievements are handled by VikingLands.Core.");
                        return;
                    }
                    string profile = args.Length > 1 ? args[1] : "example";
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.AchievementPlayerPanel.Show(profile, "Achievements");
                });

            // Copy KG Marketplace .cfg files into our FiresRPGmaker/<Module>/ folders. Path arg is the KG
            // Marketplace\Configs folder; defaults to the standard dedicated-server install location.
            new Terminal.ConsoleCommand("firesnpcs_import_kg",
                "[optional path to Marketplace\\Configs] - copy KG Marketplace .cfg files into FiresRPGmaker folders + reload",
                args =>
                {
                    string src = args.Length > 1 ? string.Join(" ", args.Args, 1, args.Args.Length - 1)
                                                 : @"C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\BepInEx\config\Marketplace\Configs";
                    int copied = ImportKGConfigs(src, line => args.Context.AddString("[firesnpcs] " + line));
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

            // Probe the LIVE world (creatures, pickables, build categories, skills) and write baseline
            // AutoGen_*.cfg achievement files in the owner's tier style. Overwrites only AutoGen_* files.
            new Terminal.ConsoleCommand("firesnpcs_gen_achievements",
                "[optional profile name=server] - probe the live game (monsters/pickables/skills/build categories) + write baseline AutoGen_*.cfg achievements",
                args =>
                {
                    string profile = args.Length > 1 ? args[1] : "server";
                    string result = AchievementGenerator.Generate(profile, line => args.Context.AddString("[firesnpcs] " + line));
                    args.Context.AddString("[firesnpcs] " + result);
                });
        }

        // KG subfolder name -> our subfolder name (both relative to their respective config roots).
        // Same on both sides since MarketplacePaths uses KG-aligned folder names.
        private static readonly (string KgFolder, string OurFolder)[] _kgFolderMap = {
            (MarketplacePaths.Bankers,      MarketplacePaths.Bankers),
            (MarketplacePaths.Gamblers,     MarketplacePaths.Gamblers),
            (MarketplacePaths.Achievements, MarketplacePaths.Achievements),
            (MarketplacePaths.Transmogs,    MarketplacePaths.Transmogs),
        };

        public static int ImportKGConfigs(string srcRoot, System.Action<string> log = null)
        {
            int n = 0;
            void Say(string s) { (log ?? (l => UnityEngine.Debug.Log("[firesnpcs_import_kg] " + l)))(s); }
            if (string.IsNullOrEmpty(srcRoot) || !System.IO.Directory.Exists(srcRoot))
            { Say("source folder not found: " + srcRoot); return 0; }
            foreach (var (kg, ours) in _kgFolderMap)
            {
                string srcDir = System.IO.Path.Combine(srcRoot, kg);
                if (!System.IO.Directory.Exists(srcDir)) { Say("skip " + kg + " (no source folder)"); continue; }
                string dstDir = MarketplacePaths.GetFolder(ours);
                var files = System.IO.Directory.GetFiles(srcDir, "*.cfg", System.IO.SearchOption.TopDirectoryOnly);
                foreach (var f in files)
                {
                    string dst = System.IO.Path.Combine(dstDir, System.IO.Path.GetFileName(f));
                    try { System.IO.File.Copy(f, dst, overwrite: true); n++; }
                    catch (System.Exception ex) { Say(System.IO.Path.GetFileName(f) + " -> error " + ex.Message); }
                }
                Say(kg + ": " + files.Length + " file(s) copied -> " + dstDir);
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
