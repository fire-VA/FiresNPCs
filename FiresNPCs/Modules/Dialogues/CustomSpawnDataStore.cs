using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Named bags of typed ZDO key/value pairs, referenced by key from dialogue commands
    /// (GiveItemWithData / SetPlayerData / Spawn*WithData) and the PlayerHas…CustomDataKeys
    /// conditions — the Marketplace CustomSpawnData system (schema:
    /// Docs/marketplace-schemas/CustomSpawnData.md).
    ///
    /// Sources, merged (Fires wins on key collision):
    ///   1. Marketplace drop-in: BepInEx/config/Marketplace/Configs/CustomSpawnData/**.yml
    ///   2. Fires:               BepInEx/config/FiresRPGmaker/CustomSpawnData/**.yml|.yaml
    /// Key = filename without extension, lowercased (MP contract). One malformed file is
    /// skipped with a warning — deliberately NOT MP's all-or-nothing abort.
    ///
    /// The files are flat two-level YAML (five optional dictionaries: Ints/Floats/Longs/
    /// Strings/Bools), parsed here directly — no YAML library in this mod.
    /// </summary>
    public static class CustomSpawnDataStore
    {
        public sealed class Blob
        {
            public readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
            public readonly Dictionary<string, float> Floats = new Dictionary<string, float>();
            public readonly Dictionary<string, long> Longs = new Dictionary<string, long>();
            public readonly Dictionary<string, string> Strings = new Dictionary<string, string>();
            public readonly Dictionary<string, bool> Bools = new Dictionary<string, bool>();

            public bool IsEmpty => Ints.Count + Floats.Count + Longs.Count + Strings.Count + Bools.Count == 0;

            public void ApplyTo(ZDO zdo)
            {
                if (zdo == null) return;
                foreach (var kv in Ints) zdo.Set(kv.Key, kv.Value);
                foreach (var kv in Floats) zdo.Set(kv.Key, kv.Value);
                foreach (var kv in Longs) zdo.Set(kv.Key, kv.Value);
                foreach (var kv in Strings) zdo.Set(kv.Key, kv.Value);
                foreach (var kv in Bools) zdo.Set(kv.Key, kv.Value);
            }

            /// <summary>MP condition semantics: "All" = every entry must match the ZDO;
            /// "OneOf" = at least one entry matches. Floats compare with 0.01 tolerance.</summary>
            public bool MatchesZdo(ZDO zdo, bool requireAll)
            {
                if (zdo == null) return false;
                int total = 0, hits = 0;
                foreach (var kv in Ints) { total++; if (zdo.GetInt(kv.Key) == kv.Value) hits++; }
                foreach (var kv in Floats) { total++; if (Mathf.Abs(zdo.GetFloat(kv.Key) - kv.Value) <= 0.01f) hits++; }
                foreach (var kv in Longs) { total++; if (zdo.GetLong(kv.Key) == kv.Value) hits++; }
                foreach (var kv in Strings) { total++; if (zdo.GetString(kv.Key) == kv.Value) hits++; }
                foreach (var kv in Bools) { total++; if (zdo.GetBool(kv.Key) == kv.Value) hits++; }
                if (total == 0) return false;
                return requireAll ? hits == total : hits > 0;
            }
        }

        private static readonly Dictionary<string, Blob> _blobs = new Dictionary<string, Blob>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private static float _nextStaleCheck;
        private static DateTime _newestSeen = DateTime.MinValue;

        public static bool TryGet(string key, out Blob blob)
        {
            EnsureLoaded();
            blob = null;
            return !string.IsNullOrEmpty(key) && _blobs.TryGetValue(key.Trim(), out blob);
        }

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static IEnumerable<string> SourceFolders()
        {
            // Marketplace drop-in first so Fires files win collisions.
            yield return Path.Combine(BepInEx.Paths.ConfigPath, "Marketplace", "Configs", "CustomSpawnData");
            yield return Path.Combine(FiresCore.Storage.FiresConfigPaths.Root, "CustomSpawnData");
        }

        private static void EnsureLoaded()
        {
            // Cheap hot-reload: rescan when any source file is newer than the last load
            // (checked at most every 5s — the Fires cfg hot-reload standard).
            if (_loaded && Time.unscaledTime >= _nextStaleCheck)
            {
                _nextStaleCheck = Time.unscaledTime + 5f;
                if (NewestWriteTime() > _newestSeen) _loaded = false;
            }
            if (_loaded) return;
            _loaded = true;
            _nextStaleCheck = Time.unscaledTime + 5f;
            _blobs.Clear();

            foreach (string folder in SourceFolders())
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (string file in Directory.GetFiles(folder, "*.y*ml", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var blob = ParseFile(file);
                            if (blob == null || blob.IsEmpty) continue;
                            string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                            _blobs[key] = blob;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[CustomSpawnData] skipping '{Path.GetFileName(file)}': {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CustomSpawnData] folder scan failed for {folder}: {ex.Message}");
                }
            }

            _newestSeen = NewestWriteTime();
            if (_blobs.Count > 0)
                Debug.Log($"[CustomSpawnData] loaded {_blobs.Count} data blob(s)");
        }

        private static DateTime NewestWriteTime()
        {
            DateTime newest = DateTime.MinValue;
            foreach (string folder in SourceFolders())
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (string file in Directory.GetFiles(folder, "*.y*ml", SearchOption.AllDirectories))
                    {
                        DateTime t = File.GetLastWriteTimeUtc(file);
                        if (t > newest) newest = t;
                    }
                }
                catch { }
            }
            return newest;
        }

        // Flat two-level YAML: top-level section names (Ints:, Floats:, Longs:, Strings:,
        // Bools:) with indented "key: value" entries. Comments (#) and blank lines ignored.
        private static Blob ParseFile(string path)
        {
            var blob = new Blob();
            string section = null;
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = StripComment(rawLine);
                if (string.IsNullOrWhiteSpace(line)) continue;

                bool indented = rawLine.Length > 0 && (rawLine[0] == ' ' || rawLine[0] == '\t');
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string left = line.Substring(0, colon).Trim();
                string right = line.Substring(colon + 1).Trim();

                if (!indented)
                {
                    section = right.Length == 0 ? left : null;   // "Ints:" opens a section; "key: v" at root is ignored
                    continue;
                }
                if (section == null || left.Length == 0 || right.Length == 0) continue;

                switch (section.ToLowerInvariant())
                {
                    case "ints":
                        if (int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) blob.Ints[left] = i;
                        break;
                    case "floats":
                        if (float.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) blob.Floats[left] = f;
                        break;
                    case "longs":
                        if (long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) blob.Longs[left] = l;
                        break;
                    case "strings":
                        blob.Strings[left] = Unquote(right);
                        break;
                    case "bools":
                        if (bool.TryParse(right, out bool b)) blob.Bools[left] = b;
                        break;
                }
            }
            return blob;
        }

        private static string StripComment(string line)
        {
            // A # outside quotes starts a comment.
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
            => s.Length >= 2 && ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '\'' && s[s.Length - 1] == '\''))
                ? s.Substring(1, s.Length - 2)
                : s;
    }
}
