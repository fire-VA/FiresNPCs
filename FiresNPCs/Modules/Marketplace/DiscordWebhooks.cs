using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// kg DiscordStuff drop-in: reads kg's exact server-side webhook config
    /// (config/Marketplace/DiscordStuff/DiscordSettings.cfg — [Webhook Links]/[Webhook Messages]/
    /// [Webhook Titles], kg defaults) and posts kg's exact payload shape ({username, content}).
    /// Server-side only — clients never see the links (the gambler client asks the server to post,
    /// mirroring kg's "KGmarket CustomWebhooks" flow). "LINK HERE" / non-absolute URLs = disabled.
    /// Lootboxes exists in the file but kg never shipped the module — not exposed here.
    /// </summary>
    public static class DiscordWebhooks
    {
        private static string CfgPath => Path.Combine(Paths.ConfigPath, "Marketplace", "DiscordStuff", "DiscordSettings.cfg");

        private sealed class Hook { public string Link; public string Message; public string Title; }
        private static readonly Dictionary<string, Hook> _hooks = new Dictionary<string, Hook>(StringComparer.OrdinalIgnoreCase)
        {
            ["Marketplace"] = new Hook { Link = "LINK HERE", Message = "**{0}** posted **x{1} {2}** with **{3} {4} each**", Title = "Marketplace Message" },
            ["Gambler"]     = new Hook { Link = "LINK HERE", Message = "**{0}** won **x{1} {2}**!", Title = "Gambler Message" },
            ["Quest"]       = new Hook { Link = "LINK HERE", Message = "**{0}** finished quest **{1}**", Title = "Quest Message" },
        };
        private static bool _loaded;
        private static float _nextStaleCheck;
        private static DateTime _seenWrite = DateTime.MinValue;

        // Client → server gambler-win relay (kg "KGmarket CustomWebhooks" parity): the winner's client
        // sends (name, count, item display name); the server formats + posts. Idempotent registration,
        // instance-tracked like the economy networks.
        public const string GamblerWinRpc = "Fires_GamblerWin";
        private static bool _rpcRegistered;
        private static ZRoutedRpc _rpcRegisteredOn;

        public static void EnsureRegistered()
        {
            var current = ZRoutedRpc.instance;
            if (current == null) return;
            if (_rpcRegistered && _rpcRegisteredOn == current) return;
            current.Register<ZPackage>(GamblerWinRpc, new Action<long, ZPackage>(RPC_GamblerWin));
            _rpcRegistered = true;
            _rpcRegisteredOn = current;
        }

        private static void RPC_GamblerWin(long senderId, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;
            try
            {
                string name = pkg.ReadString();
                int count = pkg.ReadInt();
                string item = pkg.ReadString();
                if (!string.IsNullOrEmpty(name)) PostGamblerWin(name, count, item);
            }
            catch (Exception ex) { Debug.LogWarning($"[DiscordWebhooks] gambler-win RPC failed: {ex.Message}"); }
        }

        public static void Reload() { _loaded = false; EnsureLoaded(); }

        public static void PostMarketplaceList(string seller, int qty, string item, int price, string currency)
            => Post("Marketplace", seller, qty, item, price, currency);

        public static void PostGamblerWin(string player, int count, string item)
            => Post("Gambler", player, count, item);

        public static void PostQuestFinished(string player, string questName)
            => Post("Quest", player, questName);

        private static void Post(string kind, params object[] args)
        {
            try
            {
                EnsureLoaded();
                if (!_hooks.TryGetValue(kind, out var hook)) return;
                if (string.IsNullOrWhiteSpace(hook.Link) || !Uri.TryCreate(hook.Link, UriKind.Absolute, out _)) return;

                string text;
                try { text = string.Format(hook.Message, args); }
                catch { text = hook.Message; }
                string link = hook.Link; string title = hook.Title;
                // kg posts async (Task.Run) — never block the server thread on Discord.
                Task.Run(() =>
                {
                    try
                    {
                        string json = "{\"username\":\"" + Escape(title) + "\",\"content\":\"" + Escape(text) + "\"}";
                        var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(link);
                        request.Method = "POST";
                        request.ContentType = "application/json";
                        request.Timeout = 5000;
                        byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
                        request.ContentLength = data.Length;
                        using (var stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
                        request.GetResponse().Close();
                    }
                    catch (Exception ex) { Debug.LogWarning($"[DiscordWebhooks] {kind} POST failed: {ex.Message}"); }
                });
            }
            catch (Exception ex) { Debug.LogWarning($"[DiscordWebhooks] {kind} failed: {ex.Message}"); }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        private static void EnsureLoaded()
        {
            if (_loaded && Time.unscaledTime >= _nextStaleCheck)
            {
                _nextStaleCheck = Time.unscaledTime + 5f;
                try { if (File.Exists(CfgPath) && File.GetLastWriteTimeUtc(CfgPath) > _seenWrite) _loaded = false; } catch { }
            }
            if (_loaded) return;
            _loaded = true;
            _nextStaleCheck = Time.unscaledTime + 5f;

            try
            {
                if (!File.Exists(CfgPath)) return;
                _seenWrite = File.GetLastWriteTimeUtc(CfgPath);
                foreach (var rawLine in File.ReadAllLines(CfgPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    // kg key shapes: "<Kind> Webhook Link" / "<Kind> Webhook Message" / "<Kind> Webhook Title"
                    foreach (var kind in _hooks.Keys)
                    {
                        if (key.Equals(kind + " Webhook Link", StringComparison.OrdinalIgnoreCase)) _hooks[kind].Link = val;
                        else if (key.Equals(kind + " Webhook Message", StringComparison.OrdinalIgnoreCase)) _hooks[kind].Message = val;
                        else if (key.Equals(kind + " Webhook Title", StringComparison.OrdinalIgnoreCase)) _hooks[kind].Title = val;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[DiscordWebhooks] read failed: {ex.Message}"); }
        }
    }
}
