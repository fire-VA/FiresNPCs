using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using FiresCore.Npc;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.Modules.Migration
{
    // ── `rpgmaker_migrate_proof rawbefore | fields | render [N]`: the field-by-field PROOF that `fires_migrate_kg all` carries
    // every kg Marketplace NPC over to FiresNPCs with all its data and its look, on a world WITHOUT the kg plugin (Fire 10-05:
    // "I have yet to prove we properly transfer all info and appearances etc from marketplace npcs to our own"). It sits beside
    // RpgMakerMigrateTest ([worldgen]) and reuses its world guard and before-census; run order in Tools\GANDHEIM_MIGRATION_PROOF.md.
    //   rawbefore (server, BEFORE `rpgmaker_migrate_test run`): every key on every MarketPlaceNPC ZDO, named where the name is
    //            known (kg keys, ZDOVars). Proves which kg data exists at all, so a key the migrator never reads can't hide.
    //   fields   (server, after the restart + `rpgmaker_migrate_test verify`): one CSV row per NPC per field - kg value, the value
    //            the migrator's rules give, the value on the migrated ZDO now - MATCH / CHANGED / MISSING / EXTRA / UNSUPPORTED;
    //            the Marketplace configs against what FiresNPCs loaded (traders item by item, quests, dialogues); and the
    //            world: kg ZDOs left, ZDOs whose prefab no loaded mod provides, and the loaded plugin list.
    //   render   (client): goes to each migrated NPC (or the first N), reads what its body actually WEARS and LOOKS like
    //            (VisEquipment, model, colours, scale) against its ZDO, and takes a SHOT of it.
    // Files: <config>\FiresConfigs\Migration\migrateproof_*_<world>.(json|csv) on the side that ran the step.
    internal static class RpgMakerMigrateProof
    {
        // "<area>_test": FDT's launch gate (FiresTestTrigger.Enqueue) only runs fires_test_all, <area>_test [args] or its direct
        // list from a -firestest line, so the unattended run needs this name.
        private const string Command = "rpgmaker_migrate_proof_test", Tag = "[RpgMakerMigrateProof]";
        private const string RpcAsk = "Fires_MigrateProof_Ask", RpcReply = "Fires_MigrateProof_Reply";
        private const string Pass = "PASS", Fail = "FAIL", Skip = "SKIP";
        private const string KgPrefab = "MarketPlaceNPC", OurPrefab = "StaticNpc";
        private const float MatchMetres = 0.5f, ReplySeconds = 300f, ArriveGiveUpSeconds = 45f, ArriveSettleSeconds = 4f, NpcReach = 4f, ShotHoldSeconds = 1.5f;
        private const int ReplyCap = 60000;

        // Every key kg's Marketplace is known to write on an NPC ZDO (KgNpcMigrator's list + the ones it does not read).
        private static readonly string[] KgKnownKeys =
        {
            "KGmarketNPC", "KGnpcProfile", "KGnpcDialogue", "KGnpcNameOverride", "KGnpcModelOverride", "KGmarketPinIcon", "KGmarketPinned",
            "KGleftItem", "KGrightItem", "KGhelmetItem", "KGchestItem", "KGlegsItem", "KGcapeItem", "KGhairItem", "KGhairItemColor",
            "KGLeftItemBack", "KGRightItemBack", "KGinteractAnimation", "KGgreetingAnimation", "KGbyeAnimation", "KGgreetingText",
            "KGbyeText", "KGskinColor", "KGbeardItem", "KGbeardColor", "KGinteractSound", "KGperiodicAnimation", "KGperiodicSound",
            "KGmarket PatrolData", "KGnpcScale", "KGtextSize", "KGtextHeight", "KGperiodicAnimationTime", "KGperiodicSoundTime",
            "KGcraftingAnimation", "KGmarket CustomWebhooks", "KGtsLevel",
        };

        private static bool s_running;
        private static string s_reply;
        private static int s_step, s_total, s_passes, s_fails, s_skips;

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class RegisterCommand
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand(Command,
                    "[F5 test] [no relaunch] field-by-field proof of fires_migrate_kg on the disposable world 'GandheimMigrate': "
                    + "rawbefore (before the migration) | fields (after the restart) | render [N] (client: look at each NPC, SHOT).",
                    args => Start(args.Length > 1 ? args.Args[1].ToLowerInvariant() : "", args.Length > 2 ? args.Args[2] : ""),
                    isCheat: false, isNetwork: false, onlyServer: false);
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

        // ── Client ─────────────────────────────────────────────────────────────────────────────────────────────
        private static void Start(string mode, string arg)
        {
            if (s_running) { Say($"{Tag} a run is already going."); return; }
            if (ZNet.instance == null || Player.m_localPlayer == null) { Say($"{Tag} join a world first."); return; }
            if (mode != "" && mode != "rawbefore" && mode != "fields" && mode != "render") { Say($"{Tag} usage: {Command} rawbefore | fields | render [N]"); return; }
            ZNet.instance.StartCoroutine(Run(mode, arg));
        }

        private static IEnumerator Run(string mode, string arg)
        {
            s_running = true;
            s_step = s_passes = s_fails = s_skips = 0;
            float started = Time.realtimeSinceStartup;
            try
            {
                string[] names = mode == "rawbefore" ? new[] { "plugins", "rawbefore" }
                    : mode == "fields" ? new[] { "plugins", "fields", "configs", "world" }
                    : mode == "render" ? new[] { "plugins", "render" }
                    : new[] { "plugins", "rawbefore", "fields", "configs", "world", "render" };
                s_total = names.Length;
                string mod = global::FiresRPGmaker.FiresRPGmaker.PluginName, version = global::FiresRPGmaker.FiresRPGmaker.PluginVersion;
                // "(hash)" is part of FDT's runner BEGIN contract (see RpgMakerMigrateTest): without it the runner relaunches the test.
                Say($"{Tag} BEGIN {mod} {version} ({DllHash()}) on client '{Player.m_localPlayer.GetPlayerName()}' "
                    // ", ~<s> s" completes FDT's BEGIN contract (FiresTests.BeginLine); without it the runner sees no BEGIN at all.
                    + $"{(mode == "" ? "no mode" : mode)}: {s_total} steps, ~{EstimateSeconds(mode, arg)} s");
                if (mode == "")
                {
                    foreach (string n in names) Step(n, Skip, $"on demand: {Command} rawbefore (before the migration) | fields | render [N]");
                    yield break;
                }

                // The client's own plugin list; the server lists its own in the replies below.
                string clientPlugins = PluginList(out string clientKg);
                Step("plugins", clientKg == null ? Pass : Fail, $"client: {clientPlugins}" + (clientKg != null ? $"; kg plugin LOADED: {clientKg}" : "; no kg/Marketplace plugin"));

                if (mode == "render") { yield return Render(arg); yield break; }

                yield return Ask(mode);
                var rows = Rows(s_reply);
                if (s_reply == null) { while (s_step < s_total) Step(names[s_step], Fail, "no reply from the server"); yield break; }
                foreach (string n in names.Skip(1))
                {
                    var row = rows.FirstOrDefault(r => r.Name == n);
                    if (row == null) Step(n, Fail, "the server sent no row for this step: " + string.Join(" / ", rows.Select(r => r.Detail).Take(3)));
                    else Step(n, row.Result, row.Detail);
                }
            }
            finally
            {
                Say($"{Tag} END {(s_fails > 0 ? Fail : Pass)}: {s_passes} pass, {s_fails} fail, {s_skips} skip in {Time.realtimeSinceStartup - started:0} s");
                s_running = false;
            }
        }

        // The runner waits this long for END: render takes ~14 s per NPC (teleport, settle, SHOT).
        private static int EstimateSeconds(string mode, string arg)
        {
            if (mode == "render") return 60 + 14 * (int.TryParse(arg, out int n) && n > 0 ? n : 80);
            return mode == "fields" ? 60 : 20;
        }

        // ── Client: render ─────────────────────────────────────────────────────────────────────────────────────
        // Each migrated NPC's ZDO says what it should wear; its live body says what it does. Read both, SHOT the NPC.
        private static IEnumerator Render(string arg)
        {
            yield return Ask("roster");
            var roster = Rows(s_reply).Where(r => r.Name == "npc").Select(r => r.Detail.Split('|')).Where(p => p.Length >= 14).ToList();
            if (roster.Count == 0) { Step("render", Fail, s_reply == null ? "no reply from the server" : "the server listed no migrated NPC"); yield break; }
            int limit = int.TryParse(arg, out int n) && n > 0 ? Math.Min(n, roster.Count) : roster.Count;

            var inv = CultureInfo.InvariantCulture;
            var csv = new List<string> { "npc,name,x,z,part,expected,actual,status,note" };
            int npcsOk = 0, npcsSeen = 0, rowsOk = 0, rowsBad = 0;
            var player = Player.m_localPlayer;
            for (int i = 0; i < limit && Player.m_localPlayer != null; i++)
            {
                var p = roster[i];
                // npc|name|x|y|z|yaw|helmet|chest|legs|shoulder|left|right|hair|beard|modelIndex|scale|skin|hairColor
                string name = p[1];
                var at = new Vector3(float.Parse(p[2], inv), float.Parse(p[3], inv), float.Parse(p[4], inv));
                float yaw = float.Parse(p[5], inv);
                Vector3 front = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 stand = at + front * 3f;
                player.TeleportTo(stand + Vector3.up * 0.5f, Quaternion.LookRotation(-front), true);
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
                string id = (i + 1).ToString(inv);
                string where = string.Format(inv, "{0:0},{1:0}", at.x, at.z);
                if (npc == null)
                {
                    csv.Add(Csv(id, name, where, "body", "an NPC within 4 m", "none", "MISSING", "no body after arriving"));
                    rowsBad++;
                    continue;
                }
                npcsSeen++;
                var vis = npc.GetComponentInChildren<VisEquipment>(true);
                bool all = true;
                void Part(string part, string expected, string actual, bool ok, string note = "")
                {
                    csv.Add(Csv(id, name, where, part, expected, actual, ok ? "MATCH" : "CHANGED", note));
                    if (ok) rowsOk++; else { rowsBad++; all = false; }
                }
                void Item(string part, string expected, int currentHash)
                {
                    int want = string.IsNullOrEmpty(expected) ? 0 : expected.GetStableHashCode();
                    string actual = currentHash == 0 ? "" : (ObjectDB.instance?.GetItemPrefab(currentHash)?.name ?? ("#" + currentHash));
                    Part(part, expected, actual, want == currentHash);
                }
                if (vis == null) { Part("visequipment", "present", "none", false, "the body has no VisEquipment"); }
                else
                {
                    Item("helmet", p[6], vis.m_currentHelmetItemHash);
                    Item("chest", p[7], vis.m_currentChestItemHash);
                    Item("legs", p[8], vis.m_currentLegItemHash);
                    Item("shoulder", p[9], vis.m_currentShoulderItemHash);
                    Item("lefthand", p[10], vis.m_currentLeftItemHash);
                    Item("righthand", p[11], vis.m_currentRightItemHash);
                    bool hairHidden = vis.m_helmetHideHair != ItemDrop.ItemData.HelmetHairType.Default && vis.m_currentHelmetItemHash != 0;
                    bool beardHidden = vis.m_helmetHideBeard != ItemDrop.ItemData.HelmetHairType.Default && vis.m_currentHelmetItemHash != 0;
                    int hairWant = string.IsNullOrEmpty(p[12]) ? 0 : p[12].GetStableHashCode();
                    int beardWant = string.IsNullOrEmpty(p[13]) ? 0 : p[13].GetStableHashCode();
                    Part("hair", p[12], vis.m_currentHairItemHash == 0 ? "" : "#" + vis.m_currentHairItemHash,
                        hairWant == vis.m_currentHairItemHash || hairHidden, hairHidden ? "the helmet hides or swaps the hair (vanilla rule)" : "");
                    Part("beard", p[13], vis.m_currentBeardItemHash == 0 ? "" : "#" + vis.m_currentBeardItemHash,
                        beardWant == vis.m_currentBeardItemHash || beardHidden, beardHidden ? "the helmet hides the beard (vanilla rule)" : "");
                    if (p.Length > 14) Part("model", p[14], vis.m_currentModelIndex.ToString(inv), p[14] == vis.m_currentModelIndex.ToString(inv));
                    if (p.Length > 16 && p[16] != "") Part("skincolor", p[16], V(vis.m_skinColor), Near(p[16], vis.m_skinColor));
                    if (p.Length > 17 && p[17] != "") Part("haircolor", p[17], V(vis.m_hairColor), Near(p[17], vis.m_hairColor));
                }
                if (p.Length > 15)
                {
                    float scale = float.Parse(p[15], inv);
                    float actual = npc.transform.localScale.y;
                    Part("scale", scale.ToString("0.###", inv), actual.ToString("0.###", inv), Mathf.Abs(actual - scale) < 0.02f);
                }
                if (all) npcsOk++;

                Vector3 aim = npc.transform.position + Vector3.up * 1.2f;
                Say($"{Tag} SHOT " + "{" + string.Format(inv, "{0:0.0},{1:0.0},{2:0.0},{3:0.0}", aim.x, aim.y, aim.z, 3.5f) + "} " + $"migrated NPC {id}/{limit} '{name}'");
                yield return new WaitForSecondsRealtime(ShotHoldSeconds);
            }

            string file = WriteFile($"migrateproof_render_{World()}.csv", csv);
            Step("render", npcsSeen == limit && rowsBad == 0 ? Pass : Fail,
                $"{npcsSeen} of {limit} NPC bodies found, {npcsOk} wear and look exactly as their ZDO says; {rowsOk} part(s) MATCH, {rowsBad} differ; written to {file}");
        }

        private static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2:0.000}", v.x, v.y, v.z);

        private static bool Near(string expected, Vector3 actual)
        {
            var parts = expected.Split(',');
            if (parts.Length != 3) return false;
            var inv = CultureInfo.InvariantCulture;
            var e = new Vector3(float.Parse(parts[0], inv), float.Parse(parts[1], inv), float.Parse(parts[2], inv));
            return (e - actual).sqrMagnitude < 0.0004f;
        }

        // ── Plumbing (same shape as RpgMakerMigrateTest) ───────────────────────────────────────────────────────
        private static IEnumerator Ask(string op)
        {
            s_reply = null;
            if (ZNet.instance.IsServer()) { s_reply = Answer(op); yield break; }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAsk, op);
            float until = Time.realtimeSinceStartup + ReplySeconds;
            while (s_reply == null && Time.realtimeSinceStartup < until) yield return null;
        }

        private sealed class Row { public string Name, Result, Detail; }

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

        private static void Step(string name, string result, string detail)
        {
            s_step++;
            if (result == Pass) s_passes++; else if (result == Fail) s_fails++; else s_skips++;
            Say($"{Tag} {s_step}/{s_total} {name} {result} - {detail}");
        }

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

        // Every loaded plugin "guid version" (logged in full), and the first that looks like kg's Marketplace.
        private static string PluginList(out string kg)
        {
            kg = null;
            var all = new List<string>();
            foreach (var kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
            {
                string guid = kv.Key ?? "", name = kv.Value?.Metadata?.Name ?? "";
                all.Add(guid + " " + kv.Value?.Metadata?.Version);
                if (guid.StartsWith("com.Fire.", StringComparison.OrdinalIgnoreCase)) continue;
                if (kg == null && (guid.IndexOf("Marketplace", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Marketplace", StringComparison.OrdinalIgnoreCase) >= 0
                    || guid.StartsWith("kg.", StringComparison.OrdinalIgnoreCase)))
                    kg = $"{guid} '{name}'";
            }
            all.Sort(StringComparer.OrdinalIgnoreCase);
            Debug.Log($"{Tag} {(ZNet.instance != null && ZNet.instance.IsServer() ? "server" : "client")} plugins ({all.Count}): " + string.Join(", ", all));
            return $"{all.Count} plugins loaded (full list in the log)";
        }

        // This plugin's DLL, first 12 hex of its SHA-256, for BEGIN.
        private static string DllHash()
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var s = File.OpenRead(typeof(RpgMakerMigrateProof).Assembly.Location))
                    return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").Substring(0, 12);
            }
            catch (Exception) { return "unknown"; }
        }

        private static string World() => ZNet.World != null ? ZNet.World.m_worldName : (ZNet.instance?.GetWorldName() ?? "world");

        private static string Dir()
        {
            string dir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Root, "Migration");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string WriteFile(string name, IEnumerable<string> lines)
        {
            string path = Path.Combine(Dir(), name);
            File.WriteAllLines(path, lines);
            return path;
        }

        private static string Csv(params string[] cells) =>
            string.Join(",", cells.Select(c => "\"" + (c ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\""));

        // ── Server ─────────────────────────────────────────────────────────────────────────────────────────────
        private static void OnAsk(long sender, string op)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            string text = KgNpcMigrator.IsSenderAdmin(sender) ? Answer(op) : "ROW guard|FAIL|denied: admin only";
            if (text.Length > ReplyCap) text = text.Substring(0, ReplyCap) + "\n(truncated; full detail in the server's files)";
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcReply, text);
        }

        private static string Answer(string op)
        {
            var sb = new StringBuilder();
            void Row(string name, string result, string detail)
            {
                string line = $"ROW {name}|{result}|{detail.Replace('\n', ' ')}";
                sb.Append(line).Append('\n');
                if (name != "npc") Debug.Log(Tag + " server " + line);
            }
            try
            {
                string save = World();
                if (save != RpgMakerMigrateTest.WorldSave) { Row("guard", Fail, $"the server's world save is '{save}', not '{RpgMakerMigrateTest.WorldSave}'"); return sb.ToString(); }
                PluginList(out string kg);
                switch (op)
                {
                    case "rawbefore": RawBefore(Row, kg); break;
                    case "fields": Fields(Row); Configs(Row); WorldCheck(Row, kg); break;
                    case "roster": Roster(Row); break;
                    default: Row("guard", Fail, $"unknown op '{op}'"); break;
                }
            }
            catch (Exception ex) { Row("error", Fail, $"{op} threw {ex.GetType().Name}: {ex.Message}"); }
            return sb.ToString();
        }

        private static List<ZDO> AllWithPrefab(string prefab)
        {
            var list = new List<ZDO>();
            int idx = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, list, ref idx)) { }
            return list.Where(z => z != null && z.IsValid()).ToList();
        }

        // Hash -> readable name for every key we can name: kg's keys and vanilla's ZDOVars.
        private static Dictionary<int, string> KeyNames()
        {
            var names = new Dictionary<int, string>();
            foreach (string k in KgKnownKeys) names[k.GetStableHashCode()] = k;
            foreach (var f in typeof(ZDOVars).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(int) && f.GetValue(null) is int h && !names.ContainsKey(h)) names[h] = "ZDOVars." + f.Name;
            return names;
        }

        private static string RawPath() => Path.Combine(Dir(), $"migrateproof_rawbefore_{World()}.json");

        // ── rawbefore: every key on every kg NPC ZDO, before anything touches them ────────────────────────────────────
        private static void RawBefore(Action<string, string, string> row, string kg)
        {
            if (kg != null || (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(KgPrefab) != null))
            { row("rawbefore", Fail, $"kg is loaded on the server ({kg ?? "MarketPlaceNPC prefab registered"}): the proof needs a server without it"); return; }
            if (File.Exists(RawPath())) { row("rawbefore", Fail, $"{RawPath()} already exists: this copy was captured before; tear down and set up a fresh copy"); return; }
            var names = KeyNames();
            var npcs = AllWithPrefab(KgPrefab);
            var dump = new List<Dictionary<string, object>>();
            var keyCounts = new Dictionary<string, int>();
            foreach (var z in npcs)
            {
                ZDOExtraData.GetData(z.m_uid, out var floats, out var vecs, out var quats, out var ints, out var longs, out var strings, out var bytes, out _);
                var keys = new Dictionary<string, object>();
                void Add<T>(List<KeyValuePair<int, T>> list, string kind, Func<T, object> show)
                {
                    if (list == null) return;
                    foreach (var kv in list)
                    {
                        string key = names.TryGetValue(kv.Key, out string nm) ? nm : "#" + kv.Key;
                        keys[kind + ":" + key] = show(kv.Value);
                        keyCounts.TryGetValue(kind + ":" + key, out int c);
                        keyCounts[kind + ":" + key] = c + 1;
                    }
                }
                Add(strings, "string", v => v);
                Add(floats, "float", v => v);
                Add(ints, "int", v => v);
                Add(longs, "long", v => v);
                Add(vecs, "vec3", v => V(v));
                Add(quats, "quat", v => string.Format(CultureInfo.InvariantCulture, "{0:0.0000},{1:0.0000},{2:0.0000},{3:0.0000}", v.x, v.y, v.z, v.w));
                Add(bytes, "bytes", v => v == null ? 0 : v.Length);
                var pos = z.GetPosition();
                dump.Add(new Dictionary<string, object>
                {
                    { "zdo", z.m_uid.ToString() },
                    { "pos", V(pos) },
                    { "yaw", z.GetRotation().eulerAngles.y },
                    { "keys", keys },
                });
            }
            File.WriteAllText(RawPath(), JsonConvert.SerializeObject(dump, Formatting.Indented));
            var read = new HashSet<string>(KgKnownKeys.Take(KgKnownKeys.Length - 2));   // CustomWebhooks and tsLevel are not read
            var notRead = keyCounts.Keys.Where(k => k.StartsWith("string:KG") || k.StartsWith("float:KG") || k.StartsWith("int:KG") || k.StartsWith("long:KG") || k.StartsWith("#") || k.Contains(":#"))
                .Where(k => !read.Contains(k.Substring(k.IndexOf(':') + 1))).ToList();
            row("rawbefore", npcs.Count > 0 ? Pass : Fail,
                $"{npcs.Count} MarketPlaceNPC ZDO(s), {keyCounts.Count} distinct keys; keys the migrator does not read: "
                + (notRead.Count == 0 ? "none" : string.Join(", ", notRead.Select(k => $"{k} ({keyCounts[k]})")))
                + $"; written to {RawPath()}");
        }

        // ── fields: one row per NPC per field ─────────────────────────────────────────────────────────────────────
        private static void Fields(Action<string, string, string> row)
        {
            string censusPath = Path.Combine(Dir(), $"migratetest_before_{RpgMakerMigrateTest.WorldSave}.json");
            if (!File.Exists(censusPath)) { row("fields", Fail, $"no before-census at {censusPath}: run 'rpgmaker_migrate_test run' first"); return; }
            var before = JsonConvert.DeserializeObject<List<KgNpcMigrator.Record>>(File.ReadAllText(censusPath)) ?? new List<KgNpcMigrator.Record>();
            var raw = File.Exists(RawPath()) ? JArray.Parse(File.ReadAllText(RawPath())) : null;
            var statics = AllWithPrefab(OurPrefab);
            var used = new HashSet<ZDOID>();
            var csv = new List<string> { "npc,name,x,z,field,kg_key,kg_value,fires_key,expected,actual,status,note" };
            var counts = new Dictionary<string, int> { { "MATCH", 0 }, { "CHANGED", 0 }, { "MISSING", 0 }, { "EXTRA", 0 }, { "UNSUPPORTED", 0 } };
            var byField = new Dictionary<string, Dictionary<string, int>>();
            var inv = CultureInfo.InvariantCulture;
            int unmatched = 0, i = 0;

            foreach (var rec in before)
            {
                i++;
                string id = i.ToString(inv), name = rec.Str("KGnpcNameOverride"), where = string.Format(inv, "{0:0},{1:0}", rec.Pos.x, rec.Pos.z);
                void Out(string field, string kgKey, string kgValue, string firesKey, string expected, string actual, string status, string note = "")
                {
                    csv.Add(Csv(id, name, where, field, kgKey, kgValue, firesKey, expected, actual, status, note));
                    counts[status]++;
                    if (!byField.TryGetValue(field, out var f)) byField[field] = f = new Dictionary<string, int>();
                    f.TryGetValue(status, out int c);
                    f[status] = c + 1;
                }

                ZDO hit = null;
                float bestD = MatchMetres;
                foreach (var z in statics)
                {
                    if (used.Contains(z.m_uid)) continue;
                    float d = Utils.DistanceXZ(z.GetPosition(), rec.Pos);
                    if (d <= bestD) { bestD = d; hit = z; }
                }
                if (hit == null) { unmatched++; Out("npc", "", "", "", "a StaticNpc within 0.5 m", "none", "MISSING", "the NPC itself was not carried over"); continue; }
                used.Add(hit.m_uid);

                // A plain compare: what kg said -> what the migrator's rule gives -> what the ZDO holds now.
                void Cmp(string field, string kgKey, string kgValue, string firesKey, string expected, string actual, string note = "")
                {
                    expected = expected ?? ""; actual = actual ?? "";
                    string status = expected == actual ? "MATCH" : (actual == "" ? "MISSING" : "CHANGED");
                    Out(field, kgKey, kgValue, firesKey, expected, actual, status, note);
                }
                // kg's random lists ("A B C", space-separated): the migrator keeps ONE, so any listed option is a MATCH.
                void Pick(string field, string kgKey, string firesKey, string prefix = null, Func<ZDO, string> read = null)
                {
                    string kgValue = rec.Str(kgKey);
                    string actual = read != null ? read(hit) : hit.GetString(firesKey, "");
                    var options = Options(kgValue).Select(o => prefix != null && !o.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? prefix + o : o).ToList();
                    if (options.Count == 0) { Out(field, kgKey, "", firesKey, "", actual, actual == "" ? "MATCH" : "EXTRA", actual == "" ? "" : "set although kg had none"); return; }
                    bool ok = options.Any(o => string.Equals(o, actual, StringComparison.OrdinalIgnoreCase));
                    Out(field, kgKey, kgValue, firesKey, string.Join(" | ", options), actual, ok ? "MATCH" : (actual == "" ? "MISSING" : "CHANGED"),
                        options.Count > 1 ? $"kg random list: the migrator keeps 1 of {options.Count}" : "");
                }

                // identity + placement
                var pos = hit.GetPosition();
                float dist = Vector3.Distance(pos, rec.Pos);
                Out("position", "(zdo position)", V(rec.Pos), "(zdo position)", V(rec.Pos), V(pos), dist <= MatchMetres ? "MATCH" : "CHANGED", string.Format(inv, "{0:0.00} m apart", dist));
                float yawKg = rec.Rot.eulerAngles.y, yawNow = hit.GetRotation().eulerAngles.y;
                float dYaw = Mathf.Abs(Mathf.DeltaAngle(yawKg, yawNow));
                Out("rotation", "(zdo rotation)", yawKg.ToString("0.0", inv), "(zdo rotation)", yawKg.ToString("0.0", inv), yawNow.ToString("0.0", inv), dYaw <= 1f ? "MATCH" : "CHANGED", string.Format(inv, "yaw {0:0.0} deg apart", dYaw));

                int kgType = rec.Int("KGmarketNPC");
                string dialogue = rec.Str("KGnpcDialogue");
                NpcType ours = KgNpcMigrator.TypeMap.TryGetValue(kgType, out var mapped) ? mapped : NpcType.None;
                if (ours == NpcType.None && !string.IsNullOrEmpty(dialogue)) ours = NpcType.DialogueNpc;
                Cmp("type", "KGmarketNPC", kgType + " " + KgNpcMigrator.KgTypeName(kgType), "npc_type", ((int)ours).ToString(inv) + " " + ours,
                    hit.GetInt("npc_type", -1).ToString(inv) + " " + (NpcType)hit.GetInt("npc_type", -1));
                Cmp("name", "KGnpcNameOverride", name, "npc_name_override", name, hit.GetString("npc_name_override", ""));
                Cmp("dialogue", "KGnpcDialogue", dialogue, "npc_dialogue_profile", dialogue, hit.GetString("npc_dialogue_profile", ""));
                string profile = KgNpcMigrator.NormalizeKgProfile(rec.Str("KGnpcProfile"));
                if (KgNpcMigrator.ProfileKeyByType.TryGetValue(ours, out string profileKey))
                    Cmp("profile", "KGnpcProfile", rec.Str("KGnpcProfile"), profileKey, profile, hit.GetString(profileKey, ""), "kg rule: '=Flag' suffix dropped, lowercase, empty -> default");
                else
                    Out("profile", "KGnpcProfile", rec.Str("KGnpcProfile"), "(none)", "", "", "UNSUPPORTED", $"a {ours} NPC has no profile slot in FiresNPCs; the raw value rides along as KGnpcProfile");
                Cmp("map_pinned", "KGmarketPinned", rec.Bool("KGmarketPinned").ToString(), "npc_map_pinned", rec.Bool("KGmarketPinned").ToString(), hit.GetBool("npc_map_pinned", false).ToString());
                Cmp("map_pin_icon", "KGmarketPinIcon", rec.Str("KGmarketPinIcon"), "npc_map_pin_icon", rec.Str("KGmarketPinIcon"), hit.GetString("npc_map_pin_icon", ""));

                // model, scale
                string model = rec.Str("KGnpcModelOverride");
                string modelBase = System.Text.RegularExpressions.Regex.Replace(model ?? "", "@humanoid", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                int modelIndex = hit.GetInt(ZDOVars.s_modelIndex, 0);
                if (string.Equals(modelBase, "Player_Female", StringComparison.OrdinalIgnoreCase))
                    Cmp("model", "KGnpcModelOverride", model, "s_modelIndex + npc_isfemale", "1 female", modelIndex + (hit.GetBool("npc_isfemale", false) ? " female" : " male"), "kg player-female rig -> the native body, female");
                else if (string.IsNullOrEmpty(modelBase) || string.Equals(modelBase, "Player", StringComparison.OrdinalIgnoreCase))
                    Cmp("model", "KGnpcModelOverride", model, "s_modelIndex / npc_model_override", "0 (native body)", modelIndex + (hit.GetString("npc_model_override", "") != "" ? " override " + hit.GetString("npc_model_override", "") : " (native body)").Replace("0 (native body)", "0 (native body)"));
                else
                {
                    bool present = ZNetScene.instance != null && ZNetScene.instance.GetPrefab(modelBase) != null;
                    Cmp("model", "KGnpcModelOverride", model, "npc_model_override", model, hit.GetString("npc_model_override", ""), present ? "" : $"prefab '{modelBase}' is not loaded here (its mod is missing): the override applies once it is");
                }
                float scale = rec.Flt("KGnpcScale", 1f);
                float scaleNow = hit.GetFloat("npc_scale", 1f);
                Out("scale", "KGnpcScale", scale.ToString("0.###", inv), "npc_scale", scale.ToString("0.###", inv), scaleNow.ToString("0.###", inv), Mathf.Abs(scale - scaleNow) < 0.001f ? "MATCH" : "CHANGED");

                // equipment + hair/beard (kg random lists)
                Pick("helmet", "KGhelmetItem", "npc_equip_helmet");
                Pick("chest", "KGchestItem", "npc_equip_chest");
                Pick("legs", "KGlegsItem", "npc_equip_legs");
                Pick("cape", "KGcapeItem", "npc_equip_shoulder");
                Pick("lefthand", "KGleftItem", "npc_equip_lefthand");
                Pick("righthand", "KGrightItem", "npc_equip_righthand");
                Pick("leftback", "KGLeftItemBack", "npc_equip_leftback");
                Pick("rightback", "KGRightItemBack", "npc_equip_rightback");
                Pick("hair", "KGhairItem", "npc_hair", "Hair");
                Pick("beard", "KGbeardItem", "npc_beard", "Beard");
                Pick("haircolor", "KGhairItemColor", "s_hairColor", null, z => ColorText(z.GetVec3(ZDOVars.s_hairColor, Vector3.zero), rec.Str("KGhairItemColor")));
                Pick("skincolor", "KGskinColor", "s_skinColor", null, z => ColorText(z.GetVec3(ZDOVars.s_skinColor, Vector3.zero), rec.Str("KGskinColor")));

                // the rest of kg's fashion record lives in npc_fashion_json
                JObject fashion = null;
                try { string j = hit.GetString("npc_fashion_json", ""); if (j != "") fashion = JObject.Parse(j); } catch (Exception) { }
                string F(string prop) => fashion?[prop]?.ToString() ?? "";
                void Fashion(string field, string kgKey, string prop, bool pick, string kgDefault = null)
                {
                    string kgValue = rec.S.ContainsKey(kgKey) ? rec.Str(kgKey)
                        : rec.F.ContainsKey(kgKey) ? rec.Flt(kgKey, 0f).ToString(inv)
                        : rec.I.ContainsKey(kgKey) ? rec.Int(kgKey).ToString(inv) : (kgDefault ?? "");
                    if (fashion == null) { Out(field, kgKey, kgValue, "npc_fashion_json." + prop, kgValue, "", "MISSING", "npc_fashion_json absent or unreadable"); return; }
                    var options = pick ? Options(kgValue) : new List<string> { kgValue };
                    string actual = F(prop);
                    bool ok = options.Count == 0 ? actual == "" : options.Any(o => string.Equals(o, actual, StringComparison.OrdinalIgnoreCase));
                    Out(field, kgKey, kgValue, "npc_fashion_json." + prop, string.Join(" | ", options), actual, ok ? "MATCH" : (actual == "" ? "MISSING" : "CHANGED"),
                        pick && options.Count > 1 ? $"kg random list: the migrator keeps 1 of {options.Count}" : "");
                }
                Fashion("beardcolor", "KGbeardColor", "BeardColor", false);
                Fashion("anim_interact", "KGinteractAnimation", "InteractAnimation", true);
                Fashion("anim_greet", "KGgreetingAnimation", "GreetAnimation", true);
                Fashion("anim_bye", "KGbyeAnimation", "ByeAnimation", true);
                Fashion("text_greet", "KGgreetingText", "GreetText", false);
                Fashion("text_bye", "KGbyeText", "ByeText", false);
                Fashion("sound_interact", "KGinteractSound", "InteractAudioClip", false);
                Fashion("anim_crafting", "KGcraftingAnimation", "CraftingAnimation", false, "0");
                Fashion("nametag_size", "KGtextSize", "TextSize", false, "3");
                Fashion("nametag_height", "KGtextHeight", "TextHeight", false, "0");
                Fashion("anim_periodic", "KGperiodicAnimation", "PeriodicAnimation", true);
                Fashion("anim_periodic_time", "KGperiodicAnimationTime", "PeriodicAnimationTime", false, "0");
                Fashion("sound_periodic", "KGperiodicSound", "PeriodicSound", false);
                Fashion("sound_periodic_time", "KGperiodicSoundTime", "PeriodicSoundTime", false, "0");

                // patrol
                string patrol = rec.Str("KGmarket PatrolData");
                int points = PatrolPoints(patrol);
                string route = hit.GetString("npc_patrol_route", "");
                int routePoints = route == "" ? 0 : (FiresCore.Npc.Patrol.PatrolRouteManager.GetRoute(route)?.Points.Count ?? -1);
                if (points < 2) Out("patrol", "KGmarket PatrolData", patrol, "npc_patrol_route", "", route, route == "" ? "MATCH" : "EXTRA", route == "" ? "" : "a route although kg had none");
                else Out("patrol", "KGmarket PatrolData", patrol, "npc_patrol_route", $"a route of {points} point(s)", route == "" ? "" : $"'{route}' with {routePoints} point(s)",
                    route == "" ? "MISSING" : (routePoints == points ? "MATCH" : "CHANGED"));

                // every kg key carried verbatim on the new ZDO (the migrator's passthrough)
                int carried = 0, total = 0;
                var lost = new List<string>();
                foreach (var kv in rec.S) { total++; if (hit.GetString(kv.Key, null) == kv.Value) carried++; else lost.Add(kv.Key); }
                foreach (var kv in rec.F) { total++; if (Mathf.Abs(hit.GetFloat(kv.Key, float.NaN) - kv.Value) < 1e-4f) carried++; else lost.Add(kv.Key); }
                foreach (var kv in rec.I) { total++; if (hit.GetInt(kv.Key, int.MinValue) == kv.Value) carried++; else lost.Add(kv.Key); }
                foreach (var kv in rec.B) { total++; if (hit.GetBool(kv.Key, !kv.Value) == kv.Value) carried++; else lost.Add(kv.Key); }
                Out("passthrough", "(every KG* key read)", total + " key(s)", "(same keys)", total + " key(s)", carried + " key(s)", carried == total ? "MATCH" : "MISSING",
                    lost.Count > 0 ? "not carried: " + string.Join(" ", lost) : "raw kg data kept for audits");

                // kg keys this NPC had that the migrator never reads (from rawbefore): reported, not judged
                if (raw != null)
                {
                    var src = raw.FirstOrDefault(o => (o["pos"]?.ToString() ?? "") == V(rec.Pos));
                    var keys = src?["keys"] as JObject;
                    if (keys != null)
                        foreach (var p in keys.Properties())
                        {
                            string key = p.Name.Substring(p.Name.IndexOf(':') + 1);
                            if (rec.S.ContainsKey(key) || rec.F.ContainsKey(key) || rec.I.ContainsKey(key) || rec.B.ContainsKey(key)) continue;
                            // A key the migrator reads (and judged above) can be missing from the census record only because
                            // its value was the default (0 / false / empty): not data the migration dropped.
                            if (Array.IndexOf(KgKnownKeys, key) >= 0 && key != "KGmarket CustomWebhooks" && key != "KGtsLevel") continue;
                            if (key.StartsWith("ZDOVars.")) continue;   // vanilla data on the ZDO (owner, creator, etc.)
                            Out("unread_key", key, p.Value.ToString(), "(none)", "", "", "UNSUPPORTED", "on the kg ZDO but the migrator does not read it");
                        }
                }

                // what the migration adds on top (its defaults for every migrated body)
                foreach (var extra in new[] { "npc_initialized", "npc_static_placement", "npc_infinite_health", "npc_no_face_player", "npc_exact_placement" })
                    Out("added", "", "", extra, "", hit.GetBool(extra, false).ToString(), "EXTRA", "migration default for every migrated body");
            }

            string file = WriteFile($"migrateproof_fields_{World()}.csv", csv);
            var perField = string.Join("; ", byField.OrderBy(k => k.Key).Select(k => k.Key + " " + string.Join("/", k.Value.Select(s => s.Key + " " + s.Value))));
            bool ok = unmatched == 0 && counts["CHANGED"] == 0 && counts["MISSING"] == 0;
            row("fields", ok ? Pass : Fail,
                $"{before.Count} kg NPC(s), {before.Count - unmatched} found migrated: MATCH {counts["MATCH"]}, CHANGED {counts["CHANGED"]}, MISSING {counts["MISSING"]}, "
                + $"EXTRA {counts["EXTRA"]}, UNSUPPORTED {counts["UNSUPPORTED"]}; per field: {perField}; written to {file}");
        }

        private static List<string> Options(string value) =>
            string.IsNullOrEmpty(value) ? new List<string>() : value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        private static int PatrolPoints(string data)
        {
            if (string.IsNullOrEmpty(data)) return 0;
            var coords = data.Split('@')[0].Replace(" ", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return coords.Length / 2;
        }

        // A ZDO colour back as the kg option it matches (so a MATCH compares like with like), else as numbers.
        private static string ColorText(Vector3 v, string kgValue)
        {
            if (v == Vector3.zero) return "";
            foreach (string o in Options(kgValue))
                if (ColorUtility.TryParseHtmlString(o, out var c) && (new Vector3(c.r, c.g, c.b) - v).sqrMagnitude < 0.0004f) return o;
            return V(v);
        }

        // ── configs: the Marketplace source files against what FiresNPCs loaded ─────────────────────────────────────
        private static void Configs(Action<string, string, string> row)
        {
            try
            {
                global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.LoadQuestDatabase();
                global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.LoadQuestProfiles();
                UI.QuestProfileManager.Reload();
            }
            catch (Exception ex) { Debug.LogWarning(Tag + " quest reload: " + ex.Message); }

            var csv = new List<string> { "kind,section,entry,source,loaded,status,note" };
            int ok = 0, bad = 0;
            void Out(string kind, string section, string entry, string source, string loaded, bool match, string note = "")
            {
                csv.Add(Csv(kind, section, entry, source, loaded, match ? "MATCH" : (loaded == "" ? "MISSING" : "CHANGED"), note));
                if (match) ok++; else bad++;
            }

            // Traders: every source line, in order, against the loaded profile's trades. kg writes two forms:
            // "cost, n, reward, m" and, for several costs or rewards, "c1, n1, c2, n2 = r1, m1[, r2, m2]".
            foreach (var (section, lines) in Sections(FiresCore.Compat.MarketplacePaths.TradersFolder))
            {
                var trades = lines.Where(l => l.Contains(",")).ToList();
                var profile = TraderProfileManager.GetProfile(section.ToLowerInvariant()) ?? TraderProfileManager.GetProfile(section);
                if (profile == null) { Out("trader", section, "(profile)", trades.Count + " trade line(s)", "", false, "no FiresNPCs trader profile of this name"); continue; }
                for (int t = 0; t < trades.Count; t++)
                {
                    string source = trades[t];
                    ParseTrade(source, out var costs, out var rewards);
                    var entry = t < profile.Trades.Count ? profile.Trades[t] : null;
                    string Side(IEnumerable<TradeItemStack> s) => string.Join(" + ", s.Select(i => i.PrefabName + " x" + i.Amount));
                    string loaded = entry == null ? "" : Side(entry.CostItems) + " -> " + Side(entry.RewardItems);
                    string expected = string.Join(" + ", costs.Select(c => c.Item1 + " x" + c.Item2)) + " -> " + string.Join(" + ", rewards.Select(r => r.Item1 + " x" + r.Item2));
                    bool match = entry != null && costs.Count > 0 && rewards.Count > 0 && string.Equals(expected, loaded, StringComparison.OrdinalIgnoreCase);
                    Out("trader", section, (t + 1).ToString(), source, loaded, match, costs.Count == 0 || rewards.Count == 0 ? "the source line is incomplete" : "");
                }
                if (profile.Trades.Count > trades.Count) Out("trader", section, "(extra)", trades.Count + " line(s)", profile.Trades.Count + " trade(s)", false, "FiresNPCs has more trades than the source");
            }

            // Quests and dialogues: every source section resolves after the import.
            var qm = global::FiresRPGmaker.Modules.Quest.QuestManager.Instance;
            foreach (var (section, _) in Sections(FiresCore.Compat.MarketplacePaths.QuestsFolder))
            {
                bool found = qm != null && (qm.GetQuestDefinition(section) != null || qm.GetQuestDefinition(section.ToLowerInvariant()) != null);
                Out("quest", section, "", "defined", found ? "loaded" : "", found);
            }
            foreach (var (section, _) in Sections(FiresCore.Compat.MarketplacePaths.DialoguesFolder))
            {
                bool found = Dialogues.DialogueCodex.Get(section.Trim().ToLowerInvariant()) != null || Dialogues.DialogueCodex.Get(section.Trim()) != null;
                Out("dialogue", section, "", "defined", found ? "loaded" : "", found);
            }

            string file = WriteFile($"migrateproof_configs_{World()}.csv", csv);
            row("configs", bad == 0 ? Pass : Fail, $"{ok} config entr(ies) MATCH, {bad} differ (traders line by line, quests, dialogues); written to {file}");
        }

        // One kg trader line as (prefab, amount) costs and rewards. "a, n, b, m" = one cost, one reward;
        // "a, n, b, m = c, k" = every pair left of '=' is a cost, every pair right of it a reward.
        private static void ParseTrade(string line, out List<(string, int)> costs, out List<(string, int)> rewards)
        {
            List<(string, int)> Pairs(string text)
            {
                var parts = text.Split(',').Select(s => s.Trim()).Where(s => s != "").ToArray();
                var list = new List<(string, int)>();
                for (int i = 0; i + 1 < parts.Length; i += 2)
                    list.Add((parts[i], int.TryParse(parts[i + 1], out int n) ? n : 0));
                return list;
            }
            int eq = line.IndexOf('=');
            if (eq >= 0) { costs = Pairs(line.Substring(0, eq)); rewards = Pairs(line.Substring(eq + 1)); return; }
            var all = Pairs(line);
            costs = all.Take(1).ToList();
            rewards = all.Skip(1).Take(1).ToList();
        }

        // "[Section]" blocks of every file in a Marketplace config folder: (name, non-empty non-comment lines).
        private static IEnumerable<(string, List<string>)> Sections(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) yield break;
            foreach (var f in Directory.GetFiles(folder, "*.cfg", SearchOption.AllDirectories))
            {
                string name = null;
                var lines = new List<string>();
                foreach (var raw in File.ReadAllLines(f))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        if (name != null) yield return (name, lines);
                        name = line.Substring(1, line.Length - 2).Trim();
                        lines = new List<string>();
                    }
                    else if (name != null && line != "" && !line.StartsWith("#") && !line.StartsWith("//")) lines.Add(line);
                }
                if (name != null) yield return (name, lines);
            }
        }

        // ── world: no kg left behind, nothing orphaned, kg absent ─────────────────────────────────────────────────
        private static void WorldCheck(Action<string, string, string> row, string kg)
        {
            int kgLeft = AllWithPrefab(KgPrefab).Count;
            var orphans = new Dictionary<int, int>();
            int withKgKeys = 0, total = 0;
            int kgProfile = "KGnpcProfile".GetStableHashCode();
            foreach (var z in ZDOMan.instance.m_objectsByID.Values)
            {
                if (z == null || !z.IsValid()) continue;
                total++;
                int prefab = z.GetPrefab();
                if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefab) == null)
                {
                    orphans.TryGetValue(prefab, out int c);
                    orphans[prefab] = c + 1;
                    if (z.GetString(kgProfile, "") != "") withKgKeys++;
                }
            }
            int orphanCount = orphans.Values.Sum();
            row("world", kg == null && kgLeft == 0 && withKgKeys == 0 ? Pass : Fail,
                $"server kg plugin: {kg ?? "none"}; {kgLeft} MarketPlaceNPC ZDO(s) left; {orphanCount} of {total} ZDO(s) have a prefab no loaded mod provides "
                + $"({orphans.Count} prefab hash(es){(orphans.Count > 0 ? ": " + string.Join(", ", orphans.OrderByDescending(o => o.Value).Take(10).Select(o => o.Key + " x" + o.Value)) : "")}), "
                + $"{withKgKeys} of them carry kg NPC data");
        }

        // ── roster: what the client's render step needs, per migrated NPC ─────────────────────────────────────────
        private static void Roster(Action<string, string, string> row)
        {
            var inv = CultureInfo.InvariantCulture;
            foreach (var z in AllWithPrefab(OurPrefab).Where(z => z.GetString("KGnpcProfile", null) != null || z.GetInt("KGmarketNPC", -1) >= 0))
            {
                var p = z.GetPosition();
                string Col(int key) { var v = z.GetVec3(key, Vector3.zero); return v == Vector3.zero ? "" : V(v); }
                var cells = new[]
                {
                    z.GetString("npc_name_override", "").Replace("|", "/"),
                    p.x.ToString("0.00", inv), p.y.ToString("0.00", inv), p.z.ToString("0.00", inv), z.GetRotation().eulerAngles.y.ToString("0.0", inv),
                    z.GetString("npc_equip_helmet", ""), z.GetString("npc_equip_chest", ""), z.GetString("npc_equip_legs", ""), z.GetString("npc_equip_shoulder", ""),
                    z.GetString("npc_equip_lefthand", ""), z.GetString("npc_equip_righthand", ""), z.GetString("npc_hair", ""), z.GetString("npc_beard", ""),
                    z.GetInt(ZDOVars.s_modelIndex, 0).ToString(inv), z.GetFloat("npc_scale", 1f).ToString("0.###", inv),
                    Col(ZDOVars.s_skinColor), Col(ZDOVars.s_hairColor),
                };
                row("npc", Pass, "npc|" + string.Join("|", cells));
            }
        }
    }
}
