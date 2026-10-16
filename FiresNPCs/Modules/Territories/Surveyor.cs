using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using BepInEx;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Admin-only auto marker that writes legacy territory cfg entries while an admin walks a path.
    /// Automatically places 15m radius circles every 10m along your path.
    /// 
    /// Command Format: startmarking Name[,R,G,B,flags]
    /// 
    /// Parameters (comma-separated):
    ///   Name  - Territory name (required)
    ///   R,G,B - Color values 0-255 (optional, default: 255,0,0 red)
    ///   flags - Territory flags separated by | (optional, default: None)
    /// 
    /// Examples:
    ///   startmarking DangerPath
    /// startmarking SafeRoad,0,255,0
    ///   startmarking RestrictedPath,255,0,0,NoMonsters|NoBuild
    ///   startmarking SpeedTrail,0,0,255,MoveSpeedMultiplier=1.5
    /// 
    /// Usage:
    ///   1. Run: startmarking MyPath,R,G,B,flags
    ///   2. Walk along the desired path
    ///   3. Run: stopmarking
    /// 
    /// Output: Config/FiresRPGmaker/Territories/legacy/AutoMarker.cfg
    /// </summary>
    [HarmonyPatch]
    internal static class Surveyor
    {
        private const float Radius = 15f;
        private const float Spacing = 10f;
        private static bool _active;
        private static string _profileName = string.Empty;
        private static Vector3 _lastPoint;
        private static string _filePath;
        private static int _count;
        private static bool _registered;
        private static int _colorR = 255, _colorG = 0, _colorB = 0;
        private static string _conditions = "None";

        private static string LegacyDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories, "legacy");
        private static string AutoPath => Path.Combine(LegacyDir, "AutoMarker.cfg");

        [HarmonyPatch(typeof(Terminal), "Awake")]
        [HarmonyPostfix]
        private static void RegisterCommands(Terminal __instance)
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand("startmarking",
                "Start auto territory path marking.\n" +
                "  Format: startmarking Name[,R,G,B,flags]\n" +
                "  Walk to create territory nodes every 10m along your path.\n" +
                "  Examples:\n" +
                "    startmarking DangerPath\n" +
                "    startmarking SafeRoad,0,255,0\n" +
                "    startmarking RestrictedPath,255,0,0,NoMonsters|NoBuild",
                StartCommand);

            new Terminal.ConsoleCommand("stopmarking",
                "Stop auto territory path marking and save the path.",
                StopCommand);
        }

        private static void StartCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                if (TerritoryFileSync.RefuseIfNotAdmin(args.Context, "[Surveyor]")) return;   // 0.0.504
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("[Surveyor] No local player");
                    return;
                }

                if (!TryParseArgs(args.FullLine, "startmarking", out var name, out var r, out var g, out var b, out var flags))
                {
                    args.Context.AddString("[Surveyor] Format: startmarking Name[,R,G,B,flags]");
                    args.Context.AddString("[Surveyor] Examples:");
                    args.Context.AddString("[Surveyor]   startmarking DangerPath");
                    args.Context.AddString("[Surveyor]   startmarking SafeRoad,0,255,0");
                    args.Context.AddString("[Surveyor]   startmarking RestrictedPath,255,0,0,NoMonsters|NoBuild");
                    return;
                }

                _colorR = r;
                _colorG = g;
                _colorB = b;
                _conditions = string.IsNullOrWhiteSpace(flags) ? "None" : flags;

                Directory.CreateDirectory(LegacyDir);
                _filePath = AutoPath;
                _profileName = name.Trim();
                _active = true;
                _count = 0;
                _lastPoint = Player.m_localPlayer.transform.position;

                if (!File.Exists(_filePath))
                {
                    File.WriteAllText(_filePath, string.Empty);
                }

                AppendTerritory(_lastPoint);

                args.Context.AddString($"[Surveyor] Started marking '{_profileName}'");
                args.Context.AddString($"[Surveyor]   Color: RGB({_colorR},{_colorG},{_colorB})");
                args.Context.AddString($"[Surveyor]   Flags: {_conditions}");
                args.Context.AddString($"[Surveyor]   Walk to mark path, then run 'stopmarking'");

                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Started marking path '{_profileName}'");
            }
            catch (Exception ex)
            {
                args.Context.AddString($"[Surveyor] Error: {ex.Message}");
            }
        }

        private static void StopCommand(Terminal.ConsoleEventArgs args)
        {
            if (!_active)
            {
                args.Context.AddString("[Surveyor] Not currently marking a path");
                args.Context.AddString("[Surveyor] Use 'startmarking Name' to begin");
                return;
            }

            _active = false;
            args.Context.AddString($"[Surveyor] Stopped marking '{_profileName}'");
            args.Context.AddString($"[Surveyor]   Total nodes: {_count}");
            args.Context.AddString($"[Surveyor]   Saved to: {_filePath}");

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Path '{_profileName}' saved ({_count} nodes)");

            // Reload territories
            try
            {
                TerritoryLedger.Reload();
                TerritorySyncedState.BroadcastTerritories();
            }
            catch { }
        }

        /// <summary>
        /// Parses command arguments in format: Name[,R,G,B,flags]
        /// </summary>
        private static bool TryParseArgs(string raw, string cmd, out string name, out int r, out int g, out int b, out string flags)
        {
            // Defaults
            name = string.Empty;
            r = 255; g = 0; b = 0; // Default red
            flags = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(raw)) return false;

                // Remove command prefix
                raw = raw.Trim();
                var lower = raw.ToLowerInvariant();
                int idx = lower.IndexOf(cmd.ToLowerInvariant());
                if (idx >= 0)
                {
                    raw = raw.Substring(idx + cmd.Length).TrimStart(' ', '=');
                }

                if (string.IsNullOrWhiteSpace(raw)) return false;

                // Split by comma: Name,R,G,B,flags
                var parts = raw.Split(new[] { ',' }, StringSplitOptions.None);

                // Part 0: Name (required)
                name = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(name)) return false;

                // Parts 1,2,3: R,G,B (optional)
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    int.TryParse(parts[1].Trim(), out r);
                if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    int.TryParse(parts[2].Trim(), out g);
                if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
                    int.TryParse(parts[3].Trim(), out b);

                // Part 4+: flags (optional)
                if (parts.Length > 4)
                {
                    flags = string.Join(",", parts.Skip(4)).Trim();
                }

                r = Mathf.Clamp(r, 0, 255);
                g = Mathf.Clamp(g, 0, 255);
                b = Mathf.Clamp(b, 0, 255);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Tick(Player player)
        {
            if (!_active || player == null || player != Player.m_localPlayer) return;

            var pos = player.transform.position;
            if (Vector3.Distance(pos, _lastPoint) >= Spacing)
            {
                _lastPoint = pos;
                AppendTerritory(pos);
            }
        }

        private static void AppendTerritory(Vector3 pos)
        {
            try
            {
                _count++;
                var lines = File.Exists(_filePath) ? File.ReadAllLines(_filePath).ToList() : new System.Collections.Generic.List<string>();

                lines.Add($"[{_profileName}]");
                lines.Add("Circle");
                lines.Add($"{Mathf.RoundToInt(pos.x)},{Mathf.RoundToInt(pos.z)},{Mathf.RoundToInt(Radius)}");
                lines.Add($"{Mathf.Clamp(_colorR, 0, 255)},{Mathf.Clamp(_colorG, 0, 255)},{Mathf.Clamp(_colorB, 0, 255)},80,true");
                lines.Add(string.IsNullOrWhiteSpace(_conditions) ? "None" : _conditions);
                lines.Add("ALL");

                File.WriteAllLines(_filePath, lines);
                TerritoryFileSync.Saved(_filePath);   // 0.0.504: each marked node reaches the server on a dedi client
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Marked node {_count}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Surveyor] Failed to append territory: {ex.Message}");
            }
        }
    }
}
