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

            new Terminal.ConsoleCommand("firesnpcs_dice",
                "[diceCount=2] - preview the 'Casting the Bones' dice minigame (client UI; no reward, no cost).",
                args =>
                {
                    int n = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 2;
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.DicePouchGame.Open(n, "The bones are cast!  (preview)",
                        () => Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "<color=#7cff7c>The bones are cast!</color> (preview)"));
                    args.Context?.AddString("[firesnpcs] dice minigame opened.");
                });

            new Terminal.ConsoleCommand("firesnpcs_wheel",
                "preview the 'Wheel of Fortune' minigame (client UI; no reward, no cost).",
                args =>
                {
                    var labels = new System.Collections.Generic.List<string> { "Wood", "Stone", "Coins", "Amber", "Ruby", "Feathers" };
                    int t = UnityEngine.Random.Range(0, labels.Count);
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.WheelGame.Open(labels, t, "You won:  " + labels[t] + "  (preview)",
                        () => Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "<color=#7cff7c>" + labels[t] + "!</color> (preview)"));
                    args.Context?.AddString("[firesnpcs] wheel minigame opened.");
                });

            new Terminal.ConsoleCommand("firesnpcs_duel",
                "preview 'The Gamble for Hísing' dice duel vs the Jarl (client UI; no reward, no cost).",
                args =>
                {
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.DuelGame.Open(
                        won => Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                            won ? "<color=#7cff7c>You take Hísing!</color> (preview)" : "<color=#ff8080>The Jarl wins.</color> (preview)"));
                    args.Context?.AddString("[firesnpcs] duel minigame opened.");
                });

            new Terminal.ConsoleCommand("firesnpcs_runes",
                "[runeCount=3] - preview the 'Cast the Runes' minigame (client UI; no reward, no cost).",
                args =>
                {
                    int n = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 3;
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.RunesGame.Open(n, "The runes are cast!  (preview)",
                        () => Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "<color=#7cff7c>The runes are cast!</color> (preview)"));
                    args.Context?.AddString("[firesnpcs] runes minigame opened.");
                });

            new Terminal.ConsoleCommand("firesnpcs_pokertest",
                "verify the Texas Hold'em hand evaluator (prints PASS/FAIL + a sample showdown).",
                args => args.Context?.AddString(global::FiresRPGmaker.Modules.Poker.PokerTest.Run()));

            new Terminal.ConsoleCommand("firesnpcs_chesstest",
                "[depth=4] - verify the chess move generator via perft (exact start-position node counts).",
                args =>
                {
                    int d = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 4;
                    args.Context?.AddString(global::FiresRPGmaker.Modules.Chess.ChessPerft.Run(d));
                });

            new Terminal.ConsoleCommand("firesnpcs_chess",
                "[depth] - open the chess board vs the AI (no arg = Chess/Difficulty config; depth 1-12 forces a fixed search depth).",
                args =>
                {
                    int d = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 0;
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.ChessTableGame.Open(d);
                    args.Context?.AddString("[firesnpcs] chess board opened (" + (d > 0 ? "AI depth " + d : "difficulty config") + ").");
                });

            new Terminal.ConsoleCommand("firesnpcs_fanoronatest",
                "verify the Fanorona rules engine (setup, captures, chains, playouts).",
                args => args.Context?.AddString(global::FiresRPGmaker.Modules.Fanorona.FanoronaTest.Run()));

            new Terminal.ConsoleCommand("firesnpcs_fanorona",
                "[depth=3] - open the Fanorona board vs the AI (depth 1-5 sets strength).",
                args =>
                {
                    int d = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 3;
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.FanoronaTableGame.Open(d);
                    args.Context?.AddString("[firesnpcs] fanorona board opened (AI depth " + d + ").");
                });

            new Terminal.ConsoleCommand("firesnpcs_pokersim",
                "[hands=20] - simulate all-AI Hold'em to verify the betting engine (chip conservation + last-hand log).",
                args =>
                {
                    int n = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 20;
                    args.Context?.AddString(global::FiresRPGmaker.Modules.Poker.PokerTest.Sim(n));
                });

            new Terminal.ConsoleCommand("firesnpcs_pokerdup",
                "[hands=500] - verify no card is ever dealt twice (or left undealt) across a run of all-AI hands.",
                args =>
                {
                    int n = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 500;
                    args.Context?.AddString(global::FiresRPGmaker.Modules.Poker.PokerTest.SimDup(n));
                });

            new Terminal.ConsoleCommand("firesnpcs_poker",
                "[startChips=1000] - open the Texas Hold'em table vs AI (client UI).",
                args =>
                {
                    int chips = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 1000;
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.Minigames.PokerTableGame.Open(chips, 10);
                    args.Context?.AddString("[firesnpcs] poker table opened.");
                });

            new Terminal.ConsoleCommand("firesnpcs_pokerqueue",
                "open the Hold'em multiplayer lobby (queue for a table).",
                args =>
                {
                    global::FiresRPGmaker.Modules.Poker.PokerLobby.EnsureRegistered();
                    global::FiresCore.Queue.FiresQueueLobby.Toggle(global::FiresRPGmaker.Modules.Poker.PokerLobby.QueueId);
                    args.Context?.AddString("[firesnpcs] poker lobby toggled.");
                });

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
                    if (global::FiresRPGmaker.Modules.Territories.TerritoryFileSync.RefuseIfNotAdmin(args.Context, "[firesnpcs]")) return;   // 0.0.505: admin only
                    string src = args.Length > 1 ? string.Join(" ", args.Args, 1, args.Args.Length - 1)
                                                 : @"C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\BepInEx\config\Marketplace\Configs";
                    int copied = ImportKGConfigs(src, line => args.Context.AddString("[firesnpcs] " + line));
                    int reloaded = ReloadModule("all");
                    args.Context.AddString("[firesnpcs] imported " + copied + " KG file(s) from '" + src + "', reloaded " + reloaded + " module(s).");
                });

            // Admin raw-file loop. Regular clients never hold config files (server data arrives
            // in-memory); an admin who wants to edit raw .cfg files pulls the server tree to local
            // disk, edits externally, then pushes the results back over the admin-validated
            // SyncFiles channel (server hot-reloads + rebroadcasts).
            new Terminal.ConsoleCommand("firesnpcs_pull_configs",
                "- (admin) download the server's raw config tree into this machine's FiresRPGmaker folders for external editing",
                args =>
                {
                    if (ZNet.instance == null) { args.Context?.AddString("[firesnpcs] no world loaded."); return; }
                    if (ZNet.instance.IsServer()) { args.Context?.AddString("[firesnpcs] you ARE the server - the files under config/FiresRPGmaker are already the canonical tree."); return; }
                    ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "FiresRPGmaker_PullConfigs", new ZPackage());
                    args.Context?.AddString("[firesnpcs] pull requested - files will land under config/FiresRPGmaker/ over the next seconds (admin only; non-admins get nothing).");
                });

            new Terminal.ConsoleCommand("firesnpcs_push_configs",
                "<folder|all> - (admin) push local FiresRPGmaker config file(s) back to the server (folders: Quests, QuestProfiles, QuestEvents, Dialogues, Traders, ServerInfos, Bankers, Gamblers, Transmogrifications, Mail, Buffers, BufferProfiles, LeaderboardAchievements, Territories)",
                args =>
                {
                    if (ZNet.instance == null) { args.Context?.AddString("[firesnpcs] no world loaded."); return; }
                    if (ZNet.instance.IsServer()) { args.Context?.AddString("[firesnpcs] you ARE the server - edits to config/FiresRPGmaker are already live (the watcher hot-reloads them)."); return; }
                    string[] pushable = { "Quests", "QuestProfiles", "QuestEvents", "Dialogues", "Traders", "ServerInfos",
                                          "Bankers", "Gamblers", "Transmogrifications", "Mail", "Buffers", "BufferProfiles",
                                          "LeaderboardAchievements", "Territories" };
                    string which = args.Length > 1 ? args[1] : "all";
                    int pushed = 0;
                    foreach (var folder in pushable)
                    {
                        if (!which.Equals("all", System.StringComparison.OrdinalIgnoreCase)
                            && !which.Equals(folder, System.StringComparison.OrdinalIgnoreCase)) continue;
                        string dir = FiresCore.Storage.FiresConfigPaths.GroupedPath(folder);
                        if (!System.IO.Directory.Exists(dir)) continue;
                        string[] files;
                        try { files = System.IO.Directory.GetFiles(dir, "*.cfg", System.IO.SearchOption.AllDirectories); }
                        catch { continue; }
                        foreach (var f in files)
                        {
                            try
                            {
                                string rel = f.Length > dir.Length ? f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/') : System.IO.Path.GetFileName(f);
                                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig(folder, rel, System.IO.File.ReadAllText(f));
                                pushed++;
                            }
                            catch (System.Exception ex) { args.Context?.AddString("[firesnpcs] " + System.IO.Path.GetFileName(f) + " -> error " + ex.Message); }
                        }
                    }
                    args.Context?.AddString("[firesnpcs] pushed " + pushed + " file(s) to the server (admin-validated there; server hot-reloads + rebroadcasts).");
                });

            // Force-regenerate example.cfg files from the latest in-code templates. Wipes user edits to
            // example.cfg specifically — useful when a sample profile's header (e.g. [basic] -> [example])
            // changes shape and the stale file would otherwise be kept.
            new Terminal.ConsoleCommand("firesnpcs_reset_examples",
                "- delete every config\\firesnpcs\\<module>\\example.cfg and regenerate the latest sample",
                args =>
                {
                    if (global::FiresRPGmaker.Modules.Territories.TerritoryFileSync.RefuseIfNotAdmin(args.Context, "[firesnpcs]")) return;   // 0.0.505: admin only
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
                    if (global::FiresRPGmaker.Modules.Territories.TerritoryFileSync.RefuseIfNotAdmin(args.Context, "[firesnpcs]")) return;   // 0.0.505: admin only
                    string profile = args.Length > 1 ? args[1] : "server";
                    string result = AchievementGenerator.Generate(profile, line => args.Context.AddString("[firesnpcs] " + line));
                    args.Context.AddString("[firesnpcs] " + result);
                });

            // Roll the leaderboard season: archive the current board as a finished season and wipe it for a new
            // one. Past seasons stay viewable via the in-panel season selector. Server-authoritative + admin-gated.
            new Terminal.ConsoleCommand("firesnpcs_leaderboard_newseason",
                "- archive the current leaderboard as a finished season and reset it for a new season (admin)",
                args =>
                {
                    if (ZNet.instance == null) { args.Context?.AddString("[firesnpcs] not connected to a world."); return; }
                    LeaderboardBoard.RequestRollSeason();
                    args.Context?.AddString(ZNet.instance.IsServer()
                        ? "[firesnpcs] season rolled — the live board was archived and reset."
                        : "[firesnpcs] requested a season roll from the server (requires admin); the board will refresh shortly.");
                });

            // Full factory reset: WIPE all leaderboard scores/skills/deaths/appearance AND the whole season history,
            // resetting to season 1 like a brand-new install. Keeps achievement definitions (.cfg). Unlike newseason
            // (which archives), nothing is recoverable. Server-authoritative + admin-gated.
            new Terminal.ConsoleCommand("firesnpcs_leaderboard_wipe",
                "- WIPE all leaderboard scores, skills, deaths, appearance + season history and reset to season 1 (admin; keeps achievement definitions)",
                args =>
                {
                    if (ZNet.instance == null) { args.Context?.AddString("[firesnpcs] not connected to a world."); return; }
                    LeaderboardBoard.RequestWipe();
                    LeaderboardTracker.WipeLocal(); // clear this client's legacy local CSV + cache
                    args.Context?.AddString(ZNet.instance.IsServer()
                        ? "[firesnpcs] leaderboard fully wiped — fresh season 1, boards cleared for all connected clients."
                        : "[firesnpcs] requested a full leaderboard wipe from the server (requires admin); boards will clear shortly.");
                });

            // Preview a firesbooks 3D book (default the animated opening book) rendered live to a centered panel —
            // for confirming the render/animation/orientation before wiring the book into the quest journal.
            new Terminal.ConsoleCommand("firesnpcs_bookview",
                "- preview an animated book. no arg=toggle; <PrefabName>; 'capture'; 'set y p distMul hMul ex ey ez'. Renders to BepInEx/config/FiresRPGmaker/Debug/BookView/",
                args =>
                {
                    global::FiresRPGmaker.UI.Books.BookViewWindow.Command(args.Args);
                    args.Context?.AddString("[firesnpcs] bookview command run — render snapshots + state written to BepInEx/config/FiresRPGmaker/Debug/BookView/");
                });
        }

        // KG Configs subfolder -> our flat sync key (GroupedPath resolves the physical grouped folder;
        // names align 1:1 by design). Every profile family a kg world can reference is covered; the
        // matching loader parses the kg .cfg format natively, so files copy verbatim — except Traders,
        // whose native parser needs well-formed headers (see NormalizeKgTraderCfg).
        private static readonly (string KgFolder, string OurFolder)[] _kgFolderMap = {
            ("Dialogues",               "Dialogues"),
            ("Quests",                  "Quests"),
            ("QuestProfiles",           "QuestProfiles"),
            ("QuestEvents",             "QuestEvents"),
            ("Traders",                 "Traders"),
            ("Bankers",                 "Bankers"),
            ("Gamblers",                "Gamblers"),
            ("Transmogrifications",     "Transmogrifications"),
            ("LeaderboardAchievements", "LeaderboardAchievements"),
            ("ServerInfos",             "ServerInfos"),
            ("Buffers",                 "Buffers"),
            ("BufferProfiles",          "BufferProfiles"),
            ("Territories",             "Territories/legacy"),
            ("Mail",                    "Mail"),
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
                // Recursive: kg trees nest profile packs in subfolders (e.g. Dialogues\Asgavin\).
                // Relative subpaths are preserved; zero-byte placeholders are skipped (and counted,
                // so "did it skip something?" is answerable from the report alone).
                var files = System.IO.Directory.GetFiles(srcDir, "*.cfg", System.IO.SearchOption.AllDirectories);
                int copied = 0, empty = 0;
                foreach (var f in files)
                {
                    try
                    {
                        if (new System.IO.FileInfo(f).Length == 0) { empty++; continue; }
                        string rel = f.Substring(srcDir.Length).TrimStart('\\', '/');
                        string dst = System.IO.Path.Combine(dstDir, rel);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst));
                        if (ours == "Traders")
                            System.IO.File.WriteAllText(dst, NormalizeKgTraderCfg(System.IO.File.ReadAllText(f)));
                        else
                            System.IO.File.Copy(f, dst, overwrite: true);
                        copied++; n++;
                    }
                    catch (System.Exception ex) { Say(System.IO.Path.GetFileName(f) + " -> error " + ex.Message); }
                }
                Say(kg + ": " + copied + " file(s) -> " + dstDir + (empty > 0 ? " (+" + empty + " zero-byte placeholder(s) skipped)" : ""));
            }

            // Surface anything the map does NOT cover that actually has content, so an unfamiliar
            // server's extra folders never vanish silently. (Marketplace_SavedNPCs templates and
            // the SavedData DB are separate pipelines by design.)
            try
            {
                foreach (var dir in System.IO.Directory.GetDirectories(srcRoot))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    bool mapped = false;
                    foreach (var (kg, _) in _kgFolderMap) if (string.Equals(kg, name, System.StringComparison.OrdinalIgnoreCase)) { mapped = true; break; }
                    if (mapped) continue;
                    int contentFiles = 0;
                    foreach (var f in System.IO.Directory.GetFiles(dir, "*", System.IO.SearchOption.AllDirectories))
                    {
                        if (System.IO.Path.GetFileName(f).Equals(".autoExclude", System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (new System.IO.FileInfo(f).Length > 0) contentFiles++;
                    }
                    if (contentFiles > 0)
                        Say("NOT IMPORTED: folder '" + name + "' has " + contentFiles + " file(s) but no matching Fires module mapping");
                }
            }
            catch { }
            return n;
        }

        /// <summary>kg trader headers may lack the closing ']' and may carry an "=Flag" suffix on the
        /// name; our native TraderProfileManager parser (which runs on clients from the synced copies)
        /// needs well-formed "[name,opts]" headers, and kg NPC ZDO profile references are lowercased —
        /// normalize both so converted files resolve identically everywhere. Trade lines pass verbatim
        /// (same exchange format on both sides).</summary>
        private static string NormalizeKgTraderCfg(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length + 32);
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string t = raw.Trim();
                if (t.StartsWith("[") && t.Length > 1)
                {
                    string inner = t.Substring(1);
                    int close = inner.IndexOf(']');
                    if (close >= 0) inner = inner.Substring(0, close);
                    var parts = inner.Split(',');
                    string name = parts[0];
                    int eq = name.IndexOf('=');
                    if (eq >= 0) name = name.Substring(0, eq);
                    parts[0] = name.Trim().ToLowerInvariant();
                    sb.Append('[').Append(string.Join(",", parts)).Append(']').Append('\n');
                }
                else sb.Append(raw.TrimEnd('\r')).Append('\n');
            }
            return sb.ToString();
        }

        private static int DeleteExampleFiles()
        {
            int n = 0;
            string root = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "FiresRPGmaker");
            if (!System.IO.Directory.Exists(root)) return 0;
            foreach (var dir in System.IO.Directory.GetDirectories(root))
            {
                var f = System.IO.Path.Combine(dir, "example.cfg");
                if (System.IO.File.Exists(f)) { try { System.IO.File.Delete(f); n++; } catch { } }
            }
            return n;
        }

        internal static int ReloadModule(string which)
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
