using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using FiresCore.Npc;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Migration
{
    // ── `rpgmaker_migrate_test run|verify`: proves `fires_migrate_kg all` really swaps kg Marketplace NPCs for FiresRPGmaker
    // NPCs on a world WITHOUT the kg plugin (Fire 10-01: "we have never definitively proven that our migration command …
    // actually works"). On-demand F5 contract test: without arguments every step SKIPs.
    // The destructive half runs on the SERVER (it alone sees every ZDO) and refuses unless the world's save is
    // "GandheimMigrate", a disposable copy; the client asks it over a routed RPC (admin-gated, like fires_migrate_kg).
    //   run:    1 guard   world name, no kg plugin on the server or this client, Marketplace\Configs on the server
    //           2 before  census of every MarketPlaceNPC ZDO by kg type; written to Migration\migratetest_before_<world>.json
    //           3 migrate KgNpcMigrator.RunAll, the code `fires_migrate_kg all` runs; exceptions counted over it
    //           4 after   one row per kg type: every NPC replaced at its position (<= 0.5 m) with its name, mapped type,
    //                     profile and dialogue; its trader/quest profile and dialogue resolve in the loaded configs;
    //                     no kg ZDO left
    //           5 trade   the client walks to a migrated Trader and interacts with it the way E does
    //                     (NpcController.Interact): the trade window opens with the imported stock
    //           6 save    the server saves the world
    //   verify (after a dedi restart): 1 guard, 2 the step 4 rows against the saved before-census, 3 trade again.
    internal static class RpgMakerMigrateTest
    {
        internal const string WorldSave = "GandheimMigrate";
        private const string Command = "rpgmaker_migrate_test", Tag = "[RpgMakerMigrateTest]";
        private const string ModName = global::FiresRPGmaker.FiresRPGmaker.PluginName, ModVersion = global::FiresRPGmaker.FiresRPGmaker.PluginVersion;
        private const string RpcAsk = "Fires_MigrateTest_Ask", RpcReply = "Fires_MigrateTest_Reply";
        private const string Pass = "PASS", Fail = "FAIL", Skip = "SKIP";
        private const string KgPrefab = "MarketPlaceNPC", OurPrefab = "StaticNpc";
        private const float MatchMetres = 0.5f, ReplySeconds = 180f, ArriveGiveUpSeconds = 60f, ArriveSettleSeconds = 6f;
        private const float NpcReach = 4f, WindowWaitSeconds = 2f, ShotHoldSeconds = 1.5f, DestroySettleSeconds = 10f;
        private const int ReplyCap = 14000;

        private static bool s_running;
        private static string s_reply;
        private static int s_step, s_total, s_passes, s_fails, s_skips;

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class RegisterCommand
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand(Command,
                    "[F5 test] [no relaunch] prove fires_migrate_kg all on the disposable world 'GandheimMigrate' (no kg plugin): census, migrate, "
                    + "per-type rows, a migrated trader's window, save. Args: run | verify (on demand; without them every step SKIPs).",
                    args => Start(args.Length > 1 ? args.Args[1].ToLowerInvariant() : ""), isCheat: false, isNetwork: false, onlyServer: false);
            }
        }

        [HarmonyPatch(typeof(ZNet), "Start")]
        private static class RegisterRpcs
        {
            private static void Postfix()
            {
                if (ZRoutedRpc.instance == null) return;
                ZRoutedRpc.instance.Register<string>(RpcAsk, OnAsk);
                ZRoutedRpc.instance.Register<string>(RpcReply, (sender, text) => s_reply = text);
            }
        }

        // ── Client: the test ────────────────────────────────────────────────────────────────────────────────────
        private static void Start(string mode)
        {
            if (s_running) { Say($"{Tag} a run is already going."); return; }
            if (ZNet.instance == null || Player.m_localPlayer == null) { Say($"{Tag} join a world first."); return; }
            if (mode != "" && mode != "run" && mode != "verify") { Say($"{Tag} usage: {Command} run | verify; nothing started."); return; }
            ZNet.instance.StartCoroutine(Run(mode));
        }

        private static IEnumerator Run(string mode)
        {
            s_running = true;
            s_step = s_passes = s_fails = s_skips = 0;
            string[] names = mode == "verify" ? new[] { "guard", "verify", "trade" } : new[] { "guard", "before", "migrate", "after", "trade", "save" };
            s_total = names.Length;
            float started = Time.realtimeSinceStartup;
            try
            {
                // "(hash)" is part of the runner's BEGIN contract (FDT FiresTests BeginLine); without it L1's BEGIN went
                // unrecognised and the runner launched the test a second time.
                Say($"{Tag} BEGIN {ModName} {ModVersion} ({DllHash()}) on client '{Player.m_localPlayer.GetPlayerName()}' "
                    + $"{(mode == "" ? "no mode" : mode)}: {s_total} steps, ~{(mode == "verify" ? 90 : 240)} s");
                if (mode == "")
                {
                    foreach (string n in names) Step(n, Skip, $"on demand: run {Command} run | verify (world '{WorldSave}' only)");
                    yield break;
                }
                string localKg = KgPluginLoaded();
                yield return Ask("guard");
                var guard = Rows(s_reply);
                bool guardOk = s_reply != null && localKg == null && guard.All(r => r.Result == Pass);
                Step("guard", guardOk ? Pass : Fail, (s_reply == null ? "no reply from the server" : string.Join("; ", guard.Select(r => r.Detail)))
                    + (localKg != null ? $"; this client has the kg plugin loaded ({localKg})" : "; no kg plugin on this client"));
                if (!guardOk) { SkipRest(names); yield break; }

                List<Row> rows;
                if (mode == "run")
                {
                    yield return Ask("before");
                    rows = Rows(s_reply);
                    var census = rows.FirstOrDefault(r => r.Name == "census");
                    foreach (var r in rows.Where(r => r.Name == "npc")) Say($"{Tag}   {r.Detail}");
                    Step("before", census != null && census.Result == Pass ? Pass : Fail, census?.Detail ?? (s_reply == null ? "no reply from the server" : string.Join("; ", rows.Select(r => r.Detail))));
                    if (census == null || census.Result != Pass) { SkipRest(names); yield break; }

                    yield return Ask("run");
                    rows = Rows(s_reply);
                    var run = rows.FirstOrDefault(r => r.Name == "migrate");
                    Step("migrate", run != null && run.Result == Pass ? Pass : Fail, s_reply == null ? "no reply from the server" : string.Join("; ", rows.Select(r => r.Detail)));
                    // The swap's ZDO destroys land a moment after RunAll returns (L1 counted 69 kg ZDOs left; a re-scan
                    // seconds later found none), so the after-count waits for them.
                    yield return new WaitForSecondsRealtime(DestroySettleSeconds);
                }
                yield return Ask("after");
                rows = Rows(s_reply);
                var typeRows = rows.Where(r => r.Name != "sample").ToList();
                foreach (var r in typeRows) Say($"{Tag}   {r.Name} {r.Result} - {r.Detail}");
                bool afterOk = s_reply != null && typeRows.Count > 0 && typeRows.All(r => r.Result == Pass);
                Step(mode == "verify" ? "verify" : "after", afterOk ? Pass : Fail, s_reply == null ? "no reply from the server"
                    : $"{typeRows.Count(r => r.Result == Pass)} of {typeRows.Count} row(s) PASS: " + string.Join(" | ", typeRows.Select(r => $"{r.Name} {r.Result}")));

                var trader = rows.FirstOrDefault(r => r.Name == "sample" && r.Detail.StartsWith("Trader "));
                if (trader == null) Step("trade", Skip, "no migrated Trader in this world");
                else yield return TradeWindow(trader.Detail);

                if (mode == "run")
                {
                    yield return Ask("save");
                    rows = Rows(s_reply);
                    var save = rows.FirstOrDefault(r => r.Name == "save");
                    Step("save", save != null && save.Result == Pass ? Pass : Fail, save?.Detail ?? "no reply from the server");
                }
            }
            finally
            {
                Say($"{Tag} END {(s_fails > 0 ? Fail : Pass)}: {s_passes} pass, {s_fails} fail, {s_skips} skip in {Time.realtimeSinceStartup - started:0} s");
                s_running = false;
            }
        }

        // "Trader <x> <y> <z> <name…>": go there and interact with the migrated NPC the way E does.
        private static IEnumerator TradeWindow(string sample)
        {
            var parts = sample.Split(new[] { ' ' }, 5);
            var inv = CultureInfo.InvariantCulture;
            var at = new Vector3(float.Parse(parts[1], inv), float.Parse(parts[2], inv), float.Parse(parts[3], inv));
            string name = parts.Length > 4 ? parts[4] : "";
            var player = Player.m_localPlayer;
            player.TeleportTo(at + new Vector3(2f, 1f, 0f), player.transform.rotation, true);
            float giveUp = Time.realtimeSinceStartup + ArriveGiveUpSeconds;
            while (Time.realtimeSinceStartup < giveUp && (Player.m_localPlayer == null || Player.m_localPlayer.IsTeleporting() || !ZoneSystem.instance.IsZoneLoaded(at)))
                yield return null;
            yield return new WaitForSecondsRealtime(ArriveSettleSeconds);

            UI.NpcController npc = null;
            float best = NpcReach;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<UI.NpcController>())
            {
                float d = Utils.DistanceXZ(c.transform.position, at);
                if (d < best) { best = d; npc = c; }
            }
            if (npc == null) { Step("trade", Fail, $"no NPC within {NpcReach:0} m of the migrated trader '{name}' at ({at.x:0}, {at.z:0}) after arriving"); yield break; }

            int errors = 0;
            void Count(string text, string trace, LogType type) { if (type == LogType.Exception) errors++; }
            Application.logMessageReceived += Count;
            try { npc.Interact(Player.m_localPlayer, false, false); }
            catch (Exception ex) { errors++; Say($"{Tag}   interact threw: {ex.GetType().Name}: {ex.Message}"); }
            float until = Time.realtimeSinceStartup + WindowWaitSeconds;
            var ui = UI.Screens.TraderUIController.Instance;
            while (Time.realtimeSinceStartup < until && (ui == null || !ui.IsVisible)) { yield return null; ui = UI.Screens.TraderUIController.Instance; }
            Application.logMessageReceived -= Count;

            bool open = ui != null && ui.IsVisible;
            int rows = 0;
            if (open && AccessTools.Field(typeof(UI.Screens.TraderUIController), "_rows")?.GetValue(ui) is List<GameObject> list) rows = list.Count(g => g != null);
            string profile = npc.traderProfile;
            int trades = TraderProfileManager.GetProfile(profile ?? "")?.Trades.Count ?? 0;
            if (open)
            {
                Vector3 p = npc.transform.position + Vector3.up * 1.5f;
                Say($"{Tag} SHOT " + "{" + string.Format(inv, "{0:0.0},{1:0.0},{2:0.0},{3:0.0}", p.x, p.y, p.z, 4f) + "} the migrated trader's window");
                yield return new WaitForSecondsRealtime(ShotHoldSeconds);
                UI.Screens.TraderUIController.HideIfVisible();
            }
            Step("trade", open && rows > 0 && errors == 0 ? Pass : Fail,
                $"'{npc.GetDisplayName()}' ({best:0.0} m from the census position, profile '{profile}', {trades} trade(s) in it): "
                + $"window {(open ? "OPEN" : "not open")}, {rows} row(s) shown, {errors} exception(s)");
        }

        private static IEnumerator Ask(string op)
        {
            s_reply = null;
            if (ZNet.instance.IsServer()) { s_reply = Answer(op, ZRoutedRpc.instance.GetServerPeerID()); yield break; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAsk, op);
            float until = Time.realtimeSinceStartup + ReplySeconds;
            while (s_reply == null && Time.realtimeSinceStartup < until) yield return null;
        }

        private sealed class Row { public string Name, Result, Detail; }

        // Reply lines "ROW <name>|<result>|<detail>".
        private static List<Row> Rows(string reply)
        {
            var rows = new List<Row>();
            if (reply == null) return rows;
            foreach (var line in reply.Split('\n'))
            {
                if (!line.StartsWith("ROW ")) continue;
                var p = line.Substring(4).Split(new[] { '|' }, 3);
                if (p.Length == 3) rows.Add(new Row { Name = p[0], Result = p[1], Detail = p[2].TrimEnd('\r') });
            }
            return rows;
        }

        private static void SkipRest(string[] names)
        {
            while (s_step < s_total) Step(names[s_step], Skip, "an earlier step failed");
        }

        private static void Step(string name, string result, string detail)
        {
            s_step++;
            if (result == Pass) s_passes++; else if (result == Fail) s_fails++; else s_skips++;
            Say($"{Tag} {s_step}/{s_total} {name} {result} - {detail}");
        }

        // The log line, the console, and BepInEx\FiresTests\rpgmaker_migrate_test.txt, which FDT's runner also reads.
        private static void Say(string line)
        {
            Debug.Log(line);
            if (Console.instance != null) Console.instance.Print(line);
            try
            {
                string folder = Path.Combine(BepInEx.Paths.BepInExRootPath, "FiresTests");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, Command + ".txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch (Exception) { }
        }

        // A loaded plugin that is kg's Marketplace (any non-Fires plugin naming Marketplace), or null.
        private static string KgPluginLoaded()
        {
            foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
            {
                string guid = kv.Key ?? "", name = kv.Value?.Metadata?.Name ?? "";
                if (guid.StartsWith("com.Fire.", StringComparison.OrdinalIgnoreCase)) continue;
                if (guid.IndexOf("Marketplace", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Marketplace", StringComparison.OrdinalIgnoreCase) >= 0)
                    return $"{guid} '{name}'";
            }
            return null;
        }

        // ── Server: the destructive half ───────────────────────────────────────────────────────────────────────
        private static void OnAsk(long sender, string op)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            string text = KgNpcMigrator.IsSenderAdmin(sender) ? Answer(op, sender) : "ROW guard|FAIL|denied: admin only";
            if (text.Length > ReplyCap) text = text.Substring(0, ReplyCap) + "\n(truncated; full detail in the server log)";
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcReply, text);
        }

        private static string Answer(string op, long sender)
        {
            var sb = new StringBuilder();
            void Row(string name, string result, string detail)
            {
                string line = $"ROW {name}|{result}|{detail.Replace('\n', ' ')}";
                sb.Append(line).Append('\n');
                Debug.Log("[RpgMakerMigrateTest] server " + line);
            }
            try
            {
                string save = ZNet.World != null ? ZNet.World.m_worldName : "";
                if (save != WorldSave) { Row("guard", Fail, $"the server's world save is '{save}', not '{WorldSave}': this test changes the world and runs only on that copy"); return sb.ToString(); }
                switch (op)
                {
                    case "guard":
                    {
                        string kg = KgPluginLoaded();
                        bool prefab = ZNetScene.instance != null && ZNetScene.instance.GetPrefab(KgPrefab) != null;
                        string configs = Path.Combine(FiresCore.Compat.MarketplacePaths.MainPath, "Configs");
                        bool haveConfigs = Directory.Exists(configs);
                        Row("world", Pass, $"server world save '{save}' (name inside '{ZNet.World.m_name}')");
                        Row("kg", kg == null && !prefab ? Pass : Fail, kg == null && !prefab ? "no kg plugin and no MarketPlaceNPC prefab on the server"
                            : $"kg present on the server: plugin {kg ?? "none"}, prefab {(prefab ? "registered" : "absent")}");
                        Row("configs", haveConfigs ? Pass : Fail, haveConfigs ? $"Marketplace configs at {configs}" : $"no Marketplace configs at {configs}");
                        break;
                    }
                    case "before":
                    {
                        // 0.0.493: a census is written ONCE per copy. L1's relaunch overwrote the 69-NPC census with an
                        // empty one after the migration had run; an existing census is now never replaced.
                        if (File.Exists(BeforePath()))
                        {
                            Row("census", Fail, $"a census already exists at {BeforePath()} ({File.GetLastWriteTime(BeforePath()):HH:mm:ss}); "
                                + "this copy was already migrated. Tear down and set up a fresh copy to run again.");
                            break;
                        }
                        var records = KgNpcMigrator.Scan();
                        if (records.Count > 0) File.WriteAllText(BeforePath(), JsonConvert.SerializeObject(records, Formatting.Indented));
                        Row("census", records.Count > 0 ? Pass : Fail, records.Count > 0
                            ? $"{records.Count} MarketPlaceNPC ZDO(s): " + string.Join(", ", records.GroupBy(r => KgNpcMigrator.KgTypeName(r.Int("KGmarketNPC"))).Select(g => $"{g.Key} {g.Count()}"))
                              + $"; written to {BeforePath()}"
                            : "no MarketPlaceNPC ZDO in this world: nothing to migrate");
                        foreach (var r in records.Take(60))
                            Row("npc", Pass, string.Format(CultureInfo.InvariantCulture, "{0} '{1}' profile '{2}' dialogue '{3}' at ({4:0.0}, {5:0.0}, {6:0.0})",
                                KgNpcMigrator.KgTypeName(r.Int("KGmarketNPC")), r.Str("KGnpcNameOverride"), r.Str("KGnpcProfile"), r.Str("KGnpcDialogue"), r.Pos.x, r.Pos.y, r.Pos.z));
                        break;
                    }
                    case "run":
                    {
                        int exceptions = 0;
                        var shown = new List<string>();
                        void Count(string text, string trace, LogType type)
                        {
                            if (type != LogType.Exception) return;
                            System.Threading.Interlocked.Increment(ref exceptions);
                            lock (shown) if (shown.Count < 5) shown.Add(text);
                        }
                        Application.logMessageReceivedThreaded += Count;
                        List<string> lines;
                        try { lines = KgNpcMigrator.RunAll(sender); }
                        finally { Application.logMessageReceivedThreaded -= Count; }
                        // The migrator's "!" lines name content the SOURCE server had and this one lacks: a model from a mod
                        // not installed here, or a dialogue its configs never defined. They are reported as warnings; any
                        // other "!" or ERROR line fails the step.
                        bool IsNote(string l) => l.IndexOf("MODEL MISSING", StringComparison.OrdinalIgnoreCase) >= 0
                            || l.IndexOf("DIALOGUE MISSING", StringComparison.OrdinalIgnoreCase) >= 0;
                        var flagged = lines.Where(l => l.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 || l.TrimStart().StartsWith("!")).ToList();
                        var notes = flagged.Where(IsNote).ToList();
                        var problems = flagged.Where(l => !IsNote(l)).ToList();
                        bool complete = lines.Any(l => l.Contains("FULL MIGRATION COMPLETE"));
                        Row("migrate", complete && exceptions == 0 && problems.Count == 0 ? Pass : Fail,
                            $"fires_migrate_kg all: {lines.Count} line(s), {(complete ? "complete" : "NOT complete")}, {exceptions} exception(s), {problems.Count} error line(s)"
                            + (problems.Count > 0 ? ": " + string.Join(" / ", problems.Take(5)) : "")
                            + $", {notes.Count} warning(s) about content the source server had"
                            + (notes.Count > 0 ? ": " + string.Join(" / ", notes.Select(n => n.Trim()).Take(10)) : "")
                            + (shown.Count > 0 ? "; exceptions: " + string.Join(" / ", shown) : ""));
                        break;
                    }
                    case "after":
                        Compare(Row);
                        break;
                    case "save":
                        ZNet.instance.Save(true);
                        Row("save", Pass, $"ZNet.Save(sync) on the server at {DateTime.Now:HH:mm:ss}");
                        break;
                    default:
                        Row("guard", Fail, $"unknown op '{op}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                Row("error", Fail, $"{op} threw {ex.GetType().Name}: {ex.Message}");
            }
            return sb.ToString();
        }

        private static string BeforePath()
        {
            string dir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Root, "Migration");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"migratetest_before_{WorldSave}.json");
        }

        // Every census record against the world now: one row per kg type, a kg-left row, and one sample per migrated type.
        // Whether a Marketplace source config folder defines a section "[name]" (case-insensitive).
        private static bool SourceDefines(string folder, string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || !Directory.Exists(folder)) return false;
                string want = "[" + name.Trim() + "]";
                foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    foreach (var line in File.ReadLines(f))
                        if (string.Equals(line.Trim(), want, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception) { }
            return false;
        }

        // This plugin's DLL, first 12 hex of its SHA-256, for BEGIN.
        private static string DllHash()
        {
            try
            {
                string path = typeof(RpgMakerMigrateTest).Assembly.Location;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var s = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").Substring(0, 12);
            }
            catch (Exception) { return "unknown"; }
        }

        private static void Compare(Action<string, string, string> row)
        {
            if (!File.Exists(BeforePath())) { row("census", Fail, $"no before-census at {BeforePath()}: run '{Command} run' first"); return; }
            // The imported quests ride the config watcher; read them now so the check judges what the import wrote.
            try
            {
                global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.LoadQuestDatabase();
                global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.LoadQuestProfiles();
                UI.QuestProfileManager.Reload();
            }
            catch (Exception ex) { Debug.LogWarning("[RpgMakerMigrateTest] quest reload: " + ex.Message); }
            var before = JsonConvert.DeserializeObject<List<KgNpcMigrator.Record>>(File.ReadAllText(BeforePath())) ?? new List<KgNpcMigrator.Record>();
            var kgLeft = new List<ZDO>();
            int idx = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(KgPrefab, kgLeft, ref idx)) { }
            var statics = new List<ZDO>();
            idx = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(OurPrefab, statics, ref idx)) { }
            statics = statics.Where(z => z != null && z.IsValid()).ToList();

            var used = new HashSet<ZDOID>();
            var samples = new Dictionary<NpcType, string>();
            foreach (var group in before.GroupBy(r => r.Int("KGmarketNPC")))
            {
                string kgName = KgNpcMigrator.KgTypeName(group.Key);
                NpcType mapped = KgNpcMigrator.TypeMap.TryGetValue(group.Key, out var t) ? t : NpcType.None;
                int matched = 0, resolved = 0, needResolve = 0;
                var misses = new List<string>();
                var warnings = new List<string>();
                foreach (var rec in group)
                {
                    ZDO hit = null;
                    float bestD = MatchMetres;
                    foreach (var z in statics)
                    {
                        if (used.Contains(z.m_uid)) continue;
                        float d = Utils.DistanceXZ(z.GetPosition(), rec.Pos);
                        if (d <= bestD) { bestD = d; hit = z; }
                    }
                    string label = $"'{rec.Str("KGnpcNameOverride")}' at ({rec.Pos.x:0}, {rec.Pos.z:0})";
                    if (hit == null) { misses.Add($"{label}: no StaticNpc within {MatchMetres} m"); continue; }
                    used.Add(hit.m_uid);
                    string name = rec.Str("KGnpcNameOverride");
                    string dialogue = rec.Str("KGnpcDialogue");
                    // The migrator's own rule (KgNpcMigrator.SwapOne): a kg None NPC that has a dialogue becomes a DialogueNpc.
                    NpcType ours = mapped == NpcType.None && !string.IsNullOrEmpty(dialogue) ? NpcType.DialogueNpc : mapped;
                    var why = new List<string>();
                    if (hit.GetInt("npc_type", -1) != (int)ours) why.Add($"type {hit.GetInt("npc_type", -1)} not {(int)ours} ({ours})");
                    if (!string.IsNullOrEmpty(name) && hit.GetString("npc_name_override", "") != name) why.Add($"name '{hit.GetString("npc_name_override", "")}'");
                    if (!string.IsNullOrEmpty(dialogue) && hit.GetString("npc_dialogue_profile", "") != dialogue) why.Add($"dialogue '{hit.GetString("npc_dialogue_profile", "")}'");
                    string profile = KgNpcMigrator.NormalizeKgProfile(rec.Str("KGnpcProfile"));
                    if (KgNpcMigrator.ProfileKeyByType.TryGetValue(ours, out string key) && hit.GetString(key, "") != profile) why.Add($"{key} '{hit.GetString(key, "")}' not '{profile}'");
                    if (why.Count > 0) { misses.Add($"{label}: " + string.Join(", ", why)); continue; }
                    matched++;
                    // The content it names must exist in the configs the migration imported. Content the SOURCE configs never
                    // defined either is a warning (the migrator flags it too), not a migration failure.
                    var lacking = new List<string>();
                    bool sourceGap = true;
                    if (ours == NpcType.Trader && (TraderProfileManager.GetProfile(profile)?.Trades.Count ?? 0) == 0)
                    { lacking.Add($"trader profile '{profile}'"); sourceGap &= !SourceDefines(FiresCore.Compat.MarketplacePaths.TradersFolder, profile); }
                    if (ours == NpcType.QuestNpc && (UI.QuestProfileManager.GetQuestsForProfile(profile)?.Count ?? 0) == 0)
                    { lacking.Add($"quest profile '{profile}'"); sourceGap &= !SourceDefines(FiresCore.Compat.MarketplacePaths.QuestProfilesFolder, profile); }
                    if (!string.IsNullOrEmpty(dialogue) && Dialogues.DialogueCodex.Get(dialogue.Trim().ToLowerInvariant()) == null)
                    { lacking.Add($"dialogue '{dialogue}'"); sourceGap &= !SourceDefines(FiresCore.Compat.MarketplacePaths.DialoguesFolder, dialogue); }
                    bool resolves = ours == NpcType.Trader || ours == NpcType.QuestNpc || !string.IsNullOrEmpty(dialogue);
                    if (resolves)
                    {
                        needResolve++;
                        if (lacking.Count == 0) resolved++;
                        else if (sourceGap) { resolved++; warnings.Add($"{label}: {string.Join(", ", lacking)} is not defined in the source Marketplace configs either"); }
                        else misses.Add($"{label}: {string.Join(", ", lacking)} is in the source configs but does not resolve after the import");
                    }
                    if (!samples.ContainsKey(ours))
                    {
                        var p = hit.GetPosition();
                        samples[ours] = string.Format(CultureInfo.InvariantCulture, "{0} {1:0.00} {2:0.00} {3:0.00} {4}", ours, p.x, p.y, p.z, name);
                    }
                }
                int n = group.Count();
                row(kgName, matched == n && resolved == needResolve ? Pass : Fail,
                    $"{matched} of {n} replaced by a {mapped}{(mapped == NpcType.None ? " (DialogueNpc where it has a dialogue)" : "")} StaticNpc at the same place with its name, type, profile and dialogue; "
                    + $"{resolved} of {needResolve} name content that resolves" + (misses.Count > 0 ? "; misses: " + string.Join(" / ", misses.Take(8)) : "")
                    + (warnings.Count > 0 ? "; warnings: " + string.Join(" / ", warnings.Take(8)) : ""));
            }
            row("kg-left", kgLeft.Count == 0 ? Pass : Fail, $"{kgLeft.Count} MarketPlaceNPC ZDO(s) left in the world");
            row("census", Pass, $"{before.Count} NPC(s) in the before-census, {statics.Count} StaticNpc ZDO(s) now");
            foreach (var s in samples.Values) row("sample", Pass, s);
        }
    }
}
