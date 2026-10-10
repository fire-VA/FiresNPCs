using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace FiresRPGmaker.Modules.Buffer
{
    /// <summary>Drop-in parser for the Marketplace Buffer config format (faithful port of
    /// Buffer_Main_Server.ReadBufferProfiles / ReadBufferDatabase), plus optional Fires extension lines.
    ///
    /// Two folders, two grammars (read profiles first, then the database):
    ///   • BufferProfiles/*.cfg — [section]=profile key (lowercased, spaces stripped, FIRST-write-wins) then one
    ///     CSV line of buff UniqueNames the NPC sells.
    ///   • Buffers/*.cfg — [UniqueName] header (spaces stripped, NOT lowercased — the KG case asymmetry) then 7
    ///     positional data lines, optionally followed by Fires KEY=VALUE extension lines.
    ///
    /// We read both the Fires config root AND the Marketplace config root so existing KG buffer configs work
    /// unchanged. The off-by-one in KG's parser (guards index+5 but reads index+6, silently dropping a trailing
    /// BuffGroup at EOF) is fixed here by parsing header-delimited blocks instead of a positional cursor.</summary>
    public static class BufferConfigParser
    {
        public static string FiresBuffersFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.BufferDefinitions);
        public static string FiresProfilesFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.BufferProfiles);
        public static string MarketBuffersFolder => Path.Combine(Paths.ConfigPath, "Marketplace", "Configs", "Buffers");
        public static string MarketProfilesFolder => Path.Combine(Paths.ConfigPath, "Marketplace", "Configs", "BufferProfiles");

        /// <summary>Reads every buffer profile + buff from both the Fires and Marketplace config roots.</summary>
        public static (List<FiresBuffData> buffs, Dictionary<string, string> profiles) ReadAll()
        {
            var profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buffs = new List<FiresBuffData>();
            try
            {
                // Pure clients parse the server-synced in-memory store (no config files on client disk);
                // server/host reads its own folders + the KG Marketplace drop-in trees as before.
                bool useStore = global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore;

                if (!useStore && Directory.Exists(MarketProfilesFolder))
                    foreach (var f in Directory.GetFiles(MarketProfilesFolder, "*.cfg", SearchOption.AllDirectories))
                        ReadBufferProfiles(f, ReadLinesOrEmpty(f), profiles);
                foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines("BufferProfiles"))
                    ReadBufferProfiles(kv.Key, kv.Value, profiles);

                if (!useStore && Directory.Exists(MarketBuffersFolder))
                    foreach (var f in Directory.GetFiles(MarketBuffersFolder, "*.cfg", SearchOption.AllDirectories))
                        buffs.AddRange(ReadBufferDatabase(f, ReadLinesOrEmpty(f)));
                foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines("Buffers"))
                    buffs.AddRange(ReadBufferDatabase(kv.Key, kv.Value));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BufferConfig] read failed: {ex.Message}");
            }
            return (buffs, profiles);
        }

        private static string[] ReadLinesOrEmpty(string path)
        {
            try { return File.ReadAllLines(path); } catch { return new string[0]; }
        }

        /// <summary>Profile file → key (lowercased) ⇒ CSV of UniqueNames. First non-comment line under a section
        /// wins; later lines (any file) are ignored — KG ReadBufferProfiles parity (Buffer_Main_Server.cs:57-70).</summary>
        public static void ReadBufferProfiles(string path, string[] lines, Dictionary<string, string> profiles)
        {
            string key = "default";
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                    key = line.Replace("[", "").Replace("]", "").Replace(" ", "").ToLowerInvariant();
                else if (!profiles.ContainsKey(key))
                    profiles[key] = line.Replace(" ", "");
            }
        }

        /// <summary>Buffer database file → list of buffs. Header-delimited blocks: each [UniqueName] collects the
        /// next 7 positional data lines (KG offsets), then any further KEY=VALUE lines until the next header are
        /// Fires extensions. Parity with KG's positional read for pure-KG files; the trailing-line off-by-one
        /// is fixed by bounding each record on the next header.</summary>
        public static List<FiresBuffData> ReadBufferDatabase(string path, string[] lines)
        {
            var result = new List<FiresBuffData>();
            string uniqueName = null;
            var data = new List<string>();   // up to 7 positional lines
            var ext = new List<string>();    // trailing Fires extension lines

            void Flush()
            {
                if (uniqueName != null && data.Count >= 7)
                {
                    try { result.Add(BuildRecord(uniqueName, data, ext)); }
                    catch (Exception ex) { Debug.LogWarning($"[BufferConfig] {Path.GetFileName(path)} buff '{uniqueName}': {ex.Message}"); }
                }
                else if (uniqueName != null)
                {
                    Debug.LogWarning($"[BufferConfig] {Path.GetFileName(path)} buff '{uniqueName}' has only {data.Count}/7 lines — skipped");
                }
                uniqueName = null; data.Clear(); ext.Clear();
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("#")) continue;
                if (line.StartsWith("["))
                {
                    Flush();
                    uniqueName = line.Replace("[", "").Replace("]", "").Replace(" ", "");   // NOT lowercased
                    continue;
                }
                if (uniqueName == null) continue;          // stray line before any header (blanks included)
                // The 7 positional lines are kept VERBATIM including blanks — an empty SpritePrefab/modifier/
                // StartEffect/BuffGroup is a valid empty field (e.g. auto-created TargetSE buffs); skipping
                // blanks here collapsed the count below 7 and silently dropped the record.
                if (data.Count < 7) data.Add(line);
                else if (!string.IsNullOrWhiteSpace(line)) ext.Add(line);   // extensions skip blanks
            }
            Flush();
            return result;
        }

        private static FiresBuffData BuildRecord(string uniqueName, List<string> d, List<string> ext)
        {
            var b = new FiresBuffData
            {
                UniqueName = uniqueName,
                Name = d[0],                                   // interior spaces preserved (display name)
                Duration = int.Parse(d[1].Replace(" ", "")),
                SpritePrefab = d[2].Replace(" ", ""),
            };

            var cost = d[3].Replace(" ", "").Split(',');
            b.NeededPrefab = cost.Length > 0 ? cost[0] : "";
            b.NeededPrefabCount = cost.Length > 1 && int.TryParse(cost[1], out var c) ? c : 0;

            // Modifier line: comma list of Flag=value; invariant culture; unknown flags skipped (KG parity).
            var flags = WhatToModify.None;
            foreach (var tok in d[4].Replace(" ", "").Split(','))
            {
                var kv = tok.Split('=');
                if (kv.Length != 2) continue;
                if (Enum.TryParse<WhatToModify>(kv[0], true, out var which) &&
                    float.TryParse(kv[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var val))
                {
                    flags |= which;
                    b.ModifyList[which] = val;
                }
            }
            b.Flags = flags;
            b.StartEffectPrefab = d[5].Replace(" ", "");
            b.BuffGroup = d[6].Trim(' ');                      // only ends trimmed — interior spaces preserved

            // Fires extensions (optional, back-compat): TargetSE / Strength(Cap) / DurationCap / CostScales.
            foreach (var line in ext)
            {
                var kv = line.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                string key = kv[0].Trim().ToLowerInvariant();
                string val = kv[1].Trim();
                switch (key)
                {
                    case "targetse":
                        b.TargetSE = val.Replace(" ", "");
                        b.Backend = string.IsNullOrEmpty(b.TargetSE) ? BuffBackend.Synthetic : BuffBackend.TargetSE;
                        break;
                    case "strength":
                    case "strengthcap":
                        if (float.TryParse(val, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var s))
                            b.StrengthCap = Mathf.Clamp01(s);
                        break;
                    case "durationcap":
                        if (int.TryParse(val.Replace(" ", ""), out var dc)) b.DurationCap = dc;
                        break;
                    case "costscales":
                        b.CostScales = val.ToLowerInvariant();
                        break;
                }
            }
            return b;
        }
    }
}
