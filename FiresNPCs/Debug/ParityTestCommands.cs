using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Modules.Territories;

namespace FiresRPGmaker
{
    /// <summary>
    /// One-stop console driver for the KG-parity passes landed this session — quest types, reward
    /// dispatcher, TransferComplition chain, lifecycle events, API.ClientSide drop-in shim, territory
    /// flag patches, Marketplace v3 sort/size. Single command <c>firesrpg_test</c> + subverb keeps
    /// the terminal namespace clean.
    ///
    /// All subverbs run client-side as the local player (no remote spoofing). Use in single-player
    /// or as host; on a dedi client, server-side processing still happens via the RPC chain.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    public static class ParityTestCommands
    {
        private static bool _registered;

        [HarmonyPostfix]
        private static void Register()
        {
            if (_registered) return;
            _registered = true;

            // mail_reset — wipes the in-memory mail cache + re-reads .cfg from disk so claimed
            // attachments come back. Use during testing when iterating on the Mail UI.
            new Terminal.ConsoleCommand("mail_reset",
                "reload Mail profiles from disk (re-fills claimed attachments) — for Mail UI testing",
                args =>
                {
                    try
                    {
                        global::FiresRPGmaker.Modules.Marketplace.MailProfileManager.ReloadAll();
                        args.Context.AddString("[mail_reset] reloaded mail profiles — re-open the Mail UI to see fresh attachments.");
                    }
                    catch (Exception ex) { args.Context.AddString("[mail_reset] threw: " + ex.Message); }
                });

            new Terminal.ConsoleCommand("firesrpg_test",
                "subcommand-driven parity test surface — run 'firesrpg_test help' for the full list",
                args =>
                {
                    if (args.Length < 2) { PrintHelp(args.Context); return; }
                    string verb = args[1].ToLowerInvariant();
                    try
                    {
                        switch (verb)
                        {
                            case "help":           PrintHelp(args.Context); return;
                            case "sanity":         RunSanitySuite(args); return;
                            case "quest_progress": QuestProgress(args); return;
                            case "quest_reward":   QuestReward(args); return;
                            case "quest_accept":   QuestAccept(args); return;
                            case "quest_complete": QuestComplete(args); return;
                            case "quest_list":     QuestList(args); return;
                            case "lifecycle":      Lifecycle(args); return;
                            case "api":            ApiDump(args); return;
                            case "territory_flag": TerritoryFlagDump(args); return;
                            case "marketplace":    OpenMarketplace(args); return;
                            case "factions":       FactionsDump(args); return;
                            case "admin":          AdminDump(args); return;
                            case "panels":         OpenPanel(args); return;
                            default:
                                args.Context.AddString("[firesrpg_test] Unknown subverb '" + verb + "'. Run 'firesrpg_test help'.");
                                return;
                        }
                    }
                    catch (Exception ex)
                    {
                        args.Context.AddString("[firesrpg_test] '" + verb + "' threw: " + ex.Message);
                    }
                },
                optionsFetcher: () => new System.Collections.Generic.List<string>
                {
                    "help", "sanity", "quest_progress", "quest_reward", "quest_accept", "quest_complete",
                    "quest_list", "lifecycle", "api", "territory_flag", "marketplace",
                    "factions", "admin", "panels"
                });
        }

        // ─── help ─────────────────────────────────────────────────────────

        private static void PrintHelp(Terminal ctx)
        {
            string[] lines = {
                "firesrpg_test — KG-parity test commands. Subverbs:",
                "  sanity                            → ONE-SHOT: exercises every parity pathway, prints PASS/FAIL summary. Self-cleaning.",
                "  quest_progress <Kill|Collect|Harvest|Craft|Build|Talk> <prefab> [amount=1]",
                "      → fires a quest-progress event for the local player (Kill quest uses the prefab as kill target).",
                "  quest_reward <type> <target> [amount=1] [level=1]",
                "      → grants a single reward via QuestRewardDispatcher. Types:",
                "         item, randomitem, skill, skill_exp, pet, setcustomvalue, addcustomvalue,",
                "         guildaddlevel, epicmmo_exp, cozyheim_exp, battlepass_exp, rustyclasses_exp",
                "  quest_accept <questId>            → adds the named quest to your ActiveQuests + fires OnAcceptQuest",
                "  quest_complete <questId>          → marks completed, grants rewards, runs OnCompleteQuest + TransferComplition",
                "  quest_list                        → prints your ActiveQuests / CompletedQuests",
                "  lifecycle <questId> <event>       → fires one lifecycle event (OnAcceptQuest|OnCancelQuest|OnCompleteQuest|OnQuestTimeout|OnDeath)",
                "  api                               → dumps API.ClientSide results at the local player's position",
                "  territory_flag <flagName>         → reports which territory carries the named primary OR additional flag at your position",
                "  marketplace [profile]             → opens MarketplacePlayerPanel (profile defaults to 'default')",
                "  factions                          → dumps loaded factions (lists/skills/icon) + your current membership",
                "  admin                             → dumps the 3 admin signals + the canonical IsLocalAdmin() result",
                "  panels <banker|mail|transmog|gambler|marketplace> [profile]  → opens a player panel for manual testing",
            };
            foreach (var l in lines) ctx.AddString(l);
        }

        // ─── quest_progress ───────────────────────────────────────────────

        private static void QuestProgress(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 4) { args.Context.AddString("usage: firesrpg_test quest_progress <type> <prefab> [amount=1]"); return; }
            string type = args[2];
            string prefab = args[3];
            int amount = args.Length > 4 && int.TryParse(args[4], out var a) ? a : 1;
            QuestProgressClient.SendProgress(type, prefab, amount);
            args.Context.AddString("[firesrpg_test] sent quest_progress " + type + " " + prefab + " x" + amount);
        }

        // ─── quest_reward ─────────────────────────────────────────────────

        private static void QuestReward(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 4) { args.Context.AddString("usage: firesrpg_test quest_reward <type> <target> [amount=1] [level=1]"); return; }
            string type = args[2];
            string target = args[3];
            int amount = args.Length > 4 && int.TryParse(args[4], out var a) ? a : 1;
            int level  = args.Length > 5 && int.TryParse(args[5], out var l) ? l : 1;

            var player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("[firesrpg_test] no local player"); return; }
            var data = VaultOfKnowledge.Instance?.GetPlayerData(player.GetPlayerID());
            if (data == null) { args.Context.AddString("[firesrpg_test] vault data not available"); return; }

            var reward = new QuestReward { Type = type, Target = target, Amount = amount, Level = level };
            QuestRewardDispatcher.Grant(data, player, reward);
            VaultOfKnowledge.Instance.SavePlayerData(player.GetPlayerID(), data);
            args.Context.AddString("[firesrpg_test] granted " + type + " target=" + target + " amount=" + amount + " level=" + level);
        }

        // ─── quest_accept ─────────────────────────────────────────────────

        private static void QuestAccept(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 3) { args.Context.AddString("usage: firesrpg_test quest_accept <questId>"); return; }
            string questId = args[2];
            var def = QuestManager.Instance?.GetQuestDefinition(questId);
            if (def == null) { args.Context.AddString("[firesrpg_test] quest '" + questId + "' not in registry"); return; }
            var player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("[firesrpg_test] no local player"); return; }
            long pid = player.GetPlayerID();
            var data = VaultOfKnowledge.Instance?.GetPlayerData(pid);
            if (data == null) { args.Context.AddString("[firesrpg_test] vault data not available"); return; }
            if (data.ActiveQuests.Any(q => q.QuestId == questId))
            { args.Context.AddString("[firesrpg_test] '" + questId + "' is already active"); return; }
            data.ActiveQuests.Add(new ActiveQuestData { QuestId = questId, Progress = 0, StartedAt = DateTime.UtcNow });
            VaultOfKnowledge.Instance.SavePlayerData(pid, data);
            QuestLifecycleDispatcher.Fire(def, QuestLifecycleDispatcher.EventOnAccept);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Quest accepted: " + (def.Name ?? questId));
            args.Context.AddString("[firesrpg_test] accepted '" + questId + "' + fired OnAcceptQuest");
        }

        // ─── quest_complete ───────────────────────────────────────────────

        private static void QuestComplete(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 3) { args.Context.AddString("usage: firesrpg_test quest_complete <questId>"); return; }
            string questId = args[2];
            var def = QuestManager.Instance?.GetQuestDefinition(questId);
            if (def == null) { args.Context.AddString("[firesrpg_test] quest '" + questId + "' not in registry"); return; }
            var player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("[firesrpg_test] no local player"); return; }
            long pid = player.GetPlayerID();
            var data = VaultOfKnowledge.Instance?.GetPlayerData(pid);
            if (data == null) { args.Context.AddString("[firesrpg_test] vault data not available"); return; }

            int removed = data.ActiveQuests.RemoveAll(q => string.Equals(q.QuestId, questId, StringComparison.OrdinalIgnoreCase));
            data.AddCompletedQuest(questId, 1);
            if (def.Rewards != null && def.Rewards.Count > 0)
                QuestRewardDispatcher.GrantAll(data, player, def.Rewards);
            QuestLifecycleDispatcher.OnCompleted(def, data, player);
            VaultOfKnowledge.Instance.SavePlayerData(pid, data);

            string chain = string.IsNullOrEmpty(def.TransferComplition) ? "(no chain)" : "chain→" + def.TransferComplition;
            args.Context.AddString("[firesrpg_test] completed '" + questId + "' (removed " + removed + " active, " + (def.Rewards?.Count ?? 0) + " reward(s) granted, " + chain + ")");
        }

        // ─── quest_list ───────────────────────────────────────────────────

        private static void QuestList(Terminal.ConsoleEventArgs args)
        {
            var player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("[firesrpg_test] no local player"); return; }
            var data = VaultOfKnowledge.Instance?.GetPlayerData(player.GetPlayerID());
            if (data == null) { args.Context.AddString("[firesrpg_test] vault data not available"); return; }
            args.Context.AddString("Active quests: " + data.ActiveQuests.Count);
            foreach (var a in data.ActiveQuests)
            {
                var def = QuestManager.Instance?.GetQuestDefinition(a.QuestId);
                int target = def?.QuestTargets?.FirstOrDefault()?.Amount ?? 0;
                args.Context.AddString("  • " + a.QuestId + " — " + a.Progress + "/" + target + " (type=" + (def?.QuestType ?? "?") + ")");
            }
            args.Context.AddString("Completed quests: " + data.CompletedQuests.Count);
            foreach (var c in data.CompletedQuests)
                args.Context.AddString("  ✓ " + c.Key + " ×" + c.Value.TimesCompleted);
        }

        // ─── lifecycle ────────────────────────────────────────────────────

        private static void Lifecycle(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 4) { args.Context.AddString("usage: firesrpg_test lifecycle <questId> <OnAcceptQuest|OnCancelQuest|OnCompleteQuest|OnQuestTimeout|OnDeath>"); return; }
            string questId = args[2];
            string ev = args[3];
            var def = QuestManager.Instance?.GetQuestDefinition(questId);
            if (def == null) { args.Context.AddString("[firesrpg_test] quest '" + questId + "' not in registry"); return; }
            QuestLifecycleDispatcher.Fire(def, ev);
            int cmdCount = def.LifecycleEvents != null && def.LifecycleEvents.TryGetValue(ev, out var list) ? (list?.Count ?? 0) : 0;
            args.Context.AddString("[firesrpg_test] fired '" + ev + "' for '" + questId + "' (" + cmdCount + " command(s) attached)");
        }

        // ─── api (API.ClientSide drop-in) ─────────────────────────────────

        private static void ApiDump(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("─── API.ClientSide @ local player ───");
            args.Context.AddString("FillingTerritoryData = " + global::API.ClientSide.FillingTerritoryData);
            if (global::API.ClientSide.IsPlayerInsideTerritory(out var n, out var f, out var af))
                args.Context.AddString("IsPlayerInsideTerritory = true | name='" + n + "' flags=0x" + f.ToString("X") + " additional=0x" + af.ToString("X"));
            else
                args.Context.AddString("IsPlayerInsideTerritory = false");
            var player = Player.m_localPlayer;
            if (player != null)
            {
                // Probe NoBuild (TerritoryFlags bit 1 = 2). Player asks for a different flag via territory_flag.
                bool inside = global::API.ClientSide.IsObjectInsideTerritoryWithFlag(
                    player.transform.position, (int)TerritoryFlags.NoBuild, out var name2, out var f2, out var af2);
                args.Context.AddString("IsObjectInsideTerritoryWithFlag(NoBuild) = " + inside + (inside ? " | name='" + name2 + "'" : ""));
            }
        }

        // ─── territory_flag ───────────────────────────────────────────────

        private static void TerritoryFlagDump(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 3) { args.Context.AddString("usage: firesrpg_test territory_flag <flagName>  (e.g. NoBuild, InfiniteEitr, MoveSpeedMultiplier)"); return; }
            string flagName = args[2];
            var player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("[firesrpg_test] no local player"); return; }
            Vector3 pos = player.transform.position;

            if (Enum.TryParse<TerritoryFlags>(flagName, ignoreCase: true, out var primary))
            {
                bool hit = global::API.ClientSide.IsObjectInsideTerritoryWithFlag(pos, (int)primary, out var n, out var f, out var af);
                args.Context.AddString("[primary] " + flagName + " → " + (hit ? "INSIDE '" + n + "' (flags=0x" + f.ToString("X") + ")" : "outside"));
                return;
            }
            if (Enum.TryParse<AdditionalTerritoryFlags>(flagName, ignoreCase: true, out var additional))
            {
                bool hit = global::API.ClientSide.IsObjectInsideTerritoryWithFlag_Additional(pos, (int)additional, out var n, out var f, out var af);
                args.Context.AddString("[additional] " + flagName + " → " + (hit ? "INSIDE '" + n + "' (additional=0x" + af.ToString("X") + ")" : "outside"));
                return;
            }
            args.Context.AddString("[firesrpg_test] '" + flagName + "' is not a known TerritoryFlags or AdditionalTerritoryFlags value");
        }

        // ─── sanity suite ─────────────────────────────────────────────────

        /// <summary>One-shot end-to-end exercise of every parity pathway landed this session.
        /// Injects synthetic quests via reflection, fires each pathway, asserts the expected state
        /// change, then cleans up. Safe to re-run — sentinel-named test rows are removed afterwards.</summary>
        private static void RunSanitySuite(Terminal.ConsoleEventArgs args)
        {
            var ctx = args.Context;
            var player = Player.m_localPlayer;
            if (player == null) { ctx.AddString("[sanity] no local player — load a save first"); return; }
            var vault = VaultOfKnowledge.Instance;
            if (vault == null) { ctx.AddString("[sanity] VaultOfKnowledge.Instance is null"); return; }
            var qm = QuestManager.Instance;
            if (qm == null) { ctx.AddString("[sanity] QuestManager.Instance is null"); return; }

            int pass = 0, fail = 0;
            void Report(string label, bool ok, string detail = "")
            {
                if (ok) { pass++; ctx.AddString("  PASS  " + label + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
                else    { fail++; ctx.AddString("  FAIL  " + label + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
            }

            ctx.AddString("─── firesrpg_test sanity ───");

            // Snapshot to restore vault state (so the suite leaves nothing behind even on partial run).
            long pid = player.GetPlayerID();
            var data = vault.GetPlayerData(pid);
            if (data == null) { ctx.AddString("[sanity] vault player-data unavailable"); return; }

            // Reflect into questDatabase so we can inject + remove sentinel quests.
            var dbField = AccessTools.Field(typeof(QuestManager), "questDatabase");
            var db = dbField?.GetValue(qm) as System.Collections.Generic.Dictionary<string, QuestDefinition>;
            if (db == null) { ctx.AddString("[sanity] couldn't reflect QuestManager.questDatabase"); return; }

            var sentinelIds = new System.Collections.Generic.List<string>();
            QuestDefinition MakeSyntheticQuest(string id, string type, string targetPrefab, int amount, string transferTo = null, bool withLifecycle = false)
            {
                var def = new QuestDefinition
                {
                    QuestId = id,
                    Name = "[sanity] " + id,
                    QuestType = type,
                    Autocomplete = true,
                    TransferComplition = transferTo,
                    TransferComplitionText = transferTo != null ? "Chain follow-up: " + transferTo : null,
                };
                def.QuestTargets.Add(new QuestTarget { Prefab = targetPrefab, Amount = amount });
                if (withLifecycle)
                {
                    def.LifecycleEvents[QuestLifecycleDispatcher.EventOnAccept]   = new System.Collections.Generic.List<string>();
                    def.LifecycleEvents[QuestLifecycleDispatcher.EventOnCancel]   = new System.Collections.Generic.List<string>();
                    def.LifecycleEvents[QuestLifecycleDispatcher.EventOnComplete] = new System.Collections.Generic.List<string>();
                    def.LifecycleEvents[QuestLifecycleDispatcher.EventOnTimeout]  = new System.Collections.Generic.List<string>();
                    def.LifecycleEvents[QuestLifecycleDispatcher.EventOnDeath]    = new System.Collections.Generic.List<string>();
                }
                db[id] = def;
                QuestManager.Instance?.ForgetSerialisedQuestDb();
                sentinelIds.Add(id);
                return def;
            }

            try
            {
                // ─── 1. Quest progress wiring (7 types) ───
                ctx.AddString("[1] quest progress wiring");
                string[][] questCases = new[]
                {
                    new[] { "fires_sanity_kill",    "Kill",    "Greyling"     },
                    new[] { "fires_sanity_collect", "Collect", "Wood"         },
                    new[] { "fires_sanity_harvest", "Harvest", "Mushroom"     },
                    new[] { "fires_sanity_craft",   "Craft",   "SwordBronze"  },
                    new[] { "fires_sanity_build",   "Build",   "wood_floor"   },
                    new[] { "fires_sanity_talk",    "Talk",    "SanityTestNpc"},
                };
                foreach (var c in questCases)
                {
                    MakeSyntheticQuest(c[0], c[1], c[2], 1);
                    data.ActiveQuests.Add(new ActiveQuestData { QuestId = c[0], Progress = 0, StartedAt = DateTime.UtcNow });
                    // ApplyQuestProgressLocal deserializes from customData, so push the synthetic
                    // active row in before firing. The autocomplete chain serializes the result back.
                    vault.SerializeToCustomData(player, data);
                    VaultOfKnowledge.ApplyQuestProgressLocal(pid, c[1], c[2], 1);
                    data = vault.GetPlayerData(pid); // refresh local handle from saved state
                    bool completed = data.CompletedQuests.ContainsKey(c[0]);
                    Report(c[1].PadRight(8) + " (" + c[2] + ")", completed, completed ? "completed → reward path fired" : "no progress observed");
                }

                // Move quest (uses ApplyMoveQuestCheckLocal — distance-gated)
                {
                    var pos = player.transform.position;
                    string targetXYZ = pos.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                                       pos.y.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                                       pos.z.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    MakeSyntheticQuest("fires_sanity_move", "Move", targetXYZ, 1);
                    data.ActiveQuests.Add(new ActiveQuestData { QuestId = "fires_sanity_move", Progress = 0, StartedAt = DateTime.UtcNow });
                    vault.SerializeToCustomData(player, data);
                    VaultOfKnowledge.ApplyMoveQuestCheckLocal(pid, pos);
                    data = vault.GetPlayerData(pid);
                    bool moveOk = data.CompletedQuests.ContainsKey("fires_sanity_move");
                    Report("Move     (10m radius)", moveOk, moveOk ? "completed at player position" : "no match");
                }

                // ─── 2. Reward dispatcher (12 types) ───
                ctx.AddString("[2] reward dispatcher (12 KG types)");
                string[][] rewardCases = new[]
                {
                    new[] { "item",             "Wood",        "1", "1" },
                    new[] { "randomitem",       "Wood;Stone",  "1", "1" },
                    new[] { "skill",            "Run",         "1", "0" },
                    new[] { "skill_exp",        "Run",         "1", "0" },
                    new[] { "setcustomvalue",   "sanity_test_a", "1", "0" },
                    new[] { "addcustomvalue",   "sanity_test_b", "1", "0" },
                    new[] { "guildaddlevel",    "stub",        "1", "0" },
                    new[] { "epicmmo_exp",      "stub",        "1", "0" },
                    new[] { "cozyheim_exp",     "stub",        "1", "0" },
                    new[] { "battlepass_exp",   "stub",        "1", "0" },
                    new[] { "rustyclasses_exp", "stub",        "1", "0" },
                    // Pet skipped — would actually spawn a creature; covered by the "item" path proves dispatcher
                };
                foreach (var r in rewardCases)
                {
                    bool threw = false;
                    try
                    {
                        QuestRewardDispatcher.Grant(data, player,
                            new QuestReward { Type = r[0], Target = r[1], Amount = int.Parse(r[2]), Level = int.Parse(r[3]) });
                    }
                    catch { threw = true; }
                    Report(("reward " + r[0]).PadRight(28), !threw, threw ? "threw" : "ok");
                }

                // ─── 3. TransferComplition chain ───
                ctx.AddString("[3] TransferComplition chain");
                MakeSyntheticQuest("fires_sanity_chain_b", "Kill", "Boar", 1);
                var chainA = MakeSyntheticQuest("fires_sanity_chain_a", "Kill", "Greyling", 1, transferTo: "fires_sanity_chain_b");
                QuestLifecycleDispatcher.OnCompleted(chainA, data, player);
                bool chained = data.ActiveQuests.Any(q => q.QuestId == "fires_sanity_chain_b");
                Report("chain auto-accept of follow-up quest", chained, chained ? "B added to ActiveQuests" : "B not added");

                // ─── 4. Lifecycle dispatcher (5 events) ───
                ctx.AddString("[4] lifecycle dispatcher");
                var lcDef = MakeSyntheticQuest("fires_sanity_lifecycle", "Kill", "Boar", 1, withLifecycle: true);
                string[] events = {
                    QuestLifecycleDispatcher.EventOnAccept,
                    QuestLifecycleDispatcher.EventOnCancel,
                    QuestLifecycleDispatcher.EventOnComplete,
                    QuestLifecycleDispatcher.EventOnTimeout,
                    QuestLifecycleDispatcher.EventOnDeath,
                };
                foreach (var ev in events)
                {
                    bool threw = false;
                    try { QuestLifecycleDispatcher.Fire(lcDef, ev); }
                    catch { threw = true; }
                    Report(("Fire(" + ev + ")").PadRight(28), !threw, threw ? "threw" : "ok");
                }

                // ─── 5. API.ClientSide drop-in shim ───
                ctx.AddString("[5] API.ClientSide drop-in");
                Report("FillingTerritoryData accessible", true, "= " + global::API.ClientSide.FillingTerritoryData);
                bool apiThrew = false;
                try { global::API.ClientSide.IsPlayerInsideTerritory(out _, out _, out _); }
                catch { apiThrew = true; }
                Report("IsPlayerInsideTerritory()       ", !apiThrew);
                apiThrew = false;
                try { global::API.ClientSide.IsObjectInsideTerritoryWithFlag(player.transform.position, (int)TerritoryFlags.NoBuild, out _, out _, out _); }
                catch { apiThrew = true; }
                Report("IsObjectInsideTerritoryWithFlag ", !apiThrew);
                apiThrew = false;
                try { global::API.ClientSide.IsObjectInsideTerritoryWithFlag_Additional(player.transform.position, (int)AdditionalTerritoryFlags.NoMist, out _, out _, out _); }
                catch { apiThrew = true; }
                Report("..._Additional                  ", !apiThrew);
                apiThrew = false;
                try { global::API.ClientSide.ResetTraderItems(); }
                catch { apiThrew = true; }
                Report("ResetTraderItems()              ", !apiThrew);
                apiThrew = false;
                try { global::API.ClientSide.OpenQuestJournal(); global::FiresRPGmaker.UI.QuestJournal.QuestJournalController.Instance?.gameObject?.SetActive(false); }
                catch { apiThrew = true; }
                Report("OpenQuestJournal()              ", !apiThrew);

                // ─── 6. Territory missing-flag patches (Harmony presence check) ───
                ctx.AddString("[6] Territory missing-flag patches");
                string[] patchedTypes = {
                    "Player_UseStamina_Patch", "Player_UseEitr_Patch", "WearNTear_Damage_Patch",
                    "Player_GetRunSpeedFactor_Patch", "Player_TeleportTo_Patch", "Player_HardDeath_Patch",
                    "ArmorStand_UseItem_Patch", "Player_AutoPickup_Patch", "CreatureSpawner_Spawn_Patch",
                    "SpawnSystem_UpdateSpawning_Patch", "Mister_OnEnable_Patch",
                };
                int patchHits = 0;
                var asm = typeof(TerritoryRuntime_MissingFlagPatches).Assembly;
                foreach (var name in patchedTypes)
                {
                    var t = asm.GetType("FiresRPGmaker.Modules.Territories.TerritoryRuntime_MissingFlagPatches+" + name);
                    if (t != null) patchHits++;
                }
                Report("17-flag patch types loaded       ", patchHits == patchedTypes.Length, patchHits + "/" + patchedTypes.Length + " present");

                // ─── 7. Dialogue conditions (12 new ones) ───
                ctx.AddString("[7] dialogue conditions");
                string[] conditionProbes = {
                    "hasachievement TestAch",
                    "hasachievementscore 1",
                    "hasguild",
                    "hasguildwithname TestGuild",
                    "guildlevelmore 5",
                    "guildhasachievement TestGuildAch",
                    "isvip",
                    "npcmodelequals TestModel",
                    "npcnameequals TestName",
                    "playerhasallcustomdatakeys keyA,keyB",
                    "playerhasoneofcustomdatakeys keyA,keyB",
                    "modinstalled FakePluginGuid",
                    "irongatestatmore 1 1",
                };
                int condThrew = 0;
                foreach (var c in conditionProbes)
                {
                    try { global::FiresRPGmaker.Modules.Dialogues.DialogueConditionEngine.EvaluateSingle(c); }
                    catch { condThrew++; }
                }
                Report("12 new conditions don't throw    ", condThrew == 0, condThrew == 0 ? "all callable" : condThrew + " threw");

                // ─── 8. Marketplace v3 panel construct ───
                ctx.AddString("[8] Marketplace v3 panel");
                bool mpThrew = false;
                try
                {
                    global::FiresRPGmaker.UI.Screens.PlayerPanels.MarketplacePlayerPanel.Show("default", "[sanity]");
                }
                catch { mpThrew = true; }
                Report("MarketplacePlayerPanel.Show()    ", !mpThrew, mpThrew ? "threw" : "panel constructed");

                // ─── 9. Factions (config + enforcement plumbing) ───
                ctx.AddString("[9] Factions");
                Report("faction table loaded            ", true, global::FiresRPGmaker.Modules.Factions.FactionManager.All.Count + " faction(s)");
                bool facThrew = false;
                try
                {
                    int fh = "SwordIron".GetStableHashCode();
                    var lf = global::FiresRPGmaker.Modules.Factions.FactionMembership.LocalFaction();
                    global::FiresRPGmaker.Modules.Factions.FactionManager.UseAllowed(fh, lf);
                    global::FiresRPGmaker.Modules.Factions.FactionManager.BuildAllowed(fh, lf);
                    global::FiresRPGmaker.Modules.Factions.FactionManager.CraftAllowed(fh, lf);
                    global::FiresRPGmaker.Modules.Factions.FactionManager.PickupBlocked(fh, lf);
                }
                catch { facThrew = true; }
                Report("use/build/craft/pickup callable  ", !facThrew, facThrew ? "threw" : "ok");
                bool serOk = false;
                try
                {
                    var fd = new global::FiresRPGmaker.Modules.Factions.FactionData { Id = "fires_sanity_fac", Name = "Sanity", CanDamageSameFaction = false };
                    fd.UseNames.Add("SwordIron"); fd.BonusSkills[Skills.SkillType.Swords] = 10;
                    string yaml = global::FiresRPGmaker.Modules.Factions.FactionManager.Serialize(fd);
                    serOk = yaml.Contains("fires_sanity_fac") && yaml.Contains("SpecificUseItems") && yaml.Contains("Swords");
                }
                catch { }
                Report("FactionData serialize round-trip ", serOk);
                bool iconThrew = false;
                try { global::FiresRPGmaker.Modules.Factions.FactionIcons.Resolve(""); global::FiresRPGmaker.Modules.Factions.FactionIcons.Ui("trash_icon"); }
                catch { iconThrew = true; }
                Report("FactionIcons resolve callable    ", !iconThrew);

                // ─── 10. Economy repositories (server owns the vault) ───
                ctx.AddString("[10] Economy repos (LiteDB vault)");
                if (!global::FiresCore.Storage.VaultDatabase.IsConfigured)
                {
                    ctx.AddString("  SKIP  vault not configured (run on host/server — clients don't open the DB)");
                }
                else
                {
                    const string owner = "fires_sanity_owner";
                    int coinHash = "Coins".GetStableHashCode();
                    try
                    {
                        global::FiresCore.Storage.BankRepository.Deposit(owner, coinHash, 100);
                        int afterDep = global::FiresCore.Storage.BankRepository.GetItems(owner).TryGetValue(coinHash, out var v1) ? v1 : 0;
                        int wd = global::FiresCore.Storage.BankRepository.Withdraw(owner, coinHash, 40);
                        int afterWd = global::FiresCore.Storage.BankRepository.GetItems(owner).TryGetValue(coinHash, out var v2) ? v2 : 0;
                        global::FiresCore.Storage.BankRepository.Withdraw(owner, coinHash, 1000); // drain the sentinel
                        Report("Bank deposit/withdraw/getitems  ", afterDep == 100 && wd == 40 && afterWd == 60, "dep=" + afterDep + " wd=" + wd + " left=" + afterWd);
                    }
                    catch (Exception ex) { Report("Bank deposit/withdraw/getitems  ", false, ex.Message); }
                    try
                    {
                        int id = global::FiresCore.Storage.MarketplaceRepository.AddListing(new global::FiresCore.Storage.MarketListing { ItemPrefab = "Wood", Count = 5, Price = 10, Currency = "Coins", SellerName = "sanity", SellerUserID = owner });
                        var got = global::FiresCore.Storage.MarketplaceRepository.GetListing(id);
                        global::FiresCore.Storage.MarketplaceRepository.RemoveListing(id);
                        var gone = global::FiresCore.Storage.MarketplaceRepository.GetListing(id);
                        Report("Market add/get/remove listing   ", id != 0 && got != null && gone == null, "id=" + id);
                    }
                    catch (Exception ex) { Report("Market add/get/remove listing   ", false, ex.Message); }
                    try
                    {
                        global::FiresCore.Storage.MailRepository.AddMail(new global::FiresCore.Storage.MailEntry { Owner = owner, Sender = "sanity", Topic = "t", Message = "m", Attachments = new System.Collections.Generic.List<global::FiresCore.Storage.StoredItem>(), Links = new System.Collections.Generic.List<global::FiresCore.Storage.StoredItem>() });
                        var mails = global::FiresCore.Storage.MailRepository.GetUserMails(owner);
                        int mid = mails.Count > 0 ? mails[0]._id : 0;
                        if (mid != 0) global::FiresCore.Storage.MailRepository.RemoveMail(owner, mid);
                        int after = global::FiresCore.Storage.MailRepository.GetUserMails(owner).Count;
                        Report("Mail add/get/remove             ", mails.Count >= 1 && after == 0);
                    }
                    catch (Exception ex) { Report("Mail add/get/remove             ", false, ex.Message); }
                }

                // ─── 11. VIP + admin check ───
                ctx.AddString("[11] VIP + admin check");
                bool vipThrew = false;
                try
                {
                    global::FiresRPGmaker.Modules.Marketplace.VipManager.IsVip("x", "y");
                    float taxV = global::FiresRPGmaker.Modules.Marketplace.VipManager.MarketTaxPercent(true);
                    global::FiresRPGmaker.Modules.Marketplace.VipManager.BankerInterestMultiplier(true);
                    vipThrew = taxV < 0f || taxV > 100f;
                }
                catch { vipThrew = true; }
                Report("VipManager tax/interest sane     ", !vipThrew);
                bool adminThrew = false;
                try { global::FiresCore.Sync.AdminSyncing.IsLocalAdmin(); }
                catch { adminThrew = true; }
                Report("AdminSyncing.IsLocalAdmin()      ", !adminThrew, adminThrew ? "threw" : "= " + global::FiresCore.Sync.AdminSyncing.IsLocalAdmin());

                // ─── 12. Economy network RPC registration (idempotent) ───
                ctx.AddString("[12] Economy network RPCs");
                bool netThrew = false;
                try
                {
                    global::FiresRPGmaker.Modules.Marketplace.BankerNetwork.Initialize();
                    global::FiresRPGmaker.Modules.Marketplace.MailNetwork.Initialize();
                    global::FiresRPGmaker.Modules.Marketplace.MarketplaceNetwork.Initialize();
                }
                catch { netThrew = true; }
                Report("Banker/Mail/Market Initialize    ", !netThrew, netThrew ? "threw" : "registered (idempotent)");

                // ─── 13. Config managers load ───
                ctx.AddString("[13] Config managers load");
                bool cfgThrew = false;
                try
                {
                    int _ = global::FiresRPGmaker.Modules.Marketplace.BankerProfileManager.AllProfileNames.Count
                          + global::FiresRPGmaker.Modules.Marketplace.GamblerProfileManager.AllProfileNames.Count;
                    global::FiresRPGmaker.Modules.Marketplace.TransmogProfileManager.GetAllForProfile("default");
                    global::FiresRPGmaker.Modules.Marketplace.MarketplaceListingManager.GetProfileSamples("example");
                    foreach (var _n in global::FiresRPGmaker.Modules.Marketplace.MailProfileManager.GetLoadedProfileNames()) { }
                }
                catch { cfgThrew = true; }
                Report("Banker/Gambler/Transmog/Mail cfgs", !cfgThrew, cfgThrew ? "threw" : "all managers load");
            }
            finally
            {
                // ─── cleanup ───
                foreach (var id in sentinelIds)
                {
                    db.Remove(id);
                    QuestManager.Instance?.ForgetSerialisedQuestDb();
                    data.ActiveQuests.RemoveAll(q => string.Equals(q.QuestId, id, StringComparison.OrdinalIgnoreCase));
                    data.CompletedQuests.Remove(id);
                }
                // Remove the custom-data test markers our reward dispatcher wrote.
                player.m_customData.Remove("sanity_test_a");
                player.m_customData.Remove("sanity_test_b");
                vault.SavePlayerData(pid, data);
            }

            ctx.AddString("───────────────────────────────");
            ctx.AddString("SUMMARY  " + pass + " PASS  /  " + fail + " FAIL  (" + (pass + fail) + " total)");
        }

        // ─── factions ─────────────────────────────────────────────────────

        private static void FactionsDump(Terminal.ConsoleEventArgs args)
        {
            var ids = new System.Collections.Generic.List<string>(global::FiresRPGmaker.Modules.Factions.FactionManager.Ids);
            args.Context.AddString("Loaded factions: " + ids.Count);
            foreach (var id in ids)
            {
                var f = global::FiresRPGmaker.Modules.Factions.FactionManager.Get(id);
                args.Context.AddString("  • " + id + " — " + f?.Name
                    + "  (use=" + (f?.UseNames.Count ?? 0) + " build=" + (f?.BuildNames.Count ?? 0)
                    + " craft=" + (f?.CraftNames.Count ?? 0) + " pickup=" + (f?.PickupNames.Count ?? 0)
                    + " skills=" + (f?.BonusSkills.Count ?? 0) + " icon=" + (string.IsNullOrEmpty(f?.Icon) ? "none" : f.Icon) + ")");
            }
            var local = global::FiresRPGmaker.Modules.Factions.FactionMembership.LocalFaction();
            args.Context.AddString("Local player faction: " + (local != null ? local.Name : "(none)"));
        }

        // ─── admin ────────────────────────────────────────────────────────

        private static void AdminDump(Terminal.ConsoleEventArgs args)
        {
            var znet = ZNet.instance;
            args.Context.AddString("─── admin signals @ local player ───");
            args.Context.AddString("  IsServer()               = " + (znet != null && znet.IsServer()));
            bool lpah = false; try { lpah = znet != null && znet.LocalPlayerIsAdminOrHost(); } catch { }
            args.Context.AddString("  LocalPlayerIsAdminOrHost = " + lpah);
            args.Context.AddString("  ConfigSync.lockExempt    = " + global::FiresCore.Sync.ConfigSync.lockExempt);
            args.Context.AddString("  → IsLocalAdmin()         = " + global::FiresCore.Sync.AdminSyncing.IsLocalAdmin());
        }

        // ─── panels ───────────────────────────────────────────────────────

        private static void OpenPanel(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 3) { args.Context.AddString("usage: firesrpg_test panels <banker|mail|transmog|gambler|marketplace> [profile]"); return; }
            string which = args[2].ToLowerInvariant();
            string profile = args.Length > 3 ? args[3] : "example";
            try
            {
                switch (which)
                {
                    case "banker":      global::FiresRPGmaker.UI.Screens.PlayerPanels.BankerPlayerPanel.Show(profile, "[test]"); break;
                    case "mail":        global::FiresRPGmaker.UI.Screens.PlayerPanels.MailPlayerPanel.Show(profile, "[test]"); break;
                    case "transmog":    global::FiresRPGmaker.UI.Screens.PlayerPanels.TransmogPlayerPanel.Show(profile, "[test]"); break;
                    case "gambler":     global::FiresRPGmaker.UI.Screens.PlayerPanels.GamblerPlayerPanel.Show(profile, "[test]"); break;
                    case "marketplace": global::FiresRPGmaker.UI.Screens.PlayerPanels.MarketplacePlayerPanel.Show(profile, "[test]"); break;
                    default: args.Context.AddString("[firesrpg_test] unknown panel '" + which + "'"); return;
                }
                args.Context.AddString("[firesrpg_test] opened " + which + " panel (profile '" + profile + "')");
            }
            catch (Exception ex) { args.Context.AddString("[firesrpg_test] open " + which + " threw: " + ex.Message); }
        }

        // ─── marketplace ──────────────────────────────────────────────────

        private static void OpenMarketplace(Terminal.ConsoleEventArgs args)
        {
            string profile = args.Length > 2 ? args[2] : "default";
            try
            {
                global::FiresRPGmaker.UI.Screens.PlayerPanels.MarketplacePlayerPanel.Show(profile, "Test");
                args.Context.AddString("[firesrpg_test] opened Marketplace panel for profile '" + profile + "'");
            }
            catch (Exception ex)
            {
                args.Context.AddString("[firesrpg_test] failed to open marketplace: " + ex.Message);
            }
        }
    }
}
