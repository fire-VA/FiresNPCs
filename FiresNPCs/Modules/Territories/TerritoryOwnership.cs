using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using FiresRPGmaker.Modules.Dialogues;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>Name-keyed territory ownership: all config blocks sharing a Name are one logical territory, so
    /// granting "PlotA" covers every PlotA area. Owner identity = stable Steam ID. Server-authoritative: the
    /// server owns the store, persists it to a sidecar JSON (survives reload/restart — the per-area Territory.Owner
    /// does not), and syncs it to clients (the build-permission check runs client-side). Non-admins can only
    /// acquire a territory whose Type is ForSale; admins (and the host) may grant any. Releasing your OWN
    /// ownership is always allowed.</summary>
    public static class TerritoryOwnership
    {
        private const string RpcGrant   = "FiresRPGmaker_Territory_GrantOwner";    // client -> server (name, grant)
        private const string RpcSync    = "FiresRPGmaker_Territory_OwnerSync";       // server -> client (json map)
        private const string RpcRequest = "FiresRPGmaker_Territory_OwnerRequest";    // client -> server (pull map)

        // territory NAME (OrdinalIgnoreCase) -> set of owner Steam IDs
        private static readonly Dictionary<string, HashSet<string>> _owners =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static bool _rpcRegistered;

        private static string SidecarPath => Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories, "territory_owners.json");

        // ── RPC registration + client pull on spawn ───────────────────────────
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class RegisterPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (_rpcRegistered || ZRoutedRpc.instance == null) return;
                _rpcRegistered = true;
                ZRoutedRpc.instance.Register<string, bool>(RpcGrant, new Action<long, string, bool>(OnGrantRpc));
                ZRoutedRpc.instance.Register<ZPackage>(RpcSync, new Action<long, ZPackage>(OnSyncRpc));
                ZRoutedRpc.instance.Register(RpcRequest, new Action<long>(OnRequestRpc));
                if (ZNet.instance != null && ZNet.instance.IsServer()) LoadSidecar();
            }
        }

        [HarmonyPatch(typeof(Player), "OnSpawned")]
        private static class RequestOnSpawn
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || ZNet.instance == null) return;
                if (ZNet.instance.IsServer()) { LoadSidecar(); return; }   // host already holds the truth
                try { if (ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcRequest); } catch { }
            }
        }

        // ── queries (used by the build-permission check + the editor dropdown) ─
        public static bool IsOwner(string territoryName, string steamId)
        {
            if (string.IsNullOrEmpty(territoryName) || string.IsNullOrEmpty(steamId) || steamId == "0") return false;
            return _owners.TryGetValue(territoryName, out var set) && set.Contains(steamId);
        }

        public static IEnumerable<string> AllTerritoryNames()
        {
            IEnumerable<Territory> src = (ZNet.instance != null && ZNet.instance.IsServer())
                ? (IEnumerable<Territory>)TerritoryLedger.Territories
                : TerritorySyncedState.ClientTerritories;
            return (src ?? Enumerable.Empty<Territory>())
                   .Where(t => t != null && !string.IsNullOrEmpty(t.Name))
                   .Select(t => t.Name)
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsForSale(string name)
        {
            foreach (var t in TerritoryLedger.Territories)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) && t.Type == TerritoryType.ForSale)
                    return true;
            return false;
        }

        private static bool TerritoryExists(string name)
        {
            foreach (var t in TerritoryLedger.Territories)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ── grant / remove (the dialogue command entry point) ──────────────────
        /// <summary>Request that the talking player gain (grant=true) or release (grant=false) ownership of a
        /// named territory. Routes to the server, which resolves the requester's Steam ID authoritatively.</summary>
        public static void RequestGrant(string territoryName, bool grant)
        {
            if (string.IsNullOrEmpty(territoryName) || ZRoutedRpc.instance == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer()) { ApplyGrant(0L, territoryName, grant); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcGrant, territoryName.Trim(), grant);
        }

        private static void OnGrantRpc(long sender, string territoryName, bool grant) => ApplyGrant(sender, territoryName, grant);

        private static void ApplyGrant(long sender, string territoryName, bool grant)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                territoryName = (territoryName ?? "").Trim();
                string ownerId = ResolveSenderSteamId(sender);
                if (string.IsNullOrEmpty(ownerId) || ownerId == "0") return;
                bool isAdmin = sender == 0L || AdminSyncing.IsAdmin(sender);

                if (grant)
                {
                    if (!TerritoryExists(territoryName)) return;
                    if (!isAdmin && !IsForSale(territoryName))
                    {
                        Debug.LogWarning($"[Territory] {ownerId} tried to acquire '{territoryName}' (not ForSale) — denied");
                        return;
                    }
                    // A plot sells once (ForSale=<coins>, 10-08); admins can still add owners from the admin editor.
                    if (!isAdmin && _owners.TryGetValue(territoryName, out var current) && current.Count > 0 && !current.Contains(ownerId))
                    {
                        Debug.LogWarning($"[Territory] {ownerId} tried to buy '{territoryName}', already owned — denied");
                        return;
                    }
                    if (!_owners.TryGetValue(territoryName, out var set)) { set = new HashSet<string>(); _owners[territoryName] = set; }
                    set.Add(ownerId);
                    Debug.Log($"[Territory] granted '{territoryName}' to {ownerId}");
                }
                else
                {
                    if (_owners.TryGetValue(territoryName, out var set))
                    {
                        set.Remove(ownerId);
                        if (set.Count == 0) _owners.Remove(territoryName);
                    }
                    Debug.Log($"[Territory] released '{territoryName}' from {ownerId}");
                }
                SaveSidecar();
                BroadcastSync();
            }
            catch (Exception ex) { Debug.LogWarning($"[Territory] grant error: {ex.Message}"); }
        }

        // ── admin edits (0.0.510, TerritoryAdminEdit: the server, after its admin check) ─────────────────────────────
        public static IReadOnlyCollection<string> OwnersOf(string territoryName)
            => !string.IsNullOrEmpty(territoryName) && _owners.TryGetValue(territoryName, out var set) ? (IReadOnlyCollection<string>)set : Array.Empty<string>();

        internal static int AdminRename(string oldName, string newName)
        {
            if (!_owners.TryGetValue(oldName, out var set)) return 0;
            _owners.Remove(oldName);
            if (!_owners.TryGetValue(newName, out var into)) { into = new HashSet<string>(); _owners[newName] = into; }
            into.UnionWith(set);
            SaveSidecar(); BroadcastSync();
            return set.Count;
        }

        internal static void AdminClear(string name)
        {
            _owners.Remove(name);
            SaveSidecar(); BroadcastSync();
        }

        internal static void AdminAdd(string name, string steamId)
        {
            if (!_owners.TryGetValue(name, out var set)) { set = new HashSet<string>(); _owners[name] = set; }
            set.Add(steamId);
            SaveSidecar(); BroadcastSync();
        }

        // ── sync ────────────────────────────────────────────────────────────────
        private static void OnRequestRpc(long sender)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcSync, SerializeMap()); } catch { }
        }

        private static void BroadcastSync()
        {
            if (ZRoutedRpc.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var peers = ZNet.instance.GetPeers();
            if (peers == null) return;
            foreach (var peer in peers)
                if (peer != null && peer.IsReady())
                    try { ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, RpcSync, SerializeMap()); } catch { }
        }

        private static void OnSyncRpc(long sender, ZPackage pkg)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;   // server is the source of truth
            try { DeserializeMap(pkg); }
            catch (Exception ex) { Debug.LogWarning($"[Territory] owner sync recv error: {ex.Message}"); }
        }

        private static ZPackage SerializeMap()
        {
            var pkg = new ZPackage();
            pkg.Write(JsonConvert.SerializeObject(_owners.ToDictionary(kv => kv.Key, kv => kv.Value.ToList())));
            return pkg;
        }

        private static void DeserializeMap(ZPackage pkg)
        {
            var dict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(pkg.ReadString());
            _owners.Clear();
            if (dict != null)
                foreach (var kv in dict)
                    _owners[kv.Key] = new HashSet<string>(kv.Value);
        }

        // ── persistence ──────────────────────────────────────────────────────────
        private static void LoadSidecar()
        {
            try
            {
                if (!File.Exists(SidecarPath)) return;
                var dict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(SidecarPath));
                _owners.Clear();
                if (dict != null) foreach (var kv in dict) _owners[kv.Key] = new HashSet<string>(kv.Value);
            }
            catch (Exception ex) { Debug.LogWarning($"[Territory] owner load failed: {ex.Message}"); }
        }

        private static void SaveSidecar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SidecarPath));
                File.WriteAllText(SidecarPath, JsonConvert.SerializeObject(
                    _owners.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()), Formatting.Indented));
            }
            catch (Exception ex) { Debug.LogWarning($"[Territory] owner save failed: {ex.Message}"); }
        }

        private static string ResolveSenderSteamId(long sender)
        {
            if (sender == 0L) return EwpDataBridge.GetLocalSteamId();   // host / local
            try
            {
                var peer = ZNet.instance?.GetPeer(sender);
                string host = peer?.m_socket?.GetHostName();
                return string.IsNullOrEmpty(host) ? null : EwpDataBridge.StripPlatformPrefix(host);
            }
            catch { return null; }
        }
    }
}
