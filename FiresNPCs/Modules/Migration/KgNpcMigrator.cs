using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using FiresCore.Npc;
using FiresRPGmaker.NpcFashion;

namespace FiresRPGmaker.Modules.Migration
{
    /// <summary>
    /// kg-Marketplace → FiresRPGmaker NPC world migration (design: Tools/MarketplaceParityPlan/06).
    ///
    /// kg NPCs are persistent ZDOs on the prefab <c>MarketPlaceNPC</c> with well-known string keys
    /// (KGmarketNPC / KGnpcProfile / 27 fashion keys / "KGmarket PatrolData" / pins). The migrator reads
    /// them RAW from ZDOMan (works for unloaded zones; kg itself doesn't need to be running), then swaps
    /// each for a seeded <c>StaticNpc</c> ZDO carrying the full mapped identity + appearance. Anchors mint
    /// themselves via StaticNpcInitializer's organic-migration path when a zone first loads server-side.
    ///
    /// Commands (admin): <c>fires_migrate_kg scan</c> — read-only census + JSON backup + report file.
    ///                   <c>fires_migrate_kg confirm</c> — backup, seed replacements, destroy kg ZDOs.
    /// Runs on the SERVER (full ZDO view). From a client console the command bridges via routed RPC.
    /// </summary>
    public static class KgNpcMigrator
    {
        private const string KgPrefabName = "MarketPlaceNPC";
        private const string OurPrefabName = "StaticNpc";
        private const string RpcRun = "Fires_MigrateKg_Run";
        private const string RpcResult = "Fires_MigrateKg_Result";
        private const string RpcTemplate = "Fires_MigrateKg_Template";

        // SavedNpcData carries Unity Vector3s (Position) — plain Json.NET walks Vector3.normalized
        // into a self-referencing loop. Same converter setup SavedNpcManager uses for template files.
        private static readonly JsonSerializerSettings TemplateJsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Converters = new List<JsonConverter> { new global::FiresRPGmaker.Managers.Vector3Converter() },
        };

        // kg Market_NPC.NPCType (kg.MarketplaceREF Dialogues enum order) → our FiresCore.Npc.NpcType.
        internal static readonly Dictionary<int, NpcType> TypeMap = new Dictionary<int, NpcType>
        {
            { 0, NpcType.None },        // None
            { 1, NpcType.Trader },      // Trader
            { 2, NpcType.InfoNpc },     // Info
            { 3, NpcType.Teleporter },  // Teleporter
            { 4, NpcType.Feedback },    // Feedback (first-class since 0.0.431)
            { 5, NpcType.Banker },      // Banker
            { 6, NpcType.Gambler },     // Gambler
            { 7, NpcType.QuestNpc },    // Quests
            { 8, NpcType.Buffer },      // Buffer
            { 9, NpcType.Transmog },    // Transmog
            { 10, NpcType.Marketplace },// Marketplace
            { 11, NpcType.Mail },       // Mail
        };

        internal static readonly Dictionary<NpcType, string> ProfileKeyByType = new Dictionary<NpcType, string>
        {
            { NpcType.Trader, "npc_trader_profile" },
            { NpcType.InfoNpc, "npc_info_profile" },
            { NpcType.Teleporter, "npc_teleporter_profile" },
            { NpcType.Banker, "npc_banker_profile" },
            { NpcType.Gambler, "npc_gambler_profile" },
            { NpcType.QuestNpc, "npc_quest_profile" },
            { NpcType.Buffer, "npc_buffer_profile" },
            { NpcType.Transmog, "npc_transmog_profile" },
            { NpcType.Marketplace, "npc_marketplace_profile" },
            { NpcType.Mail, "npc_mail_profile" },
        };

        // Every kg string key we carry (read into the record + copied verbatim onto the new ZDO so
        // NOTHING is lost — greet/periodic are live features now; pins/crafting/text* stay dormant).
        private static readonly string[] KgStringKeys =
        {
            "KGnpcProfile", "KGnpcDialogue", "KGnpcNameOverride", "KGnpcModelOverride", "KGmarketPinIcon",
            "KGleftItem", "KGrightItem", "KGhelmetItem", "KGchestItem", "KGlegsItem", "KGcapeItem",
            "KGhairItem", "KGhairItemColor", "KGLeftItemBack", "KGRightItemBack",
            "KGinteractAnimation", "KGgreetingAnimation", "KGbyeAnimation", "KGgreetingText", "KGbyeText",
            "KGskinColor", "KGbeardItem", "KGbeardColor", "KGinteractSound",
            "KGperiodicAnimation", "KGperiodicSound", "KGmarket PatrolData",
        };
        private static readonly string[] KgFloatKeys = { "KGnpcScale", "KGtextSize", "KGtextHeight", "KGperiodicAnimationTime", "KGperiodicSoundTime" };
        private static readonly string[] KgIntKeys = { "KGmarketNPC", "KGcraftingAnimation" };
        private static readonly string[] KgBoolKeys = { "KGmarketPinned" };

        // kg parity for the NPC profile field: strip a trailing "=Flag" (e.g. "botanistbuy_mp=True"),
        // lowercase (kg's ChangeProfile stores lowercase), and fall back to "default" when empty —
        // exactly what kg's ZDO.GET_NPC_Profile(...) returns at runtime.
        internal static string NormalizeKgProfile(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "default";
            int eq = raw.IndexOf('=');
            var name = (eq > 0 ? raw.Substring(0, eq) : raw).Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(name) ? "default" : name;
        }

        // Notes surfaced by the last confirm pass (missing model prefabs etc.) — appended to the result.
        private static readonly List<string> _swapNotes = new List<string>();

        internal sealed class Record
        {
            public string ZdoId;
            // Unity structs self-reference under Json.NET (Vector3.normalized → Vector3 → ...), so the
            // live values stay [JsonIgnore] and the backup serializes plain float arrays instead.
            [JsonIgnore] public Vector3 Pos;
            [JsonIgnore] public Quaternion Rot;
            public float[] PosXYZ
            {
                get { return new[] { Pos.x, Pos.y, Pos.z }; }
                set { if (value != null && value.Length == 3) Pos = new Vector3(value[0], value[1], value[2]); }
            }
            public float[] RotXYZW
            {
                get { return new[] { Rot.x, Rot.y, Rot.z, Rot.w }; }
                set { if (value != null && value.Length == 4) Rot = new Quaternion(value[0], value[1], value[2], value[3]); }
            }
            public Dictionary<string, string> S = new Dictionary<string, string>();
            public Dictionary<string, float> F = new Dictionary<string, float>();
            public Dictionary<string, int> I = new Dictionary<string, int>();
            public Dictionary<string, bool> B = new Dictionary<string, bool>();
            [JsonIgnore] public ZDO Zdo;

            public string Str(string key) { S.TryGetValue(key, out var v); return v ?? ""; }
            public float Flt(string key, float def) { return F.TryGetValue(key, out var v) ? v : def; }
            public int Int(string key) { I.TryGetValue(key, out var v); return v; }
            public bool Bool(string key) { B.TryGetValue(key, out var v); return v; }
        }

        #region Command + RPC plumbing

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class RegisterCommand
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("fires_migrate_kg",
                    "all|scan|confirm|configs|economy [confirm]|templates|rollback - migrate kg-Marketplace to Fires (all = the ENTIRE migration end to end: configs -> economy -> NPC swap -> templates)",
                    args =>
                    {
                        string mode = args.Length > 1
                            ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim().ToLowerInvariant()
                            : "scan";
                        if (mode != "scan" && mode != "confirm" && mode != "rollback" && mode != "configs"
                            && mode != "economy" && mode != "economy confirm" && mode != "templates" && mode != "all"
                            && mode != "census" && mode != "anchors" && mode != "dress")
                        {
                            args.Context.AddString("[fires_migrate_kg] usage: fires_migrate_kg all|scan|confirm|configs|economy [confirm]|templates|census|anchors|dress|rollback");
                            return;
                        }
                        if (ZNet.instance == null) { args.Context.AddString("[fires_migrate_kg] no world loaded"); return; }

                        if (ZNet.instance.IsServer())
                        {
                            // Runs directly on host, OR when server_devcommands RELAYS a client's command
                            // to the server (which bypasses our RpcResult reply — so the client console
                            // shows nothing). Debug.Log every line: it lands in the server log AND rides
                            // the Core client-log relay back to the admin, so there's always feedback +
                            // a durable record. args.Context may be null on a relayed command.
                            foreach (var line in Run(mode))
                            {
                                args.Context?.AddString(line);
                                Debug.Log("[fires_migrate_kg] " + line);
                            }
                        }
                        else
                        {
                            // Bridge to the server (full ZDO view lives there); result prints when the reply lands.
                            args.Context.AddString("[fires_migrate_kg] sent '" + mode + "' to the server...");
                            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcRun, mode);
                        }
                    }, isCheat: false, isNetwork: false, onlyServer: false);
            }
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        private static class RegisterRpcs
        {
            private static void Postfix()
            {
                if (ZRoutedRpc.instance == null) return;
                ZRoutedRpc.instance.Register<string>(RpcRun, OnRunRequest);
                ZRoutedRpc.instance.Register<string>(RpcResult, (sender, text) =>
                {
                    if (Console.instance == null) return;
                    foreach (var line in text.Split('\n')) Console.instance.AddString(line);
                });
                ZRoutedRpc.instance.Register<ZPackage>(RpcTemplate, OnTemplateData);
            }
        }

        private static void OnRunRequest(long sender, string mode)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!IsSenderAdmin(sender))
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcResult, "[fires_migrate_kg] DENIED - admin only");
                Debug.LogWarning("[KgNpcMigrator] non-admin sender " + sender + " tried '" + mode + "'");
                return;
            }
            // Templates are CLIENT-LOCAL (they bake into anchors at placement) — the requesting admin
            // receives them on their machine, so these modes need the sender.
            var lines = mode == "templates" ? ImportTemplates(sender)
                      : mode == "all" ? RunAll(sender)
                      : Run(mode);
            // Reply in one routed message, capped well under the ServerCharacters 20KB RPC ceiling —
            // the full detail always lands in the server-side report file. Also Debug.Log every line
            // (durable server record + Core client-log relay) so feedback survives any console routing.
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                Debug.Log("[fires_migrate_kg] " + line);
                if (sb.Length + line.Length > 14000) { sb.AppendLine("... (truncated - full report on the server)"); break; }
                sb.AppendLine(line);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcResult, sb.ToString().TrimEnd());
        }

        // Fail-closed admin gate: the sending peer's host id must be on the server's admin list.
        internal static bool IsSenderAdmin(long sender)
        {
            try
            {
                if (sender == ZRoutedRpc.instance.GetServerPeerID()) return true; // the server itself
                var peer = ZNet.instance.GetPeer(sender);
                if (peer == null) return false;
                string host = peer.m_socket?.GetHostName();
                if (string.IsNullOrEmpty(host)) return false;
                return ZNet.instance.ListContainsId(ZNet.instance.m_adminList, host);
            }
            catch { return false; }
        }

        #endregion

        #region Scan + swap

        private static List<string> Run(string mode)
        {
            var outLines = new List<string>();
            if (mode == "rollback") return Rollback();
            if (mode == "census") return Census();
            if (mode == "anchors") return MintMissingAnchors();
            if (mode == "dress") return DressMigratedBodies();
            if (mode == "configs") return ImportConfigs();
            if (mode == "economy" || mode == "economy confirm") return ImportEconomy(mode.EndsWith("confirm"));
            if (mode == "templates") return ImportTemplates(0L); // server-console path: save locally
            if (mode == "all") return RunAll(0L);                // server-console path: templates save locally
            try
            {
                var records = Scan();
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string dir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Root, "Migration");
                Directory.CreateDirectory(dir);
                string world = ZNet.instance?.GetWorldName() ?? "world";

                // Backup EVERY raw value before anything else — scan and confirm both write it.
                string backupPath = Path.Combine(dir, "kg_npcs_" + world + "_" + stamp + ".json");
                File.WriteAllText(backupPath, JsonConvert.SerializeObject(records, Formatting.Indented));

                var report = BuildReport(records);
                string reportPath = Path.Combine(dir, "kg_scan_" + world + "_" + stamp + ".txt");
                File.WriteAllLines(reportPath, report);

                outLines.Add("[fires_migrate_kg] " + mode.ToUpperInvariant() + ": " + records.Count + " kg NPC(s) found in '" + world + "'");
                outLines.Add("  backup: " + backupPath);
                outLines.Add("  report: " + reportPath);
                int shown = 0;
                foreach (var line in report)
                {
                    outLines.Add("  " + line);
                    if (++shown >= 40 && report.Count > 44) { outLines.Add("  ... (" + (report.Count - shown) + " more lines in the report file)"); break; }
                }

                if (mode == "confirm")
                {
                    _swapNotes.Clear();
                    _swapDump.Clear();
                    _retiredLive = 0;
                    _retiredDormant = 0;
                    _bodiesValid = 0;
                    _bodiesBroken = 0;
                    _anchorsMinted = 0;
                    int ok = 0, failed = 0;
                    foreach (var rec in records)
                    {
                        try { SwapOne(rec); ok++; }
                        catch (Exception ex)
                        {
                            failed++;
                            Debug.LogError("[KgNpcMigrator] swap failed for " + rec.ZdoId + ": " + ex);
                        }
                    }
                    string dumpPath = Path.Combine(dir, "swap_dump_" + world + "_" + stamp + ".json");
                    try { File.WriteAllText(dumpPath, JsonConvert.SerializeObject(_swapDump, Formatting.Indented)); }
                    catch (Exception ex) { Debug.LogError("[KgNpcMigrator] dump write failed: " + ex.Message); dumpPath = "(write failed: " + ex.Message + ")"; }
                    // Persist the destructive swap NOW so a crash before the next autosave can't leave
                    // a half-state (originals destroyed in memory but not on disk). Server-only.
                    try { if (ZNet.instance != null && ZNet.instance.IsServer()) ZNet.instance.Save(false); }
                    catch (Exception ex) { Debug.LogWarning("[KgNpcMigrator] post-swap save failed: " + ex.Message); }
                    outLines.Add("[fires_migrate_kg] CONFIRM complete: " + ok + " swapped, " + failed + " failed, " +
                                 _anchorsMinted + " anchor(s) minted (world saved). Bodies materialize on clients as zones load; " +
                                 "a dedicated server never instantiates them itself. Rollback source: " + backupPath);
                    // Accurate destroy tally (from the swap itself). A live re-scan is misleading here —
                    // ZDOMan.DestroyZDO queues removals that don't drain from the sector index until the
                    // next tick, so an immediate re-count reads the pre-destroy total and false-alarms.
                    outLines.Add("  retired " + (_retiredLive + _retiredDormant) + " kg NPC(s): " + _retiredLive + " in loaded zones, " + _retiredDormant + " in unloaded zones.");
                    // Read-back verification (ground truth, not a snapshot): every body re-read from the
                    // DB with a valid prefab = it WILL spawn. Any broken here means empty zones later.
                    outLines.Add("  bodies verified in DB: " + _bodiesValid + "/" + ok +
                                 (_bodiesBroken == 0 ? " (all placeable)" : " — " + _bodiesBroken + " BROKEN, will not spawn (see notes)"));
                    outLines.Add("  dump: " + dumpPath);
                    // Immediate post-swap + post-save BASELINE census. Compare against `fires_migrate_kg census`
                    // run AFTER a relog: if this shows ~69 initialized StaticNpc / 0 kg here, but the post-relog
                    // census shows uninitialized/scramble candidates or kg NPCs back, the save/load cycle is
                    // dropping the swap — proving the persistence bug lives in save/load, not in the swap itself.
                    outLines.Add("  --- post-swap baseline census (compare after relog with 'fires_migrate_kg census') ---");
                    try { outLines.AddRange(Census()); }
                    catch (Exception ex) { outLines.Add("  census failed: " + ex.Message); }
                    int noteShown = 0;
                    foreach (var note in _swapNotes)
                    {
                        outLines.Add("  ! " + note);
                        if (++noteShown >= 12 && _swapNotes.Count > 14) { outLines.Add("  ! ... (" + (_swapNotes.Count - noteShown) + " more notes in the server log)"); break; }
                    }
                }
            }
            catch (Exception ex)
            {
                outLines.Add("[fires_migrate_kg] ERROR: " + ex.Message);
                Debug.LogError("[KgNpcMigrator] " + ex);
            }
            return outLines;
        }

        /// <summary>
        /// Import kg's SavedData/DB.db — the PLAYER ECONOMY a live kg server accumulated (bank items,
        /// mail inboxes, market listings, user registry, leaderboard stats) — into our live vault.
        /// Dry-run by default (source counts only); "economy confirm" performs the merge. Owner keys
        /// are platform ids on both sides, so players keep everything across the swap with no relinking.
        /// </summary>
        private static List<string> ImportEconomy(bool confirm)
        {
            var outLines = new List<string>();
            try
            {
                string src = FiresCore.Compat.MarketplacePaths.DBFile;
                outLines.Add("[fires_migrate_kg] ECONOMY " + (confirm ? "IMPORT" : "DRY-RUN") + ": " + src);
                foreach (var line in FiresCore.Storage.KgVaultImport.Run(src, confirm))
                    outLines.Add("  " + line);
            }
            catch (Exception ex)
            {
                outLines.Add("[fires_migrate_kg] ECONOMY ERROR: " + ex.Message);
                Debug.LogError("[KgNpcMigrator] " + ex);
            }
            return outLines;
        }

        /// <summary>
        /// THE master migration: runs the entire kg → Fires path end to end, each stage completing
        /// before the next starts — configs (profile trees) → economy (DB.db) → NPC swap (scan +
        /// backup + confirm) → hammer templates (streamed to the requesting admin). Every stage is a
        /// synchronous call, so ordering is guaranteed; between configs and the swap the server-side
        /// trader/dialogue caches are explicitly reloaded so the swap's PROFILE MISSING / DIALOGUE
        /// MISSING checks run against the freshly imported content, not a stale pre-import cache.
        /// Each stage keeps its own safety net (economy import marker, NPC backup json + rollback).
        /// </summary>
        internal static List<string> RunAll(long requester)
        {
            var outLines = new List<string>();
            outLines.Add("[fires_migrate_kg] FULL MIGRATION: configs -> economy -> NPC swap -> templates");

            outLines.Add("== stage 1/4: configs ==");
            outLines.AddRange(ImportConfigs());

            // The swap's reference checks read these caches; the marketplace families were reloaded
            // by ImportConfigs, but traders + dialogues reload via the file watcher (debounced) —
            // force them now so stage 3 sees the imported content deterministically.
            try
            {
                global::FiresRPGmaker.Managers.TraderProfileManager.Reset();
                global::FiresRPGmaker.Managers.TraderProfileManager.Initialize();
                global::FiresRPGmaker.Modules.Dialogues.DialogueCodex.Reload();
                // Quests too (0.0.493, rpgmaker_migrate_test L1): imported quest profiles only reached the server's quest
                // caches on the config watcher's later pass, so quest NPCs offered nothing straight after a migrate-all.
                global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.LoadQuestDatabase();
                global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.LoadQuestProfiles();
                global::FiresRPGmaker.UI.QuestProfileManager.Reload();
            }
            catch (Exception ex) { outLines.Add("  ! cache reload: " + ex.Message); }

            outLines.Add("== stage 2/4: economy ==");
            outLines.AddRange(ImportEconomy(true));

            outLines.Add("== stage 3/4: NPC swap ==");
            outLines.AddRange(Run("confirm"));

            outLines.Add("== stage 4/4: hammer templates ==");
            outLines.AddRange(ImportTemplates(requester));

            outLines.Add("[fires_migrate_kg] FULL MIGRATION COMPLETE - verify in-world, then remove the kg plugin when satisfied. 'rollback' undoes the NPC swap; configs/economy/templates are additive.");
            return outLines;
        }

        /// <summary>
        /// Import kg's Marketplace_SavedNPCs *.yml hammer TEMPLATES. Templates are CLIENT-LOCAL by
        /// design (the appearance bakes into the anchor at placement — the template itself never
        /// syncs), so the server parses the ymls and streams each converted template to the
        /// REQUESTING ADMIN's machine over the chunked template RPC; run from the server console it
        /// saves them locally instead. kg wrapper: isPinned + main{Type/Profile/Prefab/Dialogue/
        /// NameOverride/IMAGE base64} + fashion{27 fields that mirror NPCFashionData by name}. The
        /// kg portrait IMAGE lands directly in SavedNpcData.IconPng — no re-render needed, so even
        /// templates whose model mod isn't installed here keep their real portrait.
        /// </summary>
        private static List<string> ImportTemplates(long requester)
        {
            var outLines = new List<string>();
            try
            {
                string src = Path.Combine(BepInEx.Paths.ConfigPath, "Marketplace_SavedNPCs");
                outLines.Add("[fires_migrate_kg] TEMPLATES: " + src + (requester != 0L ? " -> streaming to requesting admin" : " -> saving on this machine"));
                if (!Directory.Exists(src))
                {
                    outLines.Add("  source folder not found - nothing to import");
                    return outLines;
                }

                int ok = 0, failed = 0;
                foreach (var file in Directory.GetFiles(src, "*.yml", SearchOption.AllDirectories))
                {
                    // kg config zips often contain a nested duplicate of the whole tree — skip it.
                    string rel = file.Substring(src.Length).TrimStart('\\', '/');
                    if (rel.StartsWith("Marketplace_SavedNPCs", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        var data = ParseKgTemplate(file, out string templateName, outLines);
                        if (data == null) { failed++; continue; }

                        if (requester != 0L)
                        {
                            string json = JsonConvert.SerializeObject(data, TemplateJsonSettings);
                            global::FiresRPGmaker.Managers.ServerConfigFileWatcher.SendFileToClient(
                                requester, RpcTemplate, "kgtpl", templateName + ".json", json);
                        }
                        else
                        {
                            global::FiresRPGmaker.Managers.SavedNpcManager.SaveTemplate(templateName, data);
                        }
                        ok++;
                        outLines.Add("  + " + templateName + (string.IsNullOrEmpty(data.ModelOverride) ? "" : "  [" + data.ModelOverride + "]"));
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        outLines.Add("  ! " + Path.GetFileName(file) + " -> " + ex.Message);
                    }
                }
                outLines.Add("[fires_migrate_kg] TEMPLATES complete: " + ok + " imported, " + failed + " failed." +
                             (requester != 0L ? " They install on your machine as they arrive (hammer > NPC templates tab)." : ""));
            }
            catch (Exception ex)
            {
                outLines.Add("[fires_migrate_kg] TEMPLATES ERROR: " + ex.Message);
                Debug.LogError("[KgNpcMigrator] " + ex);
            }
            return outLines;
        }

        /// <summary>Parses one kg saved-NPC yml (flat two-level "key: value" wrapper) into our template
        /// record, mapping exactly like the world swap: kg Type name → our NpcType, Profile → the
        /// matching per-type profile (lowercased, as kg references them), Prefab → ModelOverride
        /// verbatim (ApplyModelOverride handles Player/Player_Female/@humanoid natively), fashion →
        /// NPCFashionData 1:1 (random lists kept — each placement re-rolls, kg behavior) with the
        /// head-IK LookAtPlayer only when there is no crafting pose.</summary>
        private static SavedNpcData ParseKgTemplate(string path, out string templateName, List<string> notes)
        {
            var main = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fashionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> section = null;

            foreach (var raw in File.ReadAllLines(path))
            {
                if (raw.Length == 0) continue;
                if (!char.IsWhiteSpace(raw[0]))
                {
                    string top = raw.Trim().TrimEnd(':');
                    if (top.Equals("main", StringComparison.OrdinalIgnoreCase)) section = main;
                    else if (top.Equals("fashion", StringComparison.OrdinalIgnoreCase)) section = fashionMap;
                    else section = null; // isPinned etc.
                    continue;
                }
                if (section == null) continue;
                int i = raw.IndexOf(':');
                if (i <= 0) continue;
                string key = raw.Substring(0, i).Trim();
                string val = raw.Substring(i + 1).Trim();
                if (val.Length >= 2 && val[0] == '\'' && val[val.Length - 1] == '\'') val = val.Substring(1, val.Length - 2);
                section[key] = val;
            }

            string G(Dictionary<string, string> d, string k) { d.TryGetValue(k, out var v); return v ?? ""; }

            string profile = G(main, "Profile");
            string dialogue = G(main, "Dialogue");
            string model = G(main, "Prefab");
            templateName = global::FiresRPGmaker.Managers.SavedNpcManager.MakeTemplateName(
                !string.IsNullOrEmpty(profile) ? profile : Path.GetFileNameWithoutExtension(path));

            // kg stores the enum NAME in templates (world ZDOs store the int).
            int kgType = 0;
            string typeName = G(main, "Type");
            for (int t = 0; t <= 11; t++) if (KgTypeName(t).Equals(typeName, StringComparison.OrdinalIgnoreCase)) { kgType = t; break; }
            NpcType ourType = TypeMap.TryGetValue(kgType, out var mapped) ? mapped : NpcType.None;
            if (ourType == NpcType.None && !string.IsNullOrEmpty(dialogue)) ourType = NpcType.DialogueNpc;

            string lowProfile = profile.ToLowerInvariant();
            var data = new SavedNpcData
            {
                NpcProfileName = profile,
                DisplayNameOverride = G(main, "NameOverride"),
                DialogueProfile = dialogue,
                NpcType = ourType,
                ModelOverride = model,
                IconPng = G(main, "IMAGE"),
                TraderProfile = ourType == NpcType.Trader ? lowProfile : "",
                InfoProfile = ourType == NpcType.InfoNpc ? lowProfile : "",
                QuestProfile = ourType == NpcType.QuestNpc ? lowProfile : "",
                EquipHelmet = PickOne(G(fashionMap, "HelmetItem")),
                EquipChest = PickOne(G(fashionMap, "ChestItem")),
                EquipLegs = PickOne(G(fashionMap, "LegsItem")),
                EquipShoulder = PickOne(G(fashionMap, "CapeItem")),
                EquipLeftHand = PickOne(G(fashionMap, "LeftItem")),
                EquipRightHand = PickOne(G(fashionMap, "RightItem")),
                EquipLeftBack = PickOne(G(fashionMap, "LeftItemHidden")),
                EquipRightBack = PickOne(G(fashionMap, "RightItemHidden")),
            };

            int craftAnim = 0;
            int.TryParse(G(fashionMap, "CraftingAnimation"), out craftAnim);
            var fashion = new NPCFashionData
            {
                LeftItem = G(fashionMap, "LeftItem"),
                RightItem = G(fashionMap, "RightItem"),
                HelmetItem = G(fashionMap, "HelmetItem"),
                ChestItem = G(fashionMap, "ChestItem"),
                LegsItem = G(fashionMap, "LegsItem"),
                CapeItem = G(fashionMap, "CapeItem"),
                LeftItemHidden = G(fashionMap, "LeftItemHidden"),
                RightItemHidden = G(fashionMap, "RightItemHidden"),
                HairItem = G(fashionMap, "HairItem"),
                HairColor = G(fashionMap, "HairColor"),
                BeardItem = G(fashionMap, "BeardItem"),
                BeardColor = G(fashionMap, "BeardColor"),
                SkinColor = G(fashionMap, "SkinColor"),
                InteractAnimation = G(fashionMap, "InteractAnimation"),
                GreetAnimation = G(fashionMap, "GreetAnimation"),
                ByeAnimation = G(fashionMap, "ByeAnimation"),
                GreetText = G(fashionMap, "GreetText"),
                ByeText = G(fashionMap, "ByeText"),
                InteractAudioClip = G(fashionMap, "InteractAudioClip"),
                CraftingAnimation = craftAnim.ToString(CultureInfo.InvariantCulture),
                TextSize = string.IsNullOrEmpty(G(fashionMap, "TextSize")) ? "3" : G(fashionMap, "TextSize"),
                TextHeight = string.IsNullOrEmpty(G(fashionMap, "TextHeight")) ? "0" : G(fashionMap, "TextHeight"),
                PeriodicAnimation = G(fashionMap, "PeriodicAnimation"),
                PeriodicAnimationTime = string.IsNullOrEmpty(G(fashionMap, "PeriodicAnimationTime")) ? "0" : G(fashionMap, "PeriodicAnimationTime"),
                PeriodicSound = G(fashionMap, "PeriodicSound"),
                PeriodicSoundTime = string.IsNullOrEmpty(G(fashionMap, "PeriodicSoundTime")) ? "0" : G(fashionMap, "PeriodicSoundTime"),
                ModelScale = string.IsNullOrEmpty(G(fashionMap, "ModelScale")) ? "1" : G(fashionMap, "ModelScale"),
                LookAtPlayer = craftAnim == 0 ? "true" : "false",
            };
            data.FashionJson = JsonConvert.SerializeObject(fashion);

            string modelBase = System.Text.RegularExpressions.Regex.Replace(model ?? "", "@humanoid", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrEmpty(modelBase)
                && !modelBase.Equals("Player", StringComparison.OrdinalIgnoreCase)
                && !modelBase.Equals("Player_Female", StringComparison.OrdinalIgnoreCase)
                && ZNetScene.instance != null && ZNetScene.instance.GetPrefab(modelBase) == null)
                notes.Add("  ! MODEL MISSING '" + model + "' for template '" + templateName + "' - the portrait still shows; the body applies once the providing mod is installed");

            return data;
        }

        /// <summary>Client-side: one converted kg template arrives (chunked). Saved into the local
        /// template store and the hammer tab refreshes live.</summary>
        private static void OnTemplateData(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                if (!global::FiresRPGmaker.Modules.Dialogues.DialogueSyncRPC.TryReadFilePacket(pkg, "tpl:", out _, out string fileName, out string content))
                    return; // still assembling chunks

                var data = JsonConvert.DeserializeObject<SavedNpcData>(content, TemplateJsonSettings);
                if (data == null) return;
                string name = Path.GetFileNameWithoutExtension(fileName);
                global::FiresRPGmaker.Managers.SavedNpcManager.SaveTemplate(name, data);
                try { global::FiresRPGmaker.Managers.NpcTemplatePieces.AddOrRefreshTemplateLive(name); } catch { }
                Console.instance?.AddString("[fires_migrate_kg] template installed: " + name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KgNpcMigrator] template receive failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Import the kg Marketplace config trees into OUR config structure: every profile family a kg
        /// world references (dialogues, quests+profiles+events, traders, bankers, gamblers, transmog,
        /// leaderboard achievements, server infos, buffers, territories) is copied from
        /// config/Marketplace/Configs into the matching FiresRPGmaker grouped folder, where our loaders
        /// parse it natively and the config watcher hot-reloads + syncs it to every client. Runs on the
        /// SERVER (the kg tree lives there); safe to re-run — copies overwrite.
        /// </summary>
        private static List<string> ImportConfigs()
        {
            var outLines = new List<string>();
            try
            {
                string src = Path.Combine(FiresCore.Compat.MarketplacePaths.MainPath, "Configs");
                outLines.Add("[fires_migrate_kg] CONFIGS: importing kg tree '" + src + "'");
                int copied = global::FiresRPGmaker.Modules.Marketplace.MarketplaceConsoleCommands.ImportKGConfigs(src, l => outLines.Add("  " + l));
                int reloaded = global::FiresRPGmaker.Modules.Marketplace.MarketplaceConsoleCommands.ReloadModule("all");
                outLines.Add("[fires_migrate_kg] CONFIGS complete: " + copied + " file(s) imported, " + reloaded +
                             " marketplace module(s) reloaded. Quest/dialogue/buffer reloads + client sync ride the config watcher (a few seconds).");
            }
            catch (Exception ex)
            {
                outLines.Add("[fires_migrate_kg] CONFIGS ERROR: " + ex.Message);
                Debug.LogError("[KgNpcMigrator] " + ex);
            }
            return outLines;
        }

        /// <summary>
        /// Undo the last confirm: destroy every MIGRATED StaticNpc (identified by the KGnpcProfile
        /// passthrough only migrated bodies carry) and recreate the kg MarketPlaceNPC ZDOs verbatim from
        /// the NEWEST backup JSON. Safe to iterate: rollback → fix code → confirm again.
        /// </summary>
        private static List<string> Rollback()
        {
            var outLines = new List<string>();
            try
            {
                string dir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Root, "Migration");
                string world = ZNet.instance?.GetWorldName() ?? "world";
                string backup = null;
                if (Directory.Exists(dir))
                {
                    var candidates = Directory.GetFiles(dir, "kg_npcs_" + world + "_*.json");
                    Array.Sort(candidates);
                    if (candidates.Length > 0) backup = candidates[candidates.Length - 1];
                }
                if (backup == null) { outLines.Add("[fires_migrate_kg] ROLLBACK: no backup json found for world '" + world + "'"); return outLines; }

                var records = JsonConvert.DeserializeObject<List<Record>>(File.ReadAllText(backup));

                // 1) remove the migrated Fires bodies (only they carry the KGnpcProfile passthrough).
                int removed = 0;
                var temp = new List<ZDO>();
                int idx = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(OurPrefabName, temp, ref idx)) { }
                foreach (var zdo in temp)
                {
                    if (zdo == null || !zdo.IsValid()) continue;
                    if (string.IsNullOrEmpty(zdo.GetString("KGnpcProfile", ""))) continue;
                    DestroyNetworkedZdo(zdo);
                    removed++;
                }

                // 2) recreate the kg NPCs exactly as backed up.
                int restored = 0;
                int kgHash = KgPrefabName.GetStableHashCode();
                foreach (var rec in records)
                {
                    var zdo = ZDOMan.instance.CreateNewZDO(rec.Pos, kgHash);
                    zdo.Persistent = true;
                    zdo.Distant = false;
                    zdo.Type = ZDO.ObjectType.Default;
                    zdo.SetPrefab(kgHash);
                    zdo.SetRotation(rec.Rot);
                    foreach (var kv in rec.S) zdo.Set(kv.Key, kv.Value);
                    foreach (var kv in rec.F) zdo.Set(kv.Key, kv.Value);
                    foreach (var kv in rec.I) zdo.Set(kv.Key, kv.Value);
                    foreach (var kv in rec.B) zdo.Set(kv.Key, kv.Value);
                    restored++;
                }
                outLines.Add("[fires_migrate_kg] ROLLBACK complete: removed " + removed + " migrated Fires NPC(s), restored " + restored + " kg NPC(s) from " + Path.GetFileName(backup));
            }
            catch (Exception ex)
            {
                outLines.Add("[fires_migrate_kg] ROLLBACK ERROR: " + ex.Message);
                Debug.LogError("[KgNpcMigrator] rollback: " + ex);
            }
            return outLines;
        }

        internal static List<Record> Scan()
        {
            var records = new List<Record>();
            if (ZDOMan.instance == null) return records;
            var temp = new List<ZDO>();
            int idx = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(KgPrefabName, temp, ref idx)) { }
            foreach (var zdo in temp)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                var rec = new Record
                {
                    Zdo = zdo,
                    ZdoId = zdo.m_uid.ToString(),
                    Pos = zdo.GetPosition(),
                    Rot = zdo.GetRotation(),
                };
                foreach (var k in KgStringKeys) { string v = zdo.GetString(k, ""); if (!string.IsNullOrEmpty(v)) rec.S[k] = v; }
                foreach (var k in KgFloatKeys) { float v = zdo.GetFloat(k, float.NaN); if (!float.IsNaN(v)) rec.F[k] = v; }
                foreach (var k in KgIntKeys) { int v = zdo.GetInt(k, 0); if (v != 0) rec.I[k] = v; }
                foreach (var k in KgBoolKeys) { bool v = zdo.GetBool(k, false); if (v) rec.B[k] = v; }
                records.Add(rec);
            }
            return records;
        }

        private static List<string> BuildReport(List<Record> records)
        {
            var lines = new List<string>();
            var byType = new Dictionary<string, int>();
            int patrols = 0, pins = 0, feedback = 0, dressed = 0, models = 0;
            foreach (var rec in records)
            {
                int kgType = rec.Int("KGmarketNPC");
                string typeName = KgTypeName(kgType);
                byType.TryGetValue(typeName, out int n); byType[typeName] = n + 1;
                if (!string.IsNullOrEmpty(rec.Str("KGmarket PatrolData"))) patrols++;
                if (rec.B.ContainsKey("KGmarketPinned")) pins++;
                if (kgType == 4) feedback++;
                if (!string.IsNullOrEmpty(rec.Str("KGnpcModelOverride"))) models++;
                if (!string.IsNullOrEmpty(rec.Str("KGhelmetItem")) || !string.IsNullOrEmpty(rec.Str("KGchestItem"))
                    || !string.IsNullOrEmpty(rec.Str("KGrightItem")) || !string.IsNullOrEmpty(rec.Str("KGhairItem"))) dressed++;

                // Flag models whose prefab isn't loaded HERE (props from mods the source server had).
                string mdl = rec.Str("KGnpcModelOverride");
                string mdlBase = System.Text.RegularExpressions.Regex.Replace(mdl, "@humanoid", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                bool modelMissing = !string.IsNullOrEmpty(mdlBase)
                    && !string.Equals(mdlBase, "Player", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(mdlBase, "Player_Female", StringComparison.OrdinalIgnoreCase)
                    && ZNetScene.instance != null && ZNetScene.instance.GetPrefab(mdlBase) == null;

                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "[{0}] '{1}' profile='{2}' dialogue='{3}' model='{4}' @({5:F0},{6:F0}){7}{8}{9}{10}",
                    typeName, rec.Str("KGnpcNameOverride"), rec.Str("KGnpcProfile"), rec.Str("KGnpcDialogue"),
                    mdl, rec.Pos.x, rec.Pos.z,
                    string.IsNullOrEmpty(rec.Str("KGmarket PatrolData")) ? "" : " [patrol]",
                    rec.B.ContainsKey("KGmarketPinned") ? " [pinned]" : "",
                    kgType == 4 ? " [FEEDBACK->None]" : "",
                    modelMissing ? " [MODEL MISSING]" : ""));
            }
            var summary = new List<string> { "-- summary --" };
            foreach (var kv in byType) summary.Add("  " + kv.Key + ": " + kv.Value);
            summary.Add("  model overrides: " + models + " | dressed (fashion items): " + dressed +
                        " | patrols: " + patrols + " | pinned: " + pins + " | feedback-type: " + feedback);
            summary.Add("-- npcs --");
            summary.AddRange(lines);
            return summary;
        }

        internal static string KgTypeName(int t)
        {
            string[] names = { "None", "Trader", "Info", "Teleporter", "Feedback", "Banker", "Gambler", "Quests", "Buffer", "Transmog", "Marketplace", "Mail" };
            return t >= 0 && t < names.Length ? names[t] : ("Type" + t);
        }

        private static void SwapOne(Record rec)
        {
            int prefabHash = OurPrefabName.GetStableHashCode();
            var zdo = ZDOMan.instance.CreateNewZDO(rec.Pos, prefabHash);
            zdo.Persistent = true;
            zdo.Distant = false;
            zdo.Type = ZDO.ObjectType.Default;
            // MANDATORY: CreateNewZDO's hash arg is only used for the sector index — it does NOT set the
            // ZDO's prefab FIELD. Without this SetPrefab the field stays 0, so ZNetScene.CreateObjects
            // can't resolve a prefab and DESTROYS the ZDO as an orphan (the "kg removed, ours never
            // placed" bug). Every raw-ZDO create MUST SetPrefab.
            zdo.SetPrefab(prefabHash);
            // kg's MarketPlaceNPC and our StaticNpc are both player rigs (face +Z at their stored yaw),
            // so kg's rotation applies DIRECTLY — no offset. (The earlier +180 "convention correction"
            // was a wrong guess and put most NPCs backwards.) Logged per-NPC below so any real offset is
            // provable from data, not eyeballed.
            Quaternion faceRot = rec.Rot;
            zdo.SetRotation(faceRot);
            // Ground-truth rotation trace: kg's raw stored quaternion (as read from its ZDO during Scan)
            // and the yaw we're actually writing. Read back from the DB immediately so the log proves the
            // SetRotation stuck, rather than assuming. Client-side StaticNpcInitializer logs the FINAL
            // applied transform.rotation — the two together bracket the whole rotation path end to end.
            Quaternion readBack = zdo.GetRotation();
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[fires_migrate_kg][rot] '{0}' @({1:F0},{2:F0}) kgRaw=({3:F3},{4:F3},{5:F3},{6:F3}) kgYaw={7:F1} -> wroteYaw={8:F1} dbReadbackYaw={9:F1}",
                rec.Str("KGnpcNameOverride"), rec.Pos.x, rec.Pos.z,
                rec.Rot.x, rec.Rot.y, rec.Rot.z, rec.Rot.w,
                rec.Rot.eulerAngles.y, faceRot.eulerAngles.y, readBack.eulerAngles.y));

            // Every mapped write is recorded so confirm can dump exactly what each NPC received
            // (swap_dump_*.json) — the raw kg record rides along in the same entry for diffing.
            var written = new Dictionary<string, object>();
            var notes = new List<string>();
            void Ws(string k, string v) { zdo.Set(k, v); written[k] = v; }
            void Wi(string k, int v) { zdo.Set(k, v); written[k] = v; }
            void Wb(string k, bool v) { zdo.Set(k, v); written[k] = v; }
            void Wf(string k, float v) { zdo.Set(k, v); written[k] = v; }
            void Wv(string k, Vector3 v) { zdo.Set(k, v); written[k] = v.x.ToString(CultureInfo.InvariantCulture) + "," + v.y.ToString(CultureInfo.InvariantCulture) + "," + v.z.ToString(CultureInfo.InvariantCulture); }
            void WsIf(string k, string v) { if (!string.IsNullOrEmpty(v)) Ws(k, v); }

            // ── identity ────────────────────────────────────────────────────────────────────────────
            int kgType = rec.Int("KGmarketNPC");
            NpcType ourType = TypeMap.TryGetValue(kgType, out var mapped) ? mapped : NpcType.None;
            string dialogue = rec.Str("KGnpcDialogue");
            // kg rides dialogues on ANY type; a typeless kg NPC that has one is a dialogue NPC for us.
            if (ourType == NpcType.None && !string.IsNullOrEmpty(dialogue)) ourType = NpcType.DialogueNpc;

            Wb("npc_initialized", true);
            Wb("npc_static_placement", true);
            // kg Marketplace NPCs can never die, so every swapped NPC — including patrol bodies —
            // carries the per-NPC infinite-health flag. Patrol NPCs keep their full contract
            // (route + defend-when-attacked); they just can't be killed. Hand-placed Fires NPCs
            // are untouched by this: the flag is stamped only on migrated bodies.
            Wb("npc_infinite_health", true);
            // kg bodies never rotate toward players (head-IK at most). Our stationed NPCs DO
            // greet-turn by design — suppress that for migrated bodies only, so bench/crafting
            // poses stay locked and standing NPCs hold their placed facing, exactly like kg.
            Wb("npc_no_face_player", true);
            Ws("npc_profile", "StaticNpc");
            Wi("npc_type", (int)ourType);
            Wv("npc_placed_anchor", rec.Pos);
            written["rotation"] = "applied yaw " + faceRot.eulerAngles.y.ToString("F1", CultureInfo.InvariantCulture) + "deg (kg raw yaw " + rec.Rot.eulerAngles.y.ToString("F1", CultureInfo.InvariantCulture) + "deg, no offset)";
            // Rotation is carried by SetRotation(faceRot) above — StaticNpcInitializer reads it via
            // zdo.GetRotation() on spawn. Do NOT seed npc_stationed/npc_has_stationed_pos here: those
            // route the body into the companion-stationed/reconciler path BEFORE the static initializer
            // runs, which stopped migrated bodies from spawning at all. StaticNpcInitializer sets the
            // stationed state itself, the correct way, after it takes over.
            // kg placed each NPC at an EXACT position (bench-seat height for sitters, counter for
            // crafters). Skip our downward surface-probe so the body stays at kg's Y instead of being
            // snapped to the terrain (which made sitting NPCs float above their seats).
            Wb("npc_exact_placement", true);

            string name = rec.Str("KGnpcNameOverride");
            WsIf("npc_name_override", name);
            WsIf("npc_dialogue_profile", dialogue);

            // kg map pin → first-class Fires pin (NpcMapPins renders these; keys also stay in the
            // dormant KG* passthrough for provenance).
            if (rec.Bool("KGmarketPinned")) Wb("npc_map_pinned", true);
            WsIf("npc_map_pin_icon", rec.Str("KGmarketPinIcon"));

            // Mirror kg's GET_NPC_Profile (defaults to "default" when the ZDO key is empty). Strip any kg
            // "=Flag" suffix (e.g. "botanistbuy_mp=True") and lowercase (kg's ChangeProfile lowercases), then
            // always write it so every migrated NPC lands with a resolvable profile — no runtime guessing.
            string profile = NormalizeKgProfile(rec.Str("KGnpcProfile"));
            if (ProfileKeyByType.TryGetValue(ourType, out var profileKey))
            {
                Ws(profileKey, profile);
                // Surface broken references at CONFIRM time instead of a silent empty UI at interact
                // time. These are usually broken in the kg source data too (profile renamed/deleted
                // while the placed NPC kept the old name).
                if (ourType == NpcType.Trader
                    && global::FiresRPGmaker.Managers.TraderProfileManager.GetProfile(profile.ToLowerInvariant()) == null)
                    notes.Add("TRADER PROFILE MISSING: '" + name + "' references '" + profile + "' but no such section exists in the imported trader configs (broken in kg too - fix the cfg or reassign)");
            }
            if (!string.IsNullOrEmpty(dialogue)
                && global::FiresRPGmaker.Modules.Dialogues.DialogueCodex.Get(dialogue) == null)
                notes.Add("DIALOGUE MISSING: '" + name + "' references '" + dialogue + "' but no such dialogue exists in the imported configs");

            // Model mapping. kg dresses player-rig NPCs via "Player[@humanoid]" / "Player_Female[@humanoid]"
            // overrides — but OUR StaticNpc body IS a player rig, so those map to the NATIVE body (female via
            // model index) instead of a redundant clone; they stay fully editable in the dressing room. Real
            // creature/prop overrides carry verbatim (@humanoid parsed by ApplyModelOverride), with a
            // missing-prefab note when the providing mod isn't installed here.
            string model = rec.Str("KGnpcModelOverride");
            string modelBase = System.Text.RegularExpressions.Regex.Replace(model ?? "", "@humanoid", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            bool femalePlayer = string.Equals(modelBase, "Player_Female", StringComparison.OrdinalIgnoreCase);
            bool malePlayer = string.Equals(modelBase, "Player", StringComparison.OrdinalIgnoreCase);
            if (femalePlayer)
            {
                zdo.Set(ZDOVars.s_modelIndex, 1);
                written["s_modelIndex"] = 1;
                Wb("npc_isfemale", true);
            }
            else if (!malePlayer && !string.IsNullOrEmpty(model))
            {
                Ws("npc_model_override", model);
                if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(modelBase) == null)
                    notes.Add("MODEL MISSING '" + model + "' for '" + name + "' @(" + (int)rec.Pos.x + "," + (int)rec.Pos.z + ") - install the providing mod, override applies once present");
            }

            float scale = rec.Flt("KGnpcScale", 1f);
            if (scale > 0.01f && Math.Abs(scale - 1f) > 0.001f) Wf("npc_scale", scale);

            // ── appearance (kg fashion → resolved Fires values + the master fashion JSON) ──────────
            string helmet = PickOne(rec.Str("KGhelmetItem"));
            string chest = PickOne(rec.Str("KGchestItem"));
            string legs = PickOne(rec.Str("KGlegsItem"));
            string cape = PickOne(rec.Str("KGcapeItem"));
            string left = PickOne(rec.Str("KGleftItem"));
            string right = PickOne(rec.Str("KGrightItem"));
            string leftBack = PickOne(rec.Str("KGLeftItemBack"));
            string rightBack = PickOne(rec.Str("KGRightItemBack"));
            string hair = PrefixStyle(PickOne(rec.Str("KGhairItem")), "Hair");
            string beard = PrefixStyle(PickOne(rec.Str("KGbeardItem")), "Beard");

            WsIf("npc_equip_helmet", helmet);
            WsIf("npc_equip_chest", chest);
            WsIf("npc_equip_legs", legs);
            WsIf("npc_equip_shoulder", cape);
            WsIf("npc_equip_lefthand", left);
            WsIf("npc_equip_righthand", right);
            WsIf("npc_equip_leftback", leftBack);
            SeedVanillaEquipment(zdo, helmet, chest, legs, cape, left, right, leftBack, rightBack);
            written["vanilla_equip_hashes"] = "seeded";
            WsIf("npc_equip_rightback", rightBack);
            WsIf("npc_hair", hair);
            WsIf("npc_beard", beard);
            // Ground-truth hair/beard trace: the RAW kg values and what we actually wrote. Brenja rendering
            // bald means one of: kg stored no KGhairItem (source blank), PrefixStyle produced a name with no
            // live style, or a helmet hides it. This line makes the source vs written values provable per NPC.
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[fires_migrate_kg][hair] '{0}' KGhairItem='{1}' KGbeardItem='{2}' -> npc_hair='{3}' npc_beard='{4}' hairColor='{5}' helmet='{6}'",
                rec.Str("KGnpcNameOverride"), rec.Str("KGhairItem"), rec.Str("KGbeardItem"),
                hair ?? "", beard ?? "", rec.Str("KGhairItemColor"), helmet ?? ""));

            if (ColorUtility.TryParseHtmlString(PickOne(rec.Str("KGhairItemColor")), out var hairCol))
            {
                zdo.Set(ZDOVars.s_hairColor, new Vector3(hairCol.r, hairCol.g, hairCol.b));
                written["s_hairColor"] = rec.Str("KGhairItemColor");
            }
            if (ColorUtility.TryParseHtmlString(PickOne(rec.Str("KGskinColor")), out var skinCol))
            {
                zdo.Set(ZDOVars.s_skinColor, new Vector3(skinCol.r, skinCol.g, skinCol.b));
                written["s_skinColor"] = rec.Str("KGskinColor");
            }

            int craftAnim = rec.Int("KGcraftingAnimation");
            var fashion = new NPCFashionData
            {
                LeftItem = left, RightItem = right, HelmetItem = helmet, ChestItem = chest,
                LegsItem = legs, CapeItem = cape,
                LeftItemHidden = leftBack, RightItemHidden = rightBack,
                HairItem = hair, HairColor = rec.Str("KGhairItemColor"),
                BeardItem = beard, BeardColor = rec.Str("KGbeardColor"),
                SkinColor = rec.Str("KGskinColor"),
                InteractAnimation = PickOne(rec.Str("KGinteractAnimation")),
                GreetAnimation = PickOne(rec.Str("KGgreetingAnimation")),
                ByeAnimation = PickOne(rec.Str("KGbyeAnimation")),
                GreetText = rec.Str("KGgreetingText"),
                ByeText = rec.Str("KGbyeText"),
                InteractAudioClip = rec.Str("KGinteractSound"),
                CraftingAnimation = craftAnim.ToString(CultureInfo.InvariantCulture),
                TextSize = rec.Flt("KGtextSize", 3f).ToString(CultureInfo.InvariantCulture),
                TextHeight = rec.Flt("KGtextHeight", 0f).ToString(CultureInfo.InvariantCulture),
                PeriodicAnimation = PickOne(rec.Str("KGperiodicAnimation")),
                PeriodicAnimationTime = rec.Flt("KGperiodicAnimationTime", 0f).ToString(CultureInfo.InvariantCulture),
                PeriodicSound = rec.Str("KGperiodicSound"),
                PeriodicSoundTime = rec.Flt("KGperiodicSoundTime", 0f).ToString(CultureInfo.InvariantCulture),
                ModelScale = scale.ToString(CultureInfo.InvariantCulture),
                // kg parity: head-IK look-at ONLY on NPCs with no crafting pose (kg adds CustomLookAt
                // solely when KGcraftingAnimation == 0). A bench-locked NPC must not even head-track.
                LookAtPlayer = craftAnim == 0 ? "true" : "false",
            };
            Ws("npc_fashion_json", JsonConvert.SerializeObject(fashion));

            // ── patrol ("x1,z1,x2,z2,...[@maxSpeed]" → Fires route + assignment) ────────────────────
            string patrol = rec.Str("KGmarket PatrolData");
            if (!string.IsNullOrEmpty(patrol))
            {
                try
                {
                    var points = ParsePatrol(patrol);
                    if (points.Count >= 2)
                    {
                        // kg patrol NPCs keep patrolling: route converted + ASSIGNED. Assignment moves the
                        // body out of the static stage onto the wander/patrol contract — it walks the route
                        // and defends itself when attacked — while the migration-stamped infinite-health
                        // flag keeps it unkillable, exactly like the kg original. Every NON-patrol NPC
                        // stays a static-stage fixture (unconditionally invulnerable + unaggroable).
                        string routeName = "kg_" + Sanitize(string.IsNullOrEmpty(name) ? profile : name) + "_" + Math.Abs(rec.ZdoId.GetHashCode() % 10000);
                        FiresCore.Npc.Patrol.PatrolRouteManager.SaveRoute(routeName, points);
                        Ws("npc_patrol_route", routeName);
                        notes.Add("patrol NPC '" + (string.IsNullOrEmpty(name) ? profile : name) + "' assigned route '" + routeName + "' (patrols + defends, unkillable)");
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[KgNpcMigrator] patrol conversion failed for " + rec.ZdoId + ": " + ex.Message); }
            }

            // ── passthrough: EVERY kg key rides along (dormant data for future features / audits) ──
            foreach (var kv in rec.S) zdo.Set(kv.Key, kv.Value);
            foreach (var kv in rec.F) zdo.Set(kv.Key, kv.Value);
            foreach (var kv in rec.I) zdo.Set(kv.Key, kv.Value);
            foreach (var kv in rec.B) zdo.Set(kv.Key, kv.Value);
            written["(passthrough)"] = (rec.S.Count + rec.F.Count + rec.I.Count + rec.B.Count) + " raw KG* keys carried verbatim";

            // ── retire the kg NPC ───────────────────────────────────────────────────────────────────
            // Plain DestroyZDO silently no-ops on ZDOs the server doesn't OWN — and the admin running
            // the command is standing in town, so their client owns every loaded kg NPC. That left the
            // originals alive inside our swapped bodies ("doubles").
            // GROUND-TRUTH self-check: read the body ZDO we just built back from the DB and confirm it
            // actually persisted with the right prefab hash. This catches the whole class of "created
            // but unspawnable" bugs (a missing SetPrefab leaves prefab=0 and ZNetScene silently culls
            // the body) at confirm time, instead of the admin discovering empty zones later.
            if (zdo.IsValid() && zdo.GetPrefab() == prefabHash) _bodiesValid++;
            else { _bodiesBroken++; notes.Add("BODY INVALID for '" + name + "': prefab=" + zdo.GetPrefab() + " valid=" + zdo.IsValid() + " (expected " + prefabHash + ") - it will NOT spawn"); }

            MintAnchor(zdo, rec.Pos, rec.Rot, name, notes);

            DestroyNetworkedZdo(rec.Zdo);

            _swapDump.Add(new SwapDumpEntry
            {
                KgName = name,
                KgProfile = profile,
                KgTypeName = KgTypeName(kgType),
                OurType = ourType.ToString(),
                PosXZ = "(" + (int)rec.Pos.x + "," + (int)rec.Pos.z + ")",
                Written = written,
                Notes = notes,
                Kg = rec,
            });
            _swapNotes.AddRange(notes);
        }

        /// <summary>One confirm-dump row: the raw kg record plus exactly what we wrote for it.</summary>
        private class SwapDumpEntry
        {
            public string KgName;
            public string KgProfile;
            public string KgTypeName;
            public string OurType;
            public string PosXZ;
            public Dictionary<string, object> Written;
            public List<string> Notes;
            public Record Kg;
        }

        private static readonly List<SwapDumpEntry> _swapDump = new List<SwapDumpEntry>();

        private static void SetIf(ZDO zdo, string key, string value)
        {
            if (!string.IsNullOrEmpty(value)) zdo.Set(key, value);
        }

        // Confirm-pass telemetry: how many kg NPCs were retired from LIVE (instantiated) zones vs
        // DORMANT (unloaded) ones — the dormant path is the whole-world guarantee.
        private static int _retiredLive;
        private static int _bodiesValid;
        private static int _bodiesBroken;
        private static int _retiredDormant;
        private static int _anchorsMinted;

        // The anchor is minted HERE, as a raw ZDO, for the same reason the body is: a dedicated server never
        // instantiates world objects near players (its reference position is parked off-world), so
        // StaticNpcInitializer.Start - and with it EnsureAnchorForBody - never runs server-side. Leaving anchors
        // to that "organic" path left every migrated body anchorless on a dedi (census: 69 StaticNpc, 0 anchors).
        // Same contract as EnsureAnchorForBody minus the surface probe, which needs colliders a dedi does not
        // have loaded: the anchor sits at kg's stored feet position and HealAnchorHeight re-seats it later.
        private static void MintAnchor(ZDO bodyZdo, Vector3 pos, Quaternion rot, string name, List<string> notes)
        {
            string anchorName = global::FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName;
            int anchorHash = anchorName.GetStableHashCode();
            if (ZNetScene.instance == null || ZNetScene.instance.GetPrefab(anchorHash) == null)
            {
                notes.Add("ANCHOR PREFAB MISSING for '" + name + "': " + anchorName + " is not registered - body seeded without an anchor (old bundle?)");
                return;
            }
            var anchor = ZDOMan.instance.CreateNewZDO(pos, anchorHash);
            anchor.Persistent = true;
            anchor.Distant = false;
            anchor.Type = ZDO.ObjectType.Default;
            anchor.SetPrefab(anchorHash);
            anchor.SetRotation(rot);
            string guid = System.Guid.NewGuid().ToString("N");
            anchor.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.Guid, guid);
            anchor.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyPrefab, OurPrefabName);
            anchor.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.Rotation, rot);
            bodyZdo.Set(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, guid);
            global::FiresCore.Bridge.NpcAnchorBridge.InvokeCaptureToAnchor(bodyZdo, anchor);
            anchor.SetConnection(ZDOExtraData.ConnectionType.Spawned, bodyZdo.m_uid);
            _anchorsMinted++;
        }

        // Repair for a world swapped before anchors were minted at confirm time: every StaticNpc body without an
        // anchor back-reference gets one, from ZDO data alone, so a re-swap is not needed to fix the census.
        private static List<string> MintMissingAnchors()
        {
            var outL = new List<string>();
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { outL.Add("[fires_migrate_kg] anchors: server only"); return outL; }
            var bodies = new List<ZDO>();
            int idx = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(OurPrefabName, bodies, ref idx)) { }
            _anchorsMinted = 0;
            int already = 0;
            var notes = new List<string>();
            foreach (var body in bodies)
            {
                if (body == null || !body.IsValid()) continue;
                if (!string.IsNullOrEmpty(body.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, ""))) { already++; continue; }
                MintAnchor(body, body.GetVec3("npc_placed_anchor", body.GetPosition()), body.GetRotation(), body.GetString("npc_name_override", "?"), notes);
            }
            try { ZNet.instance.Save(false); } catch (Exception ex) { outL.Add("  save failed: " + ex.Message); }
            outL.Add("[fires_migrate_kg] ANCHORS: " + _anchorsMinted + " minted, " + already + " already anchored, " + bodies.Count + " StaticNpc bod(ies) scanned (world saved).");
            foreach (var n in notes) outL.Add("  ! " + n);
            return outL;
        }

        // Vanilla VisEquipment dresses a body from the item hashes in ITS ZDO: UpdateEquipmentVisuals reads
        // s_chestItem & co. whenever a ZDO exists, and the Set*Item setters write those hashes only on the
        // owner. A migrated body is created here on the server, so the client-side apply runs before that
        // client owns it and the hash never lands - the body stays naked on every client (templates, which
        // have no ZDO, and player-placed NPCs, client-owned from birth, are unaffected). Seeding the hashes
        // while the server still owns the ZDO is the mechanism NpcTemplateAppearance.SeedLookOntoBody uses.
        private static void SeedVanillaEquipment(ZDO zdo, string helmet, string chest, string legs, string shoulder,
                                                 string left, string right, string leftBack, string rightBack)
        {
            if (zdo == null) return;
            zdo.Set(ZDOVars.s_helmetItem, ItemHash(helmet));
            zdo.Set(ZDOVars.s_chestItem, ItemHash(chest));
            zdo.Set(ZDOVars.s_legItem, ItemHash(legs));
            zdo.Set(ZDOVars.s_shoulderItem, ItemHash(shoulder));
            zdo.Set(ZDOVars.s_shoulderItemVariant, 0);
            zdo.Set(ZDOVars.s_rightItem, ItemHash(right));
            zdo.Set(ZDOVars.s_leftItem, ItemHash(left));
            zdo.Set(ZDOVars.s_leftItemVariant, 0);
            zdo.Set(ZDOVars.s_rightBackItem, ItemHash(rightBack));
            zdo.Set(ZDOVars.s_leftBackItem, ItemHash(leftBack));
        }

        private static int ItemHash(string name) => string.IsNullOrEmpty(name) ? 0 : name.GetStableHashCode();

        /// <summary>`fires_migrate_kg dress`: repair for bodies migrated before the swap seeded the vanilla
        /// hashes - takes ownership (so the write is authoritative and synced) and seeds them from the
        /// npc_equip_* slots the swap already wrote. Idempotent.</summary>
        private static List<string> DressMigratedBodies()
        {
            var outL = new List<string>();
            if (ZNet.instance == null || !ZNet.instance.IsServer()) { outL.Add("[fires_migrate_kg] dress: server only"); return outL; }
            var bodies = new List<ZDO>();
            int idx = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(OurPrefabName, bodies, ref idx)) { }
            int dressed = 0, bare = 0;
            foreach (var body in bodies)
            {
                if (body == null || !body.IsValid()) continue;
                string helmet = body.GetString("npc_equip_helmet", "");
                string chest = body.GetString("npc_equip_chest", "");
                string legs = body.GetString("npc_equip_legs", "");
                string shoulder = body.GetString("npc_equip_shoulder", "");
                string left = body.GetString("npc_equip_lefthand", "");
                string right = body.GetString("npc_equip_righthand", "");
                string leftBack = body.GetString("npc_equip_leftback", "");
                string rightBack = body.GetString("npc_equip_rightback", "");
                if (string.IsNullOrEmpty(helmet + chest + legs + shoulder + left + right + leftBack + rightBack)) { bare++; continue; }
                body.SetOwner(ZDOMan.GetSessionID());
                SeedVanillaEquipment(body, helmet, chest, legs, shoulder, left, right, leftBack, rightBack);
                dressed++;
            }
            try { ZNet.instance.Save(false); } catch (Exception ex) { outL.Add("  save failed: " + ex.Message); }
            outL.Add("[fires_migrate_kg] DRESS: vanilla equipment hashes seeded on " + dressed + " StaticNpc bod(ies) from their npc_equip_* slots, "
                + bare + " carry no equipment, " + bodies.Count + " scanned. Bodies already loaded on a client re-dress from the ZDO within a frame.");
            return outL;
        }

        /// <summary>Destroys a networked object regardless of who owns it or whether its zone is
        /// loaded — the exact WorldEditCommands.RemoveZDO recipe: take ownership FIRST, then if a live
        /// instance exists ZNetScene.Destroy the GameObject, else DestroyZDO the dormant record. On a
        /// server this reaches every persistent ZDO in the world (the whole ZDO DB lives in
        /// m_objectsBySector), so it works uniformly across loaded AND unloaded zones.</summary>
        private static void DestroyNetworkedZdo(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return;
            zdo.SetOwner(ZDOMan.GetSessionID());
            var znet = ZNetScene.instance;
            if (znet != null && znet.m_instances.TryGetValue(zdo, out var view) && view != null)
            {
                znet.Destroy(view.gameObject);
                _retiredLive++;
            }
            else
            {
                ZDOMan.instance.DestroyZDO(zdo);
                _retiredDormant++;
            }
        }

        /// <summary>Whole-world census of surviving kg NPC ZDOs (loaded or not) — the post-swap
        /// self-check that proves the retire pass covered unloaded zones too.</summary>
        private static int CountRemainingKgNpcs()
        {
            var temp = new List<ZDO>();
            int idx = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(KgPrefabName, temp, ref idx)) { }
            int count = 0;
            foreach (var z in temp) if (z != null && z.IsValid()) count++;
            return count;
        }

        /// <summary>Whole-world persistence census — run AFTER a relog to prove what actually survived the
        /// save/load cycle. Counts every StaticNpc ZDO (loaded or not) and breaks down how many kept their
        /// migration data (npc_initialized + name + dialogue + fashion), and how many kg MarketPlaceNPC ZDOs
        /// came back (a non-zero count post-swap = the retire didn't persist → kg revives its NPCs). This is
        /// the ground truth for the "correct data on first run, lost/scrambled on relog" report.</summary>
        private static List<string> Census()
        {
            var outL = new List<string>();
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                outL.Add("[census] must run server-side (no whole-world ZDO view on a client)");
                return outL;
            }

            var statics = new List<ZDO>();
            int idx = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(OurPrefabName, statics, ref idx)) { }

            int total = 0, initialized = 0, uninit = 0, withName = 0, withDialogue = 0, withFashion = 0, migratedTag = 0;
            var scrambleCandidates = new List<string>();
            foreach (var z in statics)
            {
                if (z == null || !z.IsValid()) continue;
                total++;
                bool init = z.GetBool("npc_initialized", false);
                bool hasName = !string.IsNullOrEmpty(z.GetString("npc_name_override", ""));
                bool hasDlg = !string.IsNullOrEmpty(z.GetString("npc_dialogue_profile", ""));
                bool hasFash = !string.IsNullOrEmpty(z.GetString("npc_fashion_json", ""));
                // A migrated body carries the kg profile passthrough; distinguishes our swaps from any
                // hand-placed Fires StaticNpc that happens to share the world.
                bool migr = !string.IsNullOrEmpty(z.GetString("KGnpcProfile", "")) || !string.IsNullOrEmpty(z.GetString("KGnpcNameOverride", ""));
                if (init) initialized++; else uninit++;
                if (hasName) withName++;
                if (hasDlg) withDialogue++;
                if (hasFash) withFashion++;
                if (migr) migratedTag++;
                // The scramble signature: looks migrated but lost npc_initialized → will randomize on spawn.
                if (migr && !init)
                {
                    var p = z.GetPosition();
                    scrambleCandidates.Add(string.Format(CultureInfo.InvariantCulture,
                        "    ({0:F0},{1:F0}) name='{2}' dlg='{3}' fashion={4}B init=FALSE",
                        p.x, p.z, z.GetString("npc_name_override", ""), z.GetString("npc_dialogue_profile", ""),
                        z.GetString("npc_fashion_json", "").Length));
                }
            }

            int kg = CountRemainingKgNpcs();

            // Anchor census: each migrated body should have exactly ONE FiresNpcAnchor (the server-side keeper
            // that re-links to its body by GUID on load, or SPAWNS A FRESH BLANK StaticNpc if it can't find it —
            // that fresh blank is the scramble source). anchors >> bodies, or bodies == 2×anchors, points at the
            // anchor spawning duplicates/blanks instead of re-linking the migrated body.
            int anchors = 0;
            try
            {
                var abuf = new List<ZDO>(); int aidx = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(FiresCore.Npc.Anchor.NpcAnchorFields.AnchorPrefabName, abuf, ref aidx)) { }
                foreach (var z in abuf) if (z != null && z.IsValid()) anchors++;
            }
            catch (Exception ex) { outL.Add("  (anchor count failed: " + ex.Message + ")"); anchors = -1; }

            outL.Add("[census] whole-world persistence check for '" + (ZNet.instance.GetWorldName() ?? "world") + "':");
            outL.Add("  StaticNpc ZDOs (loaded+unloaded): " + total);
            outL.Add("    initialized: " + initialized + " | UNINITIALIZED: " + uninit + " (uninitialized migrated bodies randomize into generic QuestNpcs)");
            outL.Add("    carry migration tag (KGnpcProfile): " + migratedTag);
            outL.Add("    with name: " + withName + " | with dialogue: " + withDialogue + " | with fashion json: " + withFashion);
            outL.Add("  FiresNpcAnchor ZDOs: " + anchors + " (should ≈ StaticNpc count; anchors spawn a BLANK StaticNpc when they can't re-link their body → scramble)");
            outL.Add("  kg MarketPlaceNPC ZDOs still present: " + kg + (kg > 0 ? "  <-- kg NPCs SURVIVED/REVIVED (retire didn't persist, or kg re-placed)" : "  (clean - none)"));
            if (scrambleCandidates.Count > 0)
            {
                outL.Add("  SCRAMBLE CANDIDATES (migrated but lost npc_initialized -> will show as random QuestNpc):");
                outL.AddRange(scrambleCandidates);
            }
            outL.Add("[census] EXPECTED after a good swap+relog: ~69 StaticNpc, ~all initialized, 0 kg MarketPlaceNPC, 0 scramble candidates.");
            return outL;
        }

        // kg random-list semantics (space-separated) — resolve ONCE at migration so all downstream
        // stores agree on the same pick.
        private static string PickOne(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.Contains(" ")) return value;
            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? value : parts[UnityEngine.Random.Range(0, parts.Length)];
        }

        // kg stores hair/beard as the SUFFIX ("14" → prefab Hair14).
        private static string PrefixStyle(string value, string prefix)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return value;
            return prefix + value;
        }

        private static List<Vector3> ParsePatrol(string data)
        {
            var points = new List<Vector3>();
            var split = data.Split('@');
            var coords = split[0].Replace(" ", "").Split(',');
            for (int i = 0; i + 1 < coords.Length; i += 2)
            {
                float x = Convert.ToSingle(coords[i], CultureInfo.InvariantCulture);
                float z = Convert.ToSingle(coords[i + 1], CultureInfo.InvariantCulture);
                float y = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0f, z)) : 30f;
                points.Add(new Vector3(x, y, z));
            }
            return points;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "npc";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            string result = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "npc" : (result.Length > 24 ? result.Substring(0, 24) : result);
        }

        #endregion
    }
}
