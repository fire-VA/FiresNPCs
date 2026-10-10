using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Modules.Dialogues;

namespace FiresRPGmaker.Modules.Territories.Paint
{
    /// <summary>
    /// 0.0.510 (Fire 10-06): editing a territory edits EVERY block that shares its name - all files under Territories
    /// (drawn/, outlines/, legacy/, any file name) - and a rename carries the territory's owners to the new name. The
    /// rewrite runs on the server (a dedicated-server admin's client doesn't hold the files): the admin's client sends
    /// the edit, the server checks admin, rewrites the matching blocks' header / colour / flags (shapes, coords and the
    /// permitted line stay), reloads and rebroadcasts, then answers with what it changed. A host does it locally.
    /// Owner edits (clear / add by online player name or Steam ID) go the same way into TerritoryOwnership.
    /// </summary>
    public static class TerritoryAdminEdit
    {
        private const string RpcEdit = "FiresRPGmaker_Territory_AdminEdit";       // client -> server (old, new, colour, flags)
        private const string RpcOwners = "FiresRPGmaker_Territory_AdminOwners";   // client -> server (op, name, arg)
        private const string RpcResult = "FiresRPGmaker_Territory_AdminResult";   // server -> client (message)
        private static bool _registered;

        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class RegisterPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (_registered || ZRoutedRpc.instance == null) return;
                _registered = true;
                ZRoutedRpc.instance.Register<ZPackage>(RpcEdit, new Action<long, ZPackage>(OnEditRpc));
                ZRoutedRpc.instance.Register<string, string, string>(RpcOwners, new Action<long, string, string, string>(OnOwners));
                ZRoutedRpc.instance.Register<string>(RpcResult, new Action<long, string>((s, msg) => Hud(msg)));
            }
        }

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        private static class UnregisterPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => _registered = false;
        }

        // ── client side ──────────────────────────────────────────────────────────────────────────────────────────────
        public static void RequestEdit(string oldName, TerritoryBrush b)
        {
            string newName = string.IsNullOrWhiteSpace(b.Name) ? oldName : b.Name.Trim();
            string colour = $"{Mathf.Clamp(b.R, 0, 255)},{Mathf.Clamp(b.G, 0, 255)},{Mathf.Clamp(b.B, 0, 255)},{Mathf.Clamp(b.Alpha, 0, 255)},true";
            string flags = string.IsNullOrWhiteSpace(b.Flags) ? "None" : b.Flags.Trim();
            if (ZNet.instance != null && ZNet.instance.IsServer()) { Hud(ApplyEdit(oldName, newName, colour, flags)); return; }
            var pkg = new ZPackage(); pkg.Write(oldName ?? ""); pkg.Write(newName); pkg.Write(colour); pkg.Write(flags);
            ZRoutedRpc.instance?.InvokeRoutedRPC(0L, RpcEdit, pkg);
        }

        /// <summary>op: "clear" (no owners), "add" (arg = an online player's name or a Steam ID).</summary>
        public static void RequestOwners(string op, string name, string arg)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) { Hud(ApplyOwners(op, name, arg)); return; }
            ZRoutedRpc.instance?.InvokeRoutedRPC(0L, RpcOwners, op ?? "", name ?? "", arg ?? "");
        }

        /// <summary>How many map areas (blocks) the clients know under this name.</summary>
        public static int AreaCount(string name)
        {
            IEnumerable<Territory> src = ZNet.instance != null && ZNet.instance.IsServer()
                ? (IEnumerable<Territory>)TerritoryLedger.Territories : TerritorySyncedState.ClientTerritories;
            int n = 0;
            if (src != null) foreach (var t in src) if (t != null && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        // ── server side ──────────────────────────────────────────────────────────────────────────────────────────────
        private static void OnEditRpc(long sender, ZPackage pkg)
        {
            try { OnEdit(sender, pkg.ReadString(), pkg.ReadString(), pkg.ReadString(), pkg.ReadString()); }
            catch (Exception ex) { Debug.LogWarning("[Territory] edit rpc: " + ex.Message); }
        }

        private static void OnEdit(long sender, string oldName, string newName, string colour, string flags)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            string msg = IsAdminSender(sender) ? ApplyEdit(oldName, newName, colour, flags) : "Territory edit refused: not an admin";
            Reply(sender, msg);
        }

        private static void OnOwners(long sender, string op, string name, string arg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            string msg = IsAdminSender(sender) ? ApplyOwners(op, name, arg) : "Territory owners refused: not an admin";
            Reply(sender, msg);
        }

        private static bool IsAdminSender(long sender) => sender == 0L || sender == ZDOMan.GetSessionID() || AdminSyncing.IsAdmin(sender);

        private static void Reply(long sender, string msg)
        {
            Debug.Log("[Territory] " + msg);
            if (sender == 0L || sender == ZDOMan.GetSessionID()) { Hud(msg); return; }
            try { ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcResult, msg); } catch { }
        }

        internal static string ApplyEdit(string oldName, string newName, string colour, string flags)
        {
            oldName = (oldName ?? "").Trim();
            newName = string.IsNullOrWhiteSpace(newName) ? oldName : newName.Trim();
            if (oldName.Length == 0) return "Territory edit: no name";
            string root = FiresCore.Storage.FiresConfigPaths.Territories;
            if (!Directory.Exists(root)) return $"Territory edit: no Territories folder";
            int blocks = 0, files = 0;
            foreach (string full in Directory.GetFiles(root, "*.cfg", SearchOption.AllDirectories))
            {
                string[] lines;
                try { lines = File.ReadAllLines(full); } catch { continue; }
                var outp = new List<string>(lines.Length);
                int line = -1; bool match = false, changed = false;
                foreach (string raw in lines)
                {
                    string t = raw.Trim();
                    if (t.StartsWith("[") && t.EndsWith("]"))
                    {
                        match = string.Equals(t.Substring(1, t.Length - 2).Trim(), oldName, StringComparison.OrdinalIgnoreCase);
                        line = 0;
                        if (match) { outp.Add("[" + newName + "]"); blocks++; changed = true; continue; }
                        outp.Add(raw); continue;
                    }
                    if (line < 0 || !match) { outp.Add(raw); if (line >= 0) line++; continue; }
                    line++;
                    if (line == 3) outp.Add(colour);        // R,G,B,A,showWater
                    else if (line == 4) outp.Add(flags);    // flags
                    else outp.Add(raw);                     // shape / coords / permitted (and anything after) kept
                }
                if (!changed) continue;
                try { File.WriteAllText(full, string.Join("\n", outp) + "\n"); files++; }
                catch (Exception ex) { Debug.LogWarning($"[Territory] edit write failed for {full}: {ex.Message}"); }
            }
            if (blocks == 0) return $"No territory named '{oldName}' in the territory files";
            try { TerritoryLedger.Reload(); TerritorySyncedState.BroadcastTerritories(); } catch (Exception ex) { Debug.LogWarning("[Territory] reload after edit: " + ex.Message); }
            bool renamed = !string.Equals(oldName, newName, StringComparison.Ordinal);
            int moved = renamed ? TerritoryOwnership.AdminRename(oldName, newName) : 0;
            return $"Saved '{newName}': {blocks} area(s) in {files} file(s)" + (renamed ? $", renamed from '{oldName}'" + (moved > 0 ? $" ({moved} owner(s) moved)" : "") : "");
        }

        internal static string ApplyOwners(string op, string name, string arg)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "Territory owners: no name";
            switch ((op ?? "").ToLowerInvariant())
            {
                case "clear":
                    TerritoryOwnership.AdminClear(name);
                    return $"'{name}' has no owners now";
                case "add":
                    string id = ResolvePlayerId((arg ?? "").Trim(), out string who);
                    if (id == null) return $"No online player or Steam ID '{arg}'";
                    TerritoryOwnership.AdminAdd(name, id);
                    return $"'{name}' owner added: {who}";
                default:
                    return "Territory owners: unknown action " + op;
            }
        }

        // An online player's name (any case) → their Steam ID; a 17-digit number is taken as a Steam ID as it is.
        private static string ResolvePlayerId(string arg, out string who)
        {
            who = arg;
            if (arg.Length == 0) return null;
            if (arg.Length >= 15 && ulong.TryParse(arg, out _)) return arg;
            if (Player.m_localPlayer != null && string.Equals(Player.m_localPlayer.GetPlayerName(), arg, StringComparison.OrdinalIgnoreCase))
            { who = Player.m_localPlayer.GetPlayerName(); return EwpDataBridge.GetLocalSteamId(); }
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || !string.Equals(peer.m_playerName, arg, StringComparison.OrdinalIgnoreCase)) continue;
                string host = peer.m_socket?.GetHostName();
                if (string.IsNullOrEmpty(host)) return null;
                who = peer.m_playerName;
                return EwpDataBridge.StripPlatformPrefix(host);
            }
            return null;
        }

        private static void Hud(string msg)
        { try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, msg); } catch { } }
    }
}
