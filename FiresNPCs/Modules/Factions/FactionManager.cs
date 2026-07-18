using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// Loads faction definitions and holds the runtime table. Sources, merged (Fires wins):
    ///   1. Marketplace drop-in: BepInEx/config/Marketplace/Configs/Factions.yml
    ///   2. Fires:               BepInEx/config/FiresRPGmaker/Factions.yml
    /// Both are the MP PascalCase YAML shape (a map of faction-id → definition). The server loads +
    /// pushes the raw YAML to clients via FiresConfigSync ("Factions" folder) so every machine
    /// resolves the same table; each side parses locally (hashes are machine-stable).
    ///
    /// The YAML is parsed by a small indentation reader scoped to exactly this schema (no YamlDotNet
    /// dependency): faction ids at indent 0, fields at indent 2, list items / skill pairs at indent 4.
    /// </summary>
    public static class FactionManager
    {
        private static readonly Dictionary<string, FactionData> _factions = new Dictionary<string, FactionData>(StringComparer.OrdinalIgnoreCase);
        // Unions across all factions — the cheap "is this item restricted AT ALL" fast-path so the
        // hot use/build/craft patches early-out for the vast majority of (unrestricted) items.
        private static readonly HashSet<int> _anyUse = new HashSet<int>();
        private static readonly HashSet<int> _anyBuild = new HashSet<int>();
        private static readonly HashSet<int> _anyCraft = new HashSet<int>();
        private static bool _loaded;
        private static float _nextStaleCheck;
        private static DateTime _seenWrite = DateTime.MinValue;

        public static IReadOnlyDictionary<string, FactionData> All { get { EnsureLoaded(); return _factions; } }

        public static FactionData Get(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return null;
            _factions.TryGetValue(id.Trim().ToLowerInvariant(), out var f);
            return f;
        }

        public static bool Exists(string id) => Get(id) != null;

        public static IEnumerable<string> Ids { get { EnsureLoaded(); return _factions.Keys; } }

        public static void Reload() { _loaded = false; EnsureLoaded(); }

        // MP drop-in single file + the Fires "Factions" folder (server-synced via
        // ServerConfigFileWatcher's WatchedFolders → every *.yml the admin drops in reaches clients).
        private static IEnumerable<string> SourceFiles()
        {
            string mp = Path.Combine(Paths.ConfigPath, "Marketplace", "Configs", "Factions.yml");
            if (File.Exists(mp)) yield return mp;

            string folder = FiresCore.Storage.FiresConfigPaths.GroupedPath("Factions");
            if (Directory.Exists(folder))
                foreach (var f in Directory.GetFiles(folder, "*.y*ml", SearchOption.AllDirectories))
                    yield return f;
        }

        private static void EnsureLoaded()
        {
            if (_loaded && Time.unscaledTime >= _nextStaleCheck)
            {
                _nextStaleCheck = Time.unscaledTime + 5f;
                if (NewestWrite() > _seenWrite) _loaded = false;
            }
            if (_loaded) return;
            _loaded = true;
            _nextStaleCheck = Time.unscaledTime + 5f;
            _factions.Clear();
            _anyUse.Clear(); _anyBuild.Clear(); _anyCraft.Clear();

            foreach (string path in SourceFiles())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    ParseFile(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FactionManager] parse failed for {Path.GetFileName(path)}: {ex.Message}");
                }
            }
            foreach (var f in _factions.Values)
            {
                _anyUse.UnionWith(f.SpecificUseItems);
                _anyBuild.UnionWith(f.SpecificBuildings);
                _anyCraft.UnionWith(f.SpecificCraftItems);
            }
            _seenWrite = NewestWrite();
            if (_factions.Count > 0)
                Debug.Log($"[FactionManager] loaded {_factions.Count} faction(s): {string.Join(", ", _factions.Keys)}");
        }

        // Whitelist checks: an item is faction-gated only if SOME faction lists it; then the local
        // player may act only if THEIR faction lists it. Unrestricted items always pass (fast-path).
        public static bool UseAllowed(int itemHash, FactionData local)
        {
            EnsureLoaded();
            if (!_anyUse.Contains(itemHash)) return true;
            return local != null && local.SpecificUseItems.Contains(itemHash);
        }

        public static bool BuildAllowed(int pieceHash, FactionData local)
        {
            EnsureLoaded();
            if (!_anyBuild.Contains(pieceHash)) return true;
            return local != null && local.SpecificBuildings.Contains(pieceHash);
        }

        public static bool CraftAllowed(int itemHash, FactionData local)
        {
            EnsureLoaded();
            if (!_anyCraft.Contains(itemHash)) return true;
            return local != null && local.SpecificCraftItems.Contains(itemHash);
        }

        // Blacklist: the local player's own faction blocks picking this item up.
        public static bool PickupBlocked(int itemHash, FactionData local)
            => local != null && local.BlockPickupItems.Contains(itemHash);

        private static DateTime NewestWrite()
        {
            DateTime newest = DateTime.MinValue;
            try { foreach (string path in SourceFiles()) { var t = File.GetLastWriteTimeUtc(path); if (t > newest) newest = t; } } catch { }
            return newest;
        }

        // ── the scoped YAML reader ───────────────────────────────────────────
        // Indent 0: "<id>:"  → new faction.  Indent 2: "<Field>:" or "<Field>: <scalar>".
        // Indent 4 under a list field: "- <name>".  Indent 4 under BonusSkills: "<Skill>: <int>".
        private static void ParseFile(string text)
        {
            FactionData cur = null;
            string listField = null;   // the indent-2 field currently collecting indent-4 children
            foreach (var rawLine in text.Replace("\r", "").Split('\n'))
            {
                string line = StripComment(rawLine);
                if (line.Trim().Length == 0) continue;
                int indent = CountIndent(rawLine);
                string body = line.Trim();

                if (indent == 0)
                {
                    // "<id>:" — start a faction.
                    string id = TrimKey(body);
                    if (string.IsNullOrEmpty(id)) { cur = null; continue; }
                    cur = new FactionData { Id = id.ToLowerInvariant() };
                    _factions[cur.Id] = cur;
                    listField = null;
                    continue;
                }
                if (cur == null) continue;

                if (indent <= 2)
                {
                    listField = null;
                    int colon = body.IndexOf(':');
                    if (colon < 0) continue;
                    string key = body.Substring(0, colon).Trim();
                    string val = body.Substring(colon + 1).Trim();

                    switch (key.ToLowerInvariant())
                    {
                        case "name": cur.Name = Unquote(val); break;
                        case "icon": cur.Icon = Unquote(val); break;
                        case "buff": cur.Buff = Unquote(val).ToLowerInvariant(); break;
                        case "candamagesamefaction": cur.CanDamageSameFaction = !val.Equals("false", StringComparison.OrdinalIgnoreCase); break;
                        case "specificuseitems": case "blockpickupitems": case "specificbuildings":
                        case "specificcraftitems": case "friendlymonsters": case "bonusskills":
                            listField = key.ToLowerInvariant();
                            // An inline list on the same line (rare) would go here; MP uses block lists.
                            break;
                    }
                    continue;
                }

                // indent >= 4: a child of the current listField.
                if (listField == null) continue;
                if (body.StartsWith("-"))
                {
                    string name = Unquote(body.Substring(1).Trim());
                    if (name.Length == 0) continue;
                    AddToSet(cur, listField, name);
                }
                else if (listField == "bonusskills")
                {
                    int colon = body.IndexOf(':');
                    if (colon < 0) continue;
                    string skill = body.Substring(0, colon).Trim();
                    string num = body.Substring(colon + 1).Trim();
                    if (Enum.TryParse(skill, true, out Skills.SkillType st)
                        && int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bonus))
                        cur.BonusSkills[st] = bonus;
                }
            }
        }

        private static void AddToSet(FactionData f, string field, string prefabName)
        {
            int hash = prefabName.GetStableHashCode();
            // set.Add returns false when already present, so the parallel name list stays deduped in lockstep.
            switch (field)
            {
                case "specificuseitems": if (f.SpecificUseItems.Add(hash)) f.UseNames.Add(prefabName); break;
                case "blockpickupitems": if (f.BlockPickupItems.Add(hash)) f.PickupNames.Add(prefabName); break;
                case "specificbuildings": if (f.SpecificBuildings.Add(hash)) f.BuildNames.Add(prefabName); break;
                case "specificcraftitems": if (f.SpecificCraftItems.Add(hash)) f.CraftNames.Add(prefabName); break;
                case "friendlymonsters": if (f.FriendlyMonsters.Add(hash)) f.FriendlyNames.Add(prefabName); break;
            }
        }

        // ── Editor round-trip: write a faction to a Fires Factions/<id>.yml + sync + reload ──────────────
        public static string Serialize(FactionData f)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(f.Id).Append(":\n");
            sb.Append("  Name: \"").Append((f.Name ?? f.Id).Replace("\"", "'")).Append("\"\n");
            if (!string.IsNullOrEmpty(f.Icon)) sb.Append("  Icon: ").Append(f.Icon).Append('\n');
            if (!string.IsNullOrEmpty(f.Buff)) sb.Append("  Buff: ").Append(f.Buff).Append('\n');
            if (!f.CanDamageSameFaction) sb.Append("  CanDamageSameFaction: false\n");
            AppendList(sb, "SpecificUseItems", f.UseNames);
            AppendList(sb, "BlockPickupItems", f.PickupNames);
            AppendList(sb, "SpecificBuildings", f.BuildNames);
            AppendList(sb, "SpecificCraftItems", f.CraftNames);
            AppendList(sb, "FriendlyMonsters", f.FriendlyNames);
            if (f.BonusSkills.Count > 0)
            {
                sb.Append("  BonusSkills:\n");
                foreach (var kv in f.BonusSkills) sb.Append("    ").Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
            }
            return sb.ToString();
        }

        private static void AppendList(System.Text.StringBuilder sb, string key, List<string> names)
        {
            if (names == null || names.Count == 0) return;
            sb.Append("  ").Append(key).Append(":\n");
            foreach (var n in names) if (!string.IsNullOrWhiteSpace(n)) sb.Append("    - ").Append(n.Trim()).Append('\n');
        }

        private static string SafeId(string id) =>
            System.Text.RegularExpressions.Regex.Replace((id ?? "").Trim().ToLowerInvariant(), "[^a-z0-9_-]", "_");

        public static void SaveFaction(FactionData f)
        {
            if (f == null || string.IsNullOrWhiteSpace(f.Id)) return;
            try
            {
                string folder = FiresCore.Storage.FiresConfigPaths.GroupedPath("Factions");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string safe = SafeId(f.Id);
                string content = Serialize(f);
                File.WriteAllText(Path.Combine(folder, safe + ".yml"), content);
                Reload();
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Factions", safe + ".yml", content);
                Debug.Log($"[FactionManager] Saved faction '{f.Id}'");
            }
            catch (Exception ex) { Debug.LogError("[FactionManager] SaveFaction failed: " + ex.Message); }
        }

        public static void DeleteFaction(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            try
            {
                string safe = SafeId(id);
                string file = Path.Combine(FiresCore.Storage.FiresConfigPaths.GroupedPath("Factions"), safe + ".yml");
                if (File.Exists(file)) File.Delete(file);
                Reload();
                // Push an empty body so clients' copy parses to nothing → the faction drops on their next reload.
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Factions", safe + ".yml", "");
                Debug.Log($"[FactionManager] Deleted faction '{id}'");
            }
            catch (Exception ex) { Debug.LogError("[FactionManager] DeleteFaction failed: " + ex.Message); }
        }

        private static int CountIndent(string line)
        {
            int n = 0;
            foreach (char c in line) { if (c == ' ') n++; else if (c == '\t') n += 2; else break; }
            return n;
        }

        private static string TrimKey(string body)
        {
            int colon = body.IndexOf(':');
            return colon < 0 ? body.Trim() : body.Substring(0, colon).Trim();
        }

        private static string StripComment(string line)
        {
            bool inQuote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inQuote = !inQuote;
                else if (c == '#' && !inQuote) return line.Substring(0, i);
            }
            return line;
        }

        private static string Unquote(string s)
        {
            if (s.Length >= 2 && ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '\'' && s[s.Length - 1] == '\'')))
                return s.Substring(1, s.Length - 2);
            return s;
        }
    }
}
