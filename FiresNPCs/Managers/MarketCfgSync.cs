using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace FiresRPGmaker.Managers
{
    /// <summary>
    /// [release] audit #1: the server's MarketPlace.cfg for the NPC book's Marketplace + Mail tabs. The file lives outside the
    /// synced cfg tree (config/Marketplace), so a dedi client never had it: the tabs read the admin's LOCAL file and Save pushed
    /// that (defaults) over the server's. An admin client now asks the server for its copy when a tab opens; Save edits that copy
    /// and pushes it back through SyncFiles ("MarketplaceCfg"). On a host / single player the disk file IS the server's.
    /// </summary>
    public static class MarketCfgSync
    {
        private const string RpcGet = "FiresRPGmaker_MarketCfgGet";
        private const string RpcReply = "FiresRPGmaker_MarketCfgReply";
        public static string DiskPath => Path.Combine(Paths.ConfigPath, "Marketplace", "MarketPlace.cfg");

        /// <summary>The server's text once received on a client (null until then); on a host it is read from disk each time.</summary>
        private static string s_serverText;
        public static bool Received => IsHost || s_serverText != null;
        public static event Action Arrived;

        private static bool IsHost => ZNet.instance == null || ZNet.instance.IsServer();

        /// <summary>From DialogueSyncRPC.Register (ZNet.Awake), on every host.</summary>
        public static void Register()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register(RpcGet, new Action<long, ZPackage>(RPC_Get));
            ZRoutedRpc.instance.Register(RpcReply, new Action<long, ZPackage>(RPC_Reply));
        }

        /// <summary>Client: ask the server for its copy (the tabs call this when they open).</summary>
        public static void Request()
        {
            if (IsHost || ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcGet, new ZPackage());
            Debug.Log("[MarketCfgSync] asked the server for MarketPlace.cfg");
        }

        /// <summary>The text to edit: the server's (client, once received) or the disk file (host). Null on a client still waiting.</summary>
        public static string Current()
        {
            if (!IsHost) return s_serverText;
            try { return File.Exists(DiskPath) ? File.ReadAllText(DiskPath) : ""; } catch { return ""; }
        }

        /// <summary>Save: the host writes its disk file (the KG plugin's watcher reloads it); a client pushes to the server and keeps the copy.</summary>
        public static void Save(string text)
        {
            if (IsHost)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DiskPath));
                File.WriteAllText(DiskPath, text);
                return;
            }
            s_serverText = text;
            FiresConfigSync.PushSavedConfig("MarketplaceCfg", "MarketPlace.cfg", text);
        }

        private static void RPC_Get(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!AdminSyncing.IsAdmin(sender)) { Debug.LogWarning($"[MarketCfgSync] non-admin {sender} asked for MarketPlace.cfg"); return; }
            var reply = new ZPackage();
            string text = "";
            try { if (File.Exists(DiskPath)) text = File.ReadAllText(DiskPath); } catch (Exception ex) { Debug.LogWarning("[MarketCfgSync] read: " + ex.Message); }
            reply.Write(text);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcReply, reply);
        }

        private static void RPC_Reply(long sender, ZPackage pkg)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;
            s_serverText = pkg.ReadString() ?? "";
            Debug.Log($"[MarketCfgSync] got the server's MarketPlace.cfg ({s_serverText.Length} chars)");
            try { Arrived?.Invoke(); } catch (Exception ex) { Debug.LogWarning("[MarketCfgSync] Arrived: " + ex.Message); }
        }

        // ── INI helpers on the text ([Main] key = value) ─────────────────────────────
        public static string Get(string text, string section, string key, string def)
        {
            if (string.IsNullOrEmpty(text)) return def;
            bool inSec = false;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) { inSec = string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase); continue; }
                if (!inSec || t.StartsWith("#") || t.StartsWith(";")) continue;
                int eq = t.IndexOf('=');
                if (eq > 0 && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) return t.Substring(eq + 1).Trim();
            }
            return def;
        }

        /// <summary>Sets key in [section], keeping every other line; adds the key (and the section) when missing.</summary>
        public static string Set(string text, string section, string key, string value)
        {
            var lines = new System.Collections.Generic.List<string>((text ?? "").Replace("\r", "").Split('\n'));
            int secStart = -1, secEnd = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (!(t.StartsWith("[") && t.EndsWith("]"))) continue;
                if (secStart >= 0) { secEnd = i; break; }
                if (string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase)) secStart = i;
            }
            string line = key + " = " + (value ?? "");
            if (secStart < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[" + section + "]"); lines.Add(line);
                return string.Join(Environment.NewLine, lines);
            }
            for (int i = secStart + 1; i < secEnd; i++)
            {
                string t = lines[i].Trim();
                int eq = t.IndexOf('=');
                if (t.StartsWith("#") || t.StartsWith(";") || eq <= 0) continue;
                if (string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) { lines[i] = line; return string.Join(Environment.NewLine, lines); }
            }
            int at = secEnd;
            while (at > secStart + 1 && lines[at - 1].Trim().Length == 0) at--;
            lines.Insert(at, line);
            return string.Join(Environment.NewLine, lines);
        }
    }
}
