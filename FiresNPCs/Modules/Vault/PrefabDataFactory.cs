using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Vault
{
    internal static class PrefabDataFactory
    {
        private static readonly Dictionary<string, string> _dataCache = new(StringComparer.OrdinalIgnoreCase);

        public static string LoadCustomSpawnerData(string dataName)
        {
            if (string.IsNullOrEmpty(dataName)) return null;
            if (_dataCache.TryGetValue(dataName, out var cached)) return cached;

            try
            {
                string baseDir = Path.Combine(Paths.ConfigPath, "data");
                if (!Directory.Exists(baseDir)) return null;

                var allFiles = Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories);
                var dataFiles = allFiles.Where(f => Path.GetFileName(f).StartsWith("data", StringComparison.OrdinalIgnoreCase)).ToList();
                if (dataFiles.Count == 0)
                {
                    dataFiles = allFiles.ToList();
                }

                var pattern = "^\\s*(?:-\\s*)?name\\s*:\\s*(['\"']?)" + Regex.Escape(dataName) + "\\1\\b";
                var rx = new Regex(pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);

                foreach (var f in dataFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(f);
                        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);

                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (!rx.IsMatch(lines[i])) continue;

                            int leading = lines[i].TakeWhile(ch => ch == ' ').Count();
                            bool isListItem = lines[i].TrimStart().StartsWith("- ");
                            int itemIndent = leading;

                            int j = i + 1;
                            for (; j < lines.Length; j++)
                            {
                                var line = lines[j];
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                int nextIndent = line.TakeWhile(ch => ch == ' ').Count();

                                if (isListItem)
                                {
                                    if (line.TrimStart().StartsWith("- ") && nextIndent == itemIndent) break;
                                    if (nextIndent <= itemIndent && !line.TrimStart().StartsWith("- ")) break;
                                }
                                else
                                {
                                    if (nextIndent <= leading) break;
                                }
                            }

                            int end = Math.Max(i, j - 1);
                            var extracted = string.Join("\n", lines.Skip(i).Take(end - i + 1));
                            _dataCache[dataName] = extracted;
                            return extracted;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public static void ApplyCustomData(GameObject go, string yaml)
        {
            if (go == null || string.IsNullOrEmpty(yaml)) return;
            var nview = go.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            var character = go.GetComponent<Character>();
            var humanoid = go.GetComponent<Humanoid>();

            int? level = null;
            long? overrideBoss = null;
            float? maxHealth = null;
            float? health = null;
            string displayName = null;

            var intValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var floatValues = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var stringValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var longValues = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var vecValues = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            var hashValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            string currentSection = string.Empty;
            foreach (var rawLine in yaml.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("#")) continue;
                if (line.StartsWith("name", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = string.Empty;
                    continue;
                }
                if (line.EndsWith(":"))
                {
                    currentSection = line.TrimEnd(':').Trim().ToLowerInvariant();
                    continue;
                }
                if (!line.StartsWith("-")) continue;
                var payload = line.TrimStart('-').Trim();
                if (string.IsNullOrEmpty(payload)) continue;

                string key = payload;
                string val = string.Empty;
                int comma = payload.IndexOf(',');
                if (comma >= 0)
                {
                    key = payload.Substring(0, comma).Trim();
                    val = payload.Substring(comma + 1).Trim();
                }
                else
                {
                    var kv = payload.Split(new[] { ':' }, 2);
                    if (kv.Length == 2)
                    {
                        key = kv[0].Trim();
                        val = kv[1].Trim();
                    }
                }

                switch (currentSection)
                {
                    case "ints":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                        {
                            intValues[key] = iv;
                            if (string.Equals(key, "level", StringComparison.OrdinalIgnoreCase)) level = iv;
                            if (string.Equals(key, "override_boss", StringComparison.OrdinalIgnoreCase)) overrideBoss = iv;
                        }
                        break;
                    case "floats":
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
                        {
                            floatValues[key] = fv;
                            if (string.Equals(key, "health", StringComparison.OrdinalIgnoreCase)) health = fv;
                            if (string.Equals(key, "max_health", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "Humanoid.m_health", StringComparison.OrdinalIgnoreCase)) maxHealth = fv;
                        }
                        break;
                    case "strings":
                        stringValues[key] = val; // allow empty string overrides
                        if (!string.IsNullOrEmpty(val) && key.IndexOf("m_name", StringComparison.OrdinalIgnoreCase) >= 0)
                            displayName = val;
                        break;
                    case "longs":
                        if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv))
                        {
                            longValues[key] = lv;
                        }
                        break;
                    case "vecs":
                        {
                            var parts = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3
                                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var vx)
                                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var vy)
                                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vz))
                            {
                                vecValues[key] = new Vector3(vx, vy, vz);
                            }
                        }
                        break;
                    case "hashes":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hv))
                        {
                            hashValues[key] = hv;
                        }
                        break;
                    default:
                        break;
                }
            }

            if (!intValues.ContainsKey("HasFields"))
            {
                intValues["HasFields"] = 1;
            }
            if (humanoid != null && !intValues.ContainsKey("HasFieldsHumanoid"))
            {
                intValues["HasFieldsHumanoid"] = 1;
            }

            foreach (var kvp in intValues)
                zdo?.Set(kvp.Key, kvp.Value);
            foreach (var kvp in floatValues)
                zdo?.Set(kvp.Key, kvp.Value);
            foreach (var kvp in stringValues)
                zdo?.Set(kvp.Key, kvp.Value);
            foreach (var kvp in longValues)
                zdo?.Set(kvp.Key, kvp.Value);
            foreach (var kvp in vecValues)
                zdo?.Set(kvp.Key, kvp.Value);
            foreach (var kvp in hashValues)
                zdo?.Set(kvp.Key, kvp.Value);

            if (level.HasValue && character != null)
            {
                character.SetLevel(Math.Max(1, level.Value));
                zdo?.Set("level", level.Value);
            }

            if (overrideBoss.HasValue)
            {
                zdo?.Set("override_boss", (int)overrideBoss.Value);
            }

            if (maxHealth.HasValue)
            {
                zdo?.Set("max_health", maxHealth.Value);
            }

            if (!string.IsNullOrEmpty(displayName))
            {
                try { AccessTools.Field(typeof(Character), "m_name")?.SetValue(character, displayName); } catch { }
                try { AccessTools.Field(typeof(Humanoid), "m_name")?.SetValue(humanoid, displayName); } catch { }
                zdo?.Set("Humanoid.m_name", displayName);
                try { go.name = displayName; } catch { }
            }

            if (health.HasValue && character != null)
            {
                zdo?.Set("health", health.Value);
            }
        }
    }
}
