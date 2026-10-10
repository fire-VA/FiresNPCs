using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Marketplace;
using FiresRPGmaker.Modules.Quest;

namespace FiresRPGmaker
{
    /// <summary>
    /// In-game self-test for the 2026-09-04 parity sprint (v0.0.428–435): everything CODE-verifiable
    /// runs as one console command, <c>firesrpg_verify</c>, in the ParityTestCommands sanity style
    /// (PASS/FAIL lines + SKIP gates + summary). The eyes-only companions live in
    /// 04_VERIFICATION_CHECKLIST.md §SPR.
    ///
    /// The suite is NON-DESTRUCTIVE: MarketPlace.cfg / DiscordSettings.cfg are byte-backed-up and
    /// restored, temp quest/trader/event cfgs are deleted and reloaded, loopback-granted items are
    /// removed from the inventory, and sentinel repo rows are drained where an API exists. Run on the
    /// HOST of a FRESH TEST WORLD — several sections gate on being the server.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    public static class SprintVerifyCommands
    {
        private static bool _registered;

        [HarmonyPostfix]
        private static void Register()
        {
            if (_registered) return;
            _registered = true;
            new Terminal.ConsoleCommand("firesrpg_verify",
                "run the v428-435 sprint verification suite (host of a fresh test world recommended)",
                args => { try { Run(args.Context); } catch (Exception ex) { args.Context.AddString("[firesrpg_verify] threw: " + ex); } });
        }

        private static void Run(Terminal ctx)
        {
            int pass = 0, fail = 0, skip = 0;
            void Report(string label, bool ok, string detail = "")
            {
                if (ok) { pass++; ctx.AddString("  PASS  " + label + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
                else    { fail++; ctx.AddString("  FAIL  " + label + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
            }
            void Skip(string label, string why) { skip++; ctx.AddString("  SKIP  " + label + " — " + why); }

            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
            var localPlayer = Player.m_localPlayer;

            ctx.AddString("─── firesrpg_verify (sprint v428-435) ───");

            // ─── 1. MarketPlace.cfg knob parsing + live enforcement (patched window, restored after) ───
            ctx.AddString("[1] MarketPlace.cfg knobs (patch → assert → restore)");
            string cfgPath = Path.Combine(BepInEx.Paths.ConfigPath, "Marketplace", "MarketPlace.cfg");
            byte[] cfgBackup = null; bool cfgExisted = false;
            try
            {
                cfgExisted = File.Exists(cfgPath);
                if (cfgExisted) cfgBackup = File.ReadAllBytes(cfgPath);
                Directory.CreateDirectory(Path.GetDirectoryName(cfgPath));
                File.WriteAllText(cfgPath, string.Join("\n", new[]
                {
                    "[Main]",
                    "MarketTaxes = 7",
                    "VIPplayersTaxes = 3",
                    "BankerVIPIncomeMultiplier = 2",
                    "VIPplayersList = SprintVipDummy",
                    "ItemMarketLimit = 2",
                    "BlockedPlayers = SprintBlockedDummy",
                    "MarketSellBlockedPrefabs = SwordIron",
                    "PossibleCurrencies = Coins SilverCoin",
                    "MaxAcceptedQuests = 1",
                    "AllowKillQuestsInParty = false",
                    "GamblerEnableWinNotifications = true",
                    "BlockedChatUsers = SprintMutedDummy",
                    "AllowMultipleQuestsScore = true",
                    "HideOtherQuestRequirementQuests = true",
                    "FactionNameplateOffset = 5,10",
                }));
                VipManager.Reload();
                MarketplaceGlobals.Reload();

                Report("VIP MarketTaxes/VIPplayersTaxes  ", Mathf.Approximately(VipManager.MarketTaxPercent(false), 7f) && Mathf.Approximately(VipManager.MarketTaxPercent(true), 3f),
                    "base=" + VipManager.MarketTaxPercent(false) + " vip=" + VipManager.MarketTaxPercent(true));
                Report("VIP BankerVIPIncomeMultiplier    ", Mathf.Approximately(VipManager.BankerInterestMultiplier(true), 2f), "= " + VipManager.BankerInterestMultiplier(true));
                Report("VIP list member match            ", VipManager.IsVip(null, "SprintVipDummy"));
                Report("ItemMarketLimit                  ", MarketplaceGlobals.ItemMarketLimit == 2, "= " + MarketplaceGlobals.ItemMarketLimit);
                Report("BlockedPlayers match             ", MarketplaceGlobals.IsBlockedPlayer(null, "SprintBlockedDummy"));
                Report("MarketSellBlockedPrefabs match   ", MarketplaceGlobals.IsBlockedPrefab("SwordIron") && !MarketplaceGlobals.IsBlockedPrefab("AxeIron"));
                Report("PossibleCurrencies keep/coerce   ", MarketplaceGlobals.CoerceCurrency("SilverCoin") == "SilverCoin" && MarketplaceGlobals.CoerceCurrency("JunkCoin") == "Coins");
                Report("MaxAcceptedQuests                ", MarketplaceGlobals.MaxAcceptedQuests == 1, "= " + MarketplaceGlobals.MaxAcceptedQuests);
                Report("AllowKillQuestsInParty=false     ", !MarketplaceGlobals.AllowKillQuestsInParty);
                Report("GamblerEnableWinNotifications    ", MarketplaceGlobals.GamblerEnableWinNotifications);
                Report("BlockedChatUsers match           ", MarketplaceGlobals.IsMutedChatUser(null, "SprintMutedDummy"));
                Report("AllowMultipleQuestsScore=true    ", MarketplaceGlobals.AllowMultipleQuestScore);
                Report("HideOtherQuestRequirementQuests  ", MarketplaceGlobals.HideOtherQuestRequirementQuests);
                Report("FactionNameplateOffset parse     ", MarketplaceGlobals.FactionNameplateOffset == new Vector2(5f, 10f), "= " + MarketplaceGlobals.FactionNameplateOffset);

                // ─── 2. Marketplace loopback (fidelity + blocked-prefab reject) — needs host + player ───
                ctx.AddString("[2] Marketplace loopback (host)");
                if (!isServer || localPlayer == null || ZRoutedRpc.instance == null)
                    Skip("fidelity list/cancel + blocked reject", "needs host with a spawned local player");
                else
                {
                    MarketplaceNetwork.Initialize();
                    string lastResult = null;
                    Action<string> hook = s => lastResult = s;
                    MarketplaceNetwork.OnResult += hook;
                    try
                    {
                        long myId = localPlayer.GetPlayerID();
                        // List a quality-3 half-durability axe from thin air (nothing consumed by the test).
                        MarketplaceNetwork.RequestList("SilverCoin", 5, "AxeIron", 1, quality: 3, durabilityPct: 50, crafterName: "SprintVerify", crafterId: 0L, customData: "{}");
                        var mine = MarketplaceListingManager.GetListings(false, "", "").FirstOrDefault(l =>
                            l.SellerId == myId && l.ItemPrefab == "AxeIron" && l.Quality == 3);
                        Report("listing carries quality/dur      ", mine != null && mine.DurabilityPercent == 50 && mine.Currency == "SilverCoin",
                            mine == null ? "listing not found in live cache" : "q=" + mine.Quality + " dur=" + mine.DurabilityPercent + "% cur=" + mine.Currency);

                        if (mine != null)
                        {
                            int q3Before = CountInInventory(localPlayer, "AxeIron", 3);
                            MarketplaceNetwork.RequestCancel(mine.Id);
                            int q3After = CountInInventory(localPlayer, "AxeIron", 3);
                            Report("cancel restores quality-3 item   ", q3After == q3Before + 1 && lastResult != null && lastResult.Contains("Cancelled"),
                                "q3 axes " + q3Before + "→" + q3After + "; result='" + lastResult + "'");
                            RemoveFromInventory(localPlayer, "AxeIron", 3, 1);
                        }

                        lastResult = null;
                        int swBefore = CountInInventory(localPlayer, "SwordIron", 1);
                        MarketplaceNetwork.RequestList("Coins", 5, "SwordIron", 1);
                        int swAfter = CountInInventory(localPlayer, "SwordIron", 1);
                        Report("blocked prefab rejected+returned ", lastResult != null && lastResult.Contains("cannot be sold") && swAfter == swBefore + 1,
                            "result='" + lastResult + "'");
                        RemoveFromInventory(localPlayer, "SwordIron", 1, 1);
                    }
                    finally { MarketplaceNetwork.OnResult -= hook; }
                }
            }
            finally
            {
                try
                {
                    if (cfgExisted) File.WriteAllBytes(cfgPath, cfgBackup);
                    else if (File.Exists(cfgPath)) File.Delete(cfgPath);
                    VipManager.Reload();
                    MarketplaceGlobals.Reload();
                }
                catch (Exception ex) { ctx.AddString("  WARN  MarketPlace.cfg restore failed: " + ex.Message); }
            }

            // ─── 3. RPC registration sweep ───
            ctx.AddString("[3] RPC registration sweep");
            if (ZRoutedRpc.instance == null) Skip("all RPC names", "ZRoutedRpc not up");
            else
            {
                var funcs = ZRoutedRpc.instance.m_functions;
                foreach (var name in new[]
                {
                    "FiresRPGmaker_BankReq", "FiresRPGmaker_BankRes",
                    "FiresRPGmaker_MailReq", "FiresRPGmaker_MailRes",
                    "FiresRPGmaker_MarketReq", "FiresRPGmaker_MarketRes",
                    "FiresRPGmaker_KillEvent", "FiresRPGmaker_QuestProgressEvent",
                    "Fires_Leaderboard_RecordStat",
                    "Fires_FactionChat", "Fires_GamblerWin", "Fires_NpcMapPins",
                    "FiresRPGmaker_Territory_GrantOwner", "FiresRPGmaker_Territory_OwnerSync",
                })
                    Report(("RPC " + name).PadRight(33), funcs.ContainsKey(name.GetStableHashCode()));
            }

            // ─── 4. Fashion extract keeps the 8 kg fields (needs a placed NPC) ───
            ctx.AddString("[4] Fashion extract round-trip");
            var npc = UnityEngine.Object.FindObjectOfType<global::FiresRPGmaker.UI.NpcController>();
            if (npc == null) Skip("extract echoes greet/sound/craft", "no NpcController in the scene — place any NPC first");
            else
            {
                var comp = npc.GetComponent<global::FiresRPGmaker.NpcFashion.NpcFashionComponent>();
                if (comp == null) comp = npc.gameObject.AddComponent<global::FiresRPGmaker.NpcFashion.NpcFashionComponent>();
                string g = comp.GreetText, b = comp.ByeText, s = comp.InteractSound; int c = comp.CraftingAnimation;
                try
                {
                    comp.GreetText = "SprintGreet"; comp.ByeText = "SprintBye"; comp.InteractSound = "SprintClip"; comp.CraftingAnimation = 2;
                    string json = global::FiresRPGmaker.NpcFashion.NpcFashionManager.ExtractFashionJsonFromNpc(npc) ?? "";
                    Report("greet/bye/sound/craft in JSON    ", json.Contains("SprintGreet") && json.Contains("SprintBye") && json.Contains("SprintClip") && json.Contains("\"CraftingAnimation\":\"2\""));
                    Report("back-items + text size in JSON   ", json.Contains("\"LeftItemHidden\"") && json.Contains("\"TextSize\""));
                }
                finally { comp.GreetText = g; comp.ByeText = b; comp.InteractSound = s; comp.CraftingAnimation = c; }
            }

            // ─── 5. Quest cfg pipeline: parser keys + positional transfer + QuestEvents folder ───
            ctx.AddString("[5] Quest cfg pipeline (host disk)");
            if (FiresConfigFiles.UseStore || QuestManager.Instance == null)
                Skip("parser + events merge", "needs host/server (clients parse the synced store)");
            else
            {
                string questsDir = FiresCore.Storage.FiresConfigPaths.GroupedPath("Quests");
                string eventsDir = FiresCore.Storage.FiresConfigPaths.GroupedPath("QuestEvents");
                string qFile = Path.Combine(questsDir, "sprint_verify.cfg");
                string eFile = Path.Combine(eventsDir, "sprint_verify_events.cfg");
                try
                {
                    Directory.CreateDirectory(questsDir);
                    Directory.CreateDirectory(eventsDir);
                    // Quest B uses '=' key/value form on purpose: the positional-format detector treats a
                    // block whose first lines lack '=' as a kg positional block, and ':' alone would trip it.
                    File.WriteAllText(qFile, string.Join("\n", new[]
                    {
                        "[sprint_verify_a]",
                        "Kill",
                        "Sprint Verify A",
                        "positional kg block",
                        "Boar,1,1",
                        "Item:Coins,1",
                        "0,0",
                        "None",
                        "sprint_verify_b,Chain fired!",
                        "[sprint_verify_b]",
                        "Type = Kill",
                        "Name = Sprint Verify B",
                        "Description = keyed block",
                        "Target = Boar,1",
                        "Reward = Item:Coins,1",
                        "OnCancelQuest = PlaySound,sfx_build",
                        "OnQuestTimeout = PlaySound,sfx_build",
                        "OnDeath = PlaySound,sfx_build",
                        "TransferComplition = sprint_verify_a,Chained back!",
                    }));
                    File.WriteAllText(eFile, "[sprint_verify_a]\nOnCancelQuest:PlaySound,sfx_folder\n");
                    QuestManager.Instance.LoadQuestDatabase();

                    var qa = QuestManager.Instance.GetQuestDefinition("sprint_verify_a");
                    var qb = QuestManager.Instance.GetQuestDefinition("sprint_verify_b");
                    Report("positional 8th line → transfer   ", qa != null && string.Equals(qa.TransferComplition, "sprint_verify_b", StringComparison.OrdinalIgnoreCase)
                        && qa.TransferComplitionText == "Chain fired!", qa == null ? "quest a missing" : "'" + qa.TransferComplition + "'");
                    Report("keyed OnCancel/Timeout/Death     ", qb != null
                        && qb.LifecycleEvents.ContainsKey("OnCancelQuest") && qb.LifecycleEvents.ContainsKey("OnQuestTimeout") && qb.LifecycleEvents.ContainsKey("OnDeath"),
                        qb == null ? "quest b missing" : "keys=" + string.Join(",", qb.LifecycleEvents.Keys));
                    Report("keyed TransferComplition         ", qb != null && string.Equals(qb.TransferComplition, "sprint_verify_a", StringComparison.OrdinalIgnoreCase));
                    Report("QuestEvents FOLDER merged        ", qa != null && qa.LifecycleEvents.TryGetValue("OnCancelQuest", out var evs)
                        && evs.Any(e => e.Contains("sfx_folder")), qa == null ? "quest a missing" : "");
                }
                catch (Exception ex) { Report("quest pipeline                   ", false, ex.Message); }
                finally
                {
                    try { if (File.Exists(qFile)) File.Delete(qFile); } catch { }
                    try { if (File.Exists(eFile)) File.Delete(eFile); } catch { }
                    try { QuestManager.Instance.LoadQuestDatabase(); } catch { }
                }
            }

            // ─── 6. Trader NeedToKnow flag round-trip (host disk) ───
            ctx.AddString("[6] Trader NeedToKnow");
            if (FiresConfigFiles.UseStore) Skip("NeedToKnow parse", "needs host/server");
            else
            {
                string tFile = Path.Combine(FiresCore.Storage.FiresConfigPaths.Traders, "sprint_verify_ntk.cfg");
                try
                {
                    Directory.CreateDirectory(FiresCore.Storage.FiresConfigPaths.Traders);
                    File.WriteAllText(tFile, "[sprint_verify_ntk=NeedToKnow,Stock=5]\nWood, 1, Coins, 1\n");
                    TraderProfileManager.Reset(); TraderProfileManager.Initialize();
                    var tp = TraderProfileManager.GetProfile("sprint_verify_ntk");
                    Report("=NeedToKnow flag parsed          ", tp != null && tp.NeedToKnow && tp.DefaultStock == 5,
                        tp == null ? "profile missing" : "ntk=" + tp.NeedToKnow + " stock=" + tp.DefaultStock);
                }
                catch (Exception ex) { Report("=NeedToKnow flag parsed          ", false, ex.Message); }
                finally
                {
                    try { if (File.Exists(tFile)) File.Delete(tFile); } catch { }
                    try { TraderProfileManager.Reset(); TraderProfileManager.Initialize(); } catch { }
                }
            }

            // ─── 7. Leaderboard KilledBy producer plumbing (vault) ───
            ctx.AddString("[7] Leaderboard KilledBy");
            if (!FiresCore.Storage.VaultDatabase.IsConfigured) Skip("RecordKilledBy round-trip", "vault not configured (host/server only)");
            else
            {
                try
                {
                    const string owner = "fires_sprint_kb";
                    FiresCore.Bridge.LeaderboardBridge.RecordKilledBy(owner, "sprint", "Troll", 2);
                    var entry = FiresCore.Storage.LeaderboardRepository.GetOrCreate(owner, "sprint");
                    int troll = entry != null && entry.KilledBy != null && entry.KilledBy.TryGetValue("Troll", out var tv) ? tv : 0;
                    Report("KilledBy dict round-trip         ", troll >= 2, "Troll=" + troll + " (sentinel row 'fires_sprint_kb' remains — no delete API)");
                }
                catch (Exception ex) { Report("KilledBy dict round-trip         ", false, ex.Message); }
                bool enumOk = Enum.IsDefined(typeof(LeaderboardStat), "KilledBy") && (int)LeaderboardStat.KilledBy == 12;
                Report("LeaderboardStat.KilledBy = 12    ", enumOk);
            }

            // ─── 8. Mail broadcast (host + admin) ───
            ctx.AddString("[8] Mail broadcast");
            if (!isServer || localPlayer == null || !FiresCore.Sync.AdminSyncing.IsLocalAdmin() || !FiresCore.Storage.VaultDatabase.IsConfigured)
                Skip("send to '*'", "needs host + admin + configured vault");
            else
            {
                MailNetwork.Initialize();
                string mailResult = null;
                Action<string> hook = s => mailResult = s;
                MailNetwork.OnResult += hook;
                try
                {
                    MailNetwork.RequestSend("*", "SprintVerifyBroadcast", "automated sprint verify — safe to ignore");
                    Report("broadcast reply                  ", mailResult != null && mailResult.Contains("Broadcast to"), "'" + mailResult + "'");
                }
                finally
                {
                    MailNetwork.OnResult -= hook;
                    try
                    {
                        foreach (var u in FiresCore.Storage.MailRepository.GetAllUsers())
                        {
                            if (u == null || string.IsNullOrEmpty(u.UserID)) continue;
                            foreach (var m in FiresCore.Storage.MailRepository.GetUserMails(u.UserID).Where(m => m.Topic == "SprintVerifyBroadcast").ToList())
                                FiresCore.Storage.MailRepository.RemoveMail(u.UserID, m._id);
                        }
                    }
                    catch { }
                }
            }

            // ─── 9. DiscordWebhooks cfg parse (patched + restored) ───
            ctx.AddString("[9] DiscordSettings.cfg parse");
            string dsPath = Path.Combine(BepInEx.Paths.ConfigPath, "Marketplace", "DiscordStuff", "DiscordSettings.cfg");
            byte[] dsBackup = null; bool dsExisted = false;
            try
            {
                dsExisted = File.Exists(dsPath);
                if (dsExisted) dsBackup = File.ReadAllBytes(dsPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dsPath));
                File.WriteAllText(dsPath, "[Webhook Links]\nMarketplace Webhook Link = https://sprint.invalid/hook\n[Webhook Messages]\nMarketplace Webhook Message = SPRINT {0}\n[Webhook Titles]\nMarketplace Webhook Title = SprintTitle\n");
                DiscordWebhooks.Reload();
                var hooksField = typeof(DiscordWebhooks).GetField("_hooks", BindingFlags.NonPublic | BindingFlags.Static);
                var dict = hooksField?.GetValue(null) as System.Collections.IDictionary;
                object mp = dict != null && dict.Contains("Marketplace") ? dict["Marketplace"] : null;
                string link = mp?.GetType().GetField("Link")?.GetValue(mp) as string;
                string msg  = mp?.GetType().GetField("Message")?.GetValue(mp) as string;
                Report("link + template read             ", link == "https://sprint.invalid/hook" && msg == "SPRINT {0}", "link='" + link + "' msg='" + msg + "'");
            }
            catch (Exception ex) { Report("link + template read             ", false, ex.Message); }
            finally
            {
                try
                {
                    if (dsExisted) File.WriteAllBytes(dsPath, dsBackup);
                    else if (File.Exists(dsPath)) File.Delete(dsPath);
                    DiscordWebhooks.Reload();
                }
                catch { }
            }

            // ─── 10. ServerInfo URL cache naming (reflection micro-check) ───
            ctx.AddString("[10] ServerInfo helpers");
            try
            {
                var m = typeof(global::FiresRPGmaker.UI.Screens.InfoNpcPlayerPanel).GetMethod("UrlCacheName", BindingFlags.NonPublic | BindingFlags.Static);
                string n = m?.Invoke(null, new object[] { "https://example.com/imgs/logo.png?v=12" }) as string;
                Report("UrlCacheName strips query/path   ", n == "logo.png", "= '" + n + "'");
            }
            catch (Exception ex) { Report("UrlCacheName strips query/path   ", false, ex.Message); }

            ctx.AddString($"─── done: {pass} PASS · {fail} FAIL · {skip} SKIP ───");
            if (fail > 0) ctx.AddString("Report FAIL lines with the 04-checklist feedback format (ID: VERDICT — Did/Saw/Wanted).");
            ctx.AddString("Eyes-only checks: 04_VERIFICATION_CHECKLIST.md §SPR (map pins, /f, InRange, territory buy, gambler shout, …).");
        }

        private static int CountInInventory(Player p, string prefab, int quality)
        {
            int total = 0;
            foreach (var it in p.GetInventory().GetAllItems())
                if (it?.m_dropPrefab != null && it.m_dropPrefab.name == prefab && it.m_quality == quality) total += it.m_stack;
            return total;
        }

        private static void RemoveFromInventory(Player p, string prefab, int quality, int amount)
        {
            int remaining = amount;
            foreach (var it in p.GetInventory().GetAllItems().ToList())
            {
                if (remaining <= 0) break;
                if (it?.m_dropPrefab == null || it.m_dropPrefab.name != prefab || it.m_quality != quality) continue;
                int take = Mathf.Min(it.m_stack, remaining);
                p.GetInventory().RemoveItem(it, take);
                remaining -= take;
            }
        }
    }
}
