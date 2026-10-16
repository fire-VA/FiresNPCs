using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace FiresRPGmaker.Modules.Buffer
{
    /// <summary>Writes buffer configs in the drop-in KG 7-line format (+ Fires extension lines) and gets them to
    /// the server: on the host it writes directly; on a dedi (admin = client) it writes a local copy AND pushes
    /// via the shared FiresRPGmaker_SyncFiles RPC, which the server writes + the file-watcher reloads/broadcasts.</summary>
    public static class BufferConfigWriter
    {
        public static string SerializeBuff(FiresBuffData b)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{b.UniqueName}]");
            sb.AppendLine(string.IsNullOrEmpty(b.Name) ? b.UniqueName : b.Name);
            sb.AppendLine(b.Duration.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(b.SpritePrefab ?? "");
            sb.AppendLine($"{b.NeededPrefab},{b.NeededPrefabCount}");

            var mods = new List<string>();
            foreach (var kv in b.ModifyList)
            {
                float def = (kv.Key == WhatToModify.ModifyMaxCarryWeight || kv.Key == WhatToModify.DamageReduction) ? 0f : 1f;
                if (Mathf.Abs(kv.Value - def) > 0.0001f)
                    mods.Add($"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}");
            }
            sb.AppendLine(string.Join(",", mods));
            sb.AppendLine(b.StartEffectPrefab ?? "");
            sb.AppendLine(b.BuffGroup ?? "");

            if (b.Backend == BuffBackend.TargetSE && !string.IsNullOrEmpty(b.TargetSE)) sb.AppendLine($"TargetSE={b.TargetSE}");
            if (b.StrengthCap < 0.999f) sb.AppendLine($"StrengthCap={b.StrengthCap.ToString(CultureInfo.InvariantCulture)}");
            if (b.DurationCap > 0) sb.AppendLine($"DurationCap={b.DurationCap.ToString(CultureInfo.InvariantCulture)}");
            // 10-09: the parser read CostScales but the writer never wrote it (Buffer Setup edits it now).
            if (!string.IsNullOrEmpty(b.CostScales) && !string.Equals(b.CostScales, "none", StringComparison.OrdinalIgnoreCase)) sb.AppendLine($"CostScales={b.CostScales}");
            return sb.ToString();
        }

        public static void WriteBuff(FiresBuffData b) => PushOrWrite("Buffers", SafeName(b.UniqueName) + ".cfg", SerializeBuff(b));

        public static void WriteProfile(string profileKey, IEnumerable<string> uniqueNames)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{profileKey}]");
            sb.AppendLine(string.Join(",", uniqueNames));
            PushOrWrite("BufferProfiles", SafeName(profileKey) + ".cfg", sb.ToString());
        }

        private static void PushOrWrite(string folder, string fileName, string content)
        {
            // Pure clients keep no config files on disk — the server push + rebroadcast feeds the
            // in-memory store; server/host writes its grouped folder as before.
            if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
            {
                // Write a local copy + reload so the authoring admin sees it immediately.
                try
                {
                    string dir = FiresCore.Storage.FiresConfigPaths.GroupedPath(folder);
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, fileName), content);
                    BufferManager.Reload();
                }
                catch (Exception ex) { Debug.LogWarning($"[BufferConfigWriter] local write failed: {ex.Message}"); }
            }

            // On a dedi the admin is a client — push to the server (it writes + the watcher reloads/broadcasts).
            try
            {
                if (ZNet.instance != null && ZNet.instance.IsServer()) return;
                if (ZRoutedRpc.instance == null) return;
                var pkg = new ZPackage();
                pkg.Write(folder);
                pkg.Write(fileName);
                pkg.Write(content);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_SyncFiles", pkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[BufferConfigWriter] push failed: {ex.Message}"); }
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "default";
            var sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length > 0 ? sb.ToString() : "default";
        }
    }
}
