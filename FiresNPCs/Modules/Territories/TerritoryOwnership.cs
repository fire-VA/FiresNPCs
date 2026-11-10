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
        // Server -> the buyer when a grant is refused (name, message); the buyer's TerritoryPurchase refunds the Coins it
        // took for that territory. Older clients never get it unless refused.
        private const string RpcRefused = "FiresRPGmaker_Territory_GrantRefused";
        private const string RpcBuyBank   = "FiresRPGmaker_Territory_BuyBank";     // client -> server (name): buy, bank only
        private const string RpcPaidByBank = "FiresRPGmaker_Territory_PaidByBank"; // server -> buyer (name): bank paid, return bag coins
        private const string RpcReward    = "FiresRPGmaker_Territory_GrantReward"; // client -> server (name): dialogue reward, free

        // territory NAME (OrdinalIgnoreCase) -> set of owner Steam IDs
        private static readonly Dictionary<string, HashSet<string>> _owners =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static ZRoutedRpc s_regOn;   // re-register RPCs per connection

        private static string SidecarPath => Path.Combine(FiresCore.Storage.FiresConfigPaths.Territories, "territory_owners.json");

        // ── RPC registration + client pull on spawn ───────────────────────────
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class RegisterPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                // re-register RPCs per connection: guard by the ZRoutedRpc instance, not a process-wide bool. The bool was never
                // reset, so a client that left and rejoined had no OwnerSync handler and every grant was dropped.
                if (!global::FiresRPGmaker.Managers.RpcRegistration.Needed(s_regOn)) return;
                s_regOn = ZRoutedRpc.instance;
                const string M = "TerritoryOwnership";
                int n = 0;
                n += global::FiresRPGmaker.Managers.RpcRegistration.Try(M, RpcGrant, () => ZRoutedRpc.instance.Register<string, bool>(RpcGrant, new Action<long, string, bool>(OnGrantRpc)));
                n += global::FiresRPGmaker.Managers.RpcRegistration.Try(M, RpcSync, () => ZRoutedRpc.instance.Register<ZPackage>(RpcSync, new Action<long, ZPackage>(OnSyncRpc)));
                n += global::FiresRPGmaker.Managers.RpcRegistration.Try(M, RpcRequest, () => ZRoutedRpc.instance.Register(RpcRequest, new Action<long>(OnRequestRpc)));
                n += global::FiresRPGmaker.Managers.RpcRegistration.Try(M, RpcRefused, () => ZRoutedRpc.instance.Register<string, string>(RpcRefused, new Action<long, string, string>(OnRefusedRpc)));
                n += global::FiresRPGmaker.Managers.RpcRegistration.Try(M, RpcBuyBank, () => ZRoutedRpc.instance.Register<string>(RpcBuyBank, new Action<long, string>(OnBuyBankRpc)));
                n += global::FiresRPGmaker.Managers.RpcRegistration.Try(M, RpcPaidByBank, () => ZRoutedRpc.instance.Register<string>(RpcPaidByBank, new Action<long, string>(OnPaidByBankRpc)));
                n += global::FiresRPGmaker.Managers.RpcRegistration.Try(M, RpcReward, () => ZRoutedRpc.instance.Register<string>(RpcReward, new Action<long, string>(OnRewardRpc)));
                global::FiresRPGmaker.Managers.RpcRegistration.Logged(M, n);
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
                try { if (ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcRequest), RpcRequest); } catch { }
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

        /// <summary>Plots are priced in this bank currency (the same prefab TerritoryPurchase names in its popup).</summary>
        internal const string PlotCurrency = "Coins";

        // The plot's price: the highest SellPrice over its ForSale areas (one name = one territory).
        private static int ForSalePrice(string name)
        {
            int price = 0;
            foreach (var t in TerritoryLedger.Territories)
                if (t != null && t.Type == TerritoryType.ForSale && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                    price = Mathf.Max(price, t.SellPrice);
            return price;
        }

        // Withdraw exactly `price` from the sender's bank (server-derived owner key), all or nothing.
        private static bool TryChargeBank(long sender, int price, out string why)
        {
            why = "";
            global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.EnsureVaultConfigured();   // same guard as the Banker
            string owner = global::FiresCore.Identity.OwnerKeys.ResolveEconomyOwner(sender, 0L);
            if (string.IsNullOrEmpty(owner) || owner == "0") { why = "no economy owner for the sender"; return false; }
            int hash = PlotCurrency.GetStableHashCode();
            int got = global::FiresCore.Storage.BankRepository.Withdraw(owner, hash, price);
            if (got >= price) return true;
            if (got > 0) global::FiresCore.Storage.BankRepository.Deposit(owner, hash, got);   // partial = put it back
            why = $"bank holds {got} of {price}";
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
        public static void RequestGrant(string territoryName, bool grant) => RequestGrant(territoryName, grant, null);

        /// <summary>Console grant = target id: <paramref name="targetSteamId"/> (a Steam ID, or an online player's
        /// name) names the grantee explicitly - a server-console / admin grant. Null = the requesting player (buy / dialogue).
        /// From a client an explicit target goes through the admin-gated owners RPC (TerritoryAdminEdit) - the grant RPC's
        /// (string, bool) wire shape must not change.</summary>
        public static void RequestGrant(string territoryName, bool grant, string targetSteamId)
        {
            if (string.IsNullOrEmpty(territoryName) || ZRoutedRpc.instance == null) return;
            bool hasTarget = !string.IsNullOrWhiteSpace(targetSteamId);
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                ApplyGrant(0L, territoryName, grant, hasTarget ? targetSteamId.Trim() : null, hasTarget ? "console" : "buy");
                return;
            }
            if (hasTarget) { Paint.TerritoryAdminEdit.RequestOwners(grant ? "add" : "remove", territoryName.Trim(), targetSteamId.Trim()); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcGrant), RpcGrant, territoryName.Trim(), grant);   // client->server RPCs target the server
        }

        private static void OnGrantRpc(long sender, string territoryName, bool grant) => ApplyGrant(sender, territoryName, grant, null, "buy");

        // Bank-only buy (the client took no bag coins): the server charges the bank or refuses.
        private static void OnBuyBankRpc(long sender, string territoryName) => ApplyGrant(sender, territoryName, true, null, "buy", bankOnly: true);

        // Dialogue reward (GiveTerritory): free, under the pre-0.0.762 rules (non-admins: ForSale, not owned, claim limit).
        private static void OnRewardRpc(long sender, string territoryName) => ApplyGrant(sender, territoryName, true, null, "reward", reward: true);

        /// <summary>Dialogue GiveTerritory: a free grant to the talking player (never charged; ForSale-gated for non-admins).</summary>
        public static void RequestRewardGrant(string territoryName)
        {
            if (string.IsNullOrEmpty(territoryName) || ZRoutedRpc.instance == null) return;
            territoryName = territoryName.Trim();
            if (ZNet.instance != null && ZNet.instance.IsServer()) { ApplyGrant(0L, territoryName, true, null, "reward", reward: true); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcReward), RpcReward, territoryName);
        }

        /// <summary>Map purchase: <paramref name="bagPaid"/> = the client already took the price from its bag (classic
        /// grant RPC; the server still tries the bank first and then tells the client to return its bag coins).
        /// False = bank only.</summary>
        internal static void RequestBuy(string territoryName, bool bagPaid)
        {
            if (string.IsNullOrEmpty(territoryName) || ZRoutedRpc.instance == null) return;
            territoryName = territoryName.Trim();
            if (ZNet.instance != null && ZNet.instance.IsServer()) { ApplyGrant(0L, territoryName, true, null, "buy", bankOnly: !bagPaid); return; }
            if (bagPaid) ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcGrant), RpcGrant, territoryName, true);
            else ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcBuyBank), RpcBuyBank, territoryName);
        }

        private const float BuyMinIntervalSec = 0.5f;   // per-sender plot-buy rate limit
        private static readonly Dictionary<long, float> _lastBuyAt = new Dictionary<long, float>();

        private static void NotifyPaidByBank(long sender, string territoryName)
        {
            if (IsLocalSender(sender)) { if (Player.m_localPlayer != null) TerritoryPurchase.OnPaidByBank(territoryName); return; }
            try { ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcPaidByBank, territoryName ?? ""); } catch { }
        }

        private static void OnPaidByBankRpc(long sender, string territoryName)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;
            if (!DialogueSyncRPC.IsFromServer(sender)) { Debug.LogWarning($"[Territory] PaidByBank ignored: sender {sender} is not the server"); return; }
            TerritoryPurchase.OnPaidByBank(territoryName);
        }

        /// <summary>Console grant = target id: test hook - the last grant/release the server applied or refused
        /// ("granted|released|refused name=.. steam=.. by=console|admin|buy reason=..").</summary>
        public static string TestLastGrant = "";

        /// <summary>Console grant = target id: true when <paramref name="sender"/> is this process itself (0L or our
        /// own routed-RPC id). Holds on a listen host AND on a dedicated server (where it means the server console).</summary>
        internal static bool IsLocalSender(long sender)
        {
            if (sender == 0L) return true;
            try
            {
                if (sender == ZDOMan.GetSessionID()) return true;
                if (ZRoutedRpc.instance != null && ZNet.instance != null && ZNet.instance.IsServer() && sender == ZRoutedRpc.instance.GetServerPeerID()) return true;
            }
            catch { }
            return false;
        }

        private static void ApplyGrant(long sender, string territoryName, bool grant, string explicitTarget, string by, bool bankOnly = false, bool reward = false)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                territoryName = (territoryName ?? "").Trim();
                // Console grant = target id: an explicit target (console/admin) is the grantee; otherwise the sender. A local
                // sender on a dedicated server has no Steam ID (GetLocalSteamId is empty there) - refuse loudly.
                string who = explicitTarget;
                string ownerId = !string.IsNullOrEmpty(explicitTarget)
                    ? Paint.TerritoryAdminEdit.ResolveTargetSteamId(explicitTarget, out who)
                    : ResolveSenderSteamId(sender);
                if (string.IsNullOrEmpty(ownerId) || ownerId == "0")
                {
                    string why = !string.IsNullOrEmpty(explicitTarget)
                        ? $"target '{explicitTarget}' is not an online player or a Steam ID"
                        : (IsLocalSender(sender) ? "local sender has no Steam ID (dedicated server console? pass the target player)" : $"peer {sender} has no Steam ID");
                    Debug.LogWarning($"[Territory] grant for '{territoryName}' refused: {why}");
                    TestLastGrant = $"refused name={territoryName} steam= by={by} reason={why}";
                    return;
                }
                bool isAdmin = IsLocalSender(sender) || AdminSyncing.IsAdmin(sender);
                if (!string.IsNullOrEmpty(explicitTarget) && !isAdmin)
                {
                    Debug.LogWarning($"[Territory] grant for '{territoryName}' to {ownerId} refused: explicit target needs an admin (peer {sender})");
                    TestLastGrant = $"refused name={territoryName} steam={ownerId} by={by} reason=not admin";
                    return;
                }
                if (string.IsNullOrEmpty(explicitTarget)) { try { who = IsLocalSender(sender) ? Player.m_localPlayer?.GetPlayerName() : ZNet.instance.GetPeer(sender)?.m_playerName; } catch { } }
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] grant for {(string.IsNullOrEmpty(who) ? "?" : who)} steam {ownerId} by {by}");   // Console grant = target id
                TestLastGrant = $"pending name={territoryName} steam={ownerId} by={by}";
                int ownersBefore = OwnersOf(territoryName).Count;   // ledger seals: cleared on release / a new owner

                if (grant)
                {
                    if (!TerritoryExists(territoryName)) { TestLastGrant = $"refused name={territoryName} steam={ownerId} by={by} reason=no such territory"; Debug.LogWarning($"[Territory] grant for '{territoryName}' refused: no such territory"); return; }
                    if (!isAdmin && !IsForSale(territoryName))
                    {
                        Debug.LogWarning($"[Territory] {ownerId} tried to acquire '{territoryName}' (not ForSale) — denied");
                        Refuse(sender, territoryName, $"{territoryName} is not for sale.");   // Refunds the buyer
                        return;
                    }
                    // A plot sells once (ForSale=<coins>); admins can still add owners from the admin editor.
                    if (!isAdmin && _owners.TryGetValue(territoryName, out var current) && current.Count > 0 && !current.Contains(ownerId))
                    {
                        Debug.LogWarning($"[Territory] {ownerId} tried to buy '{territoryName}', already owned — denied");
                        Refuse(sender, territoryName, $"{territoryName} is already owned.");   // Refunds the buyer
                        return;
                    }
                    // Claim limit, server-side. Only a NEW ownership counts.
                    if (!IsOwner(territoryName, ownerId) && ClaimLimitReached(ownerId, out int owned, out int limit))
                    {
                        if (!isAdmin)
                        {
                            Debug.LogWarning($"[Territory] {ownerId} tried to claim '{territoryName}' at the claim limit ({owned}/{limit}) — denied");
                            Refuse(sender, territoryName, $"You already own {owned} of {limit} territories allowed on this server.");
                            return;
                        }
                        Debug.LogWarning($"[Territory] player {ownerId} is at the claim limit ({limit}); admin grant of '{territoryName}' allowed");
                    }
                    // Security (audit 10-23): a player buying a ForSale plot for THEMSELVES pays its SellPrice on the
                    // server, from their Banker balance (the only currency the server holds). The old client-side coin
                    // take was unverifiable (a modded client got plots free). Explicit-target console/admin grants stay free.
                    // Fire 10-23: bank FIRST (server-side, price from the server's territory data), else BAG (the client
                    // took the coins from its inventory before sending the classic grant RPC; unverifiable, so rate-limited).
                    // Only purchases (TerritoryPurchase.RequestBuy / the classic RPC) are charged; a dialogue reward is free.
                    int price = !reward && string.IsNullOrEmpty(explicitTarget) && !IsOwner(territoryName, ownerId) ? ForSalePrice(territoryName) : 0;
                    if (price > 0)
                    {
                        float now = Time.realtimeSinceStartup;
                        if (!IsLocalSender(sender) && _lastBuyAt.TryGetValue(sender, out float last) && now - last < BuyMinIntervalSec)
                        {
                            Debug.LogWarning($"[Territory] {ownerId} (peer {sender}) buy of '{territoryName}' refused: rate limited");
                            Refuse(sender, territoryName, "Too fast — try again.");   // refunds the bag coins
                            return;
                        }
                        bool byBank = TryChargeBank(sender, price, out string payWhy);
                        if (!byBank && bankOnly)
                        {
                            Debug.LogWarning($"[Territory] {ownerId} (peer {sender}) tried to buy '{territoryName}' for {price} {PlotCurrency}: {payWhy}, no bag payment — denied");
                            Refuse(sender, territoryName, $"You need {price} {PlotCurrency} in your bank or bag.");
                            return;
                        }
                        // Prune by age: entries older than the interval no longer limit anything.
                        foreach (var k in _lastBuyAt.Where(kv => now - kv.Value >= BuyMinIntervalSec).Select(kv => kv.Key).ToList()) _lastBuyAt.Remove(k);
                        _lastBuyAt[sender] = now;
                        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] buy '{territoryName}' for {price} {PlotCurrency} by {ownerId} (peer {sender}) path={(byBank ? "bank" : "bag")}");
                        if (byBank && !bankOnly) NotifyPaidByBank(sender, territoryName);   // the client returns its bag coins
                    }
                    if (!_owners.TryGetValue(territoryName, out var set)) { set = new HashSet<string>(); _owners[territoryName] = set; }
                    set.Add(ownerId);
                    global::FiresRPGmaker.FiresLogger.LogDebug($"[Territory] granted '{territoryName}' to {ownerId}");
                    TestLastGrant = $"granted name={territoryName} steam={ownerId} by={by}";   // Console grant = target id
                }
                else
                {
                    if (_owners.TryGetValue(territoryName, out var set))
                    {
                        set.Remove(ownerId);
                        if (set.Count == 0) _owners.Remove(territoryName);
                    }
                    global::FiresRPGmaker.FiresLogger.LogDebug($"[Territory] released '{territoryName}' from {ownerId}");
                    TestLastGrant = $"released name={territoryName} steam={ownerId} by={by}";   // Console grant = target id
                }
                TerritorySeals.OnOwnersChanged(territoryName, ownersBefore, OwnersOf(territoryName).Count);
                SaveSidecar();
                BroadcastSync();
            }
            catch (Exception ex) { Debug.LogWarning($"[Territory] grant error: {ex.Message}"); }
        }

        // ── admin edits ─────────────────────────────
        public static IReadOnlyCollection<string> OwnersOf(string territoryName)
            => !string.IsNullOrEmpty(territoryName) && _owners.TryGetValue(territoryName, out var set) ? (IReadOnlyCollection<string>)set : Array.Empty<string>();

        internal static int AdminRename(string oldName, string newName)
        {
            if (!_owners.TryGetValue(oldName, out var set)) { TerritorySeals.OnRenamed(oldName, newName); return 0; }
            _owners.Remove(oldName);
            if (!_owners.TryGetValue(newName, out var into)) { into = new HashSet<string>(); _owners[newName] = into; }
            into.UnionWith(set);
            TerritorySeals.OnRenamed(oldName, newName);
            SaveSidecar(); BroadcastSync();
            return set.Count;
        }

        internal static void AdminClear(string name)
        {
            int before = OwnersOf(name).Count;
            _owners.Remove(name);
            TerritorySeals.OnOwnersChanged(name, before, 0);
            SaveSidecar(); BroadcastSync();
        }

        internal static void AdminAdd(string name, string steamId)
        {
            // Admins may exceed the claim limit; it is logged.
            if (!IsOwner(name, steamId) && ClaimLimitReached(steamId, out _, out int limit))
                Debug.LogWarning($"[Territory] player {steamId} is at the claim limit ({limit}); admin added them to '{name}'");
            int before = OwnersOf(name).Count;
            if (!_owners.TryGetValue(name, out var set)) { set = new HashSet<string>(); _owners[name] = set; }
            set.Add(steamId);
            TerritorySeals.OnOwnersChanged(name, before, set.Count);
            SaveSidecar(); BroadcastSync();
        }

        // An admin removes one owner (the admin editor / territory_owner remove).
        internal static bool AdminRemove(string name, string steamId)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(steamId) || !_owners.TryGetValue(name, out var set) || !set.Remove(steamId)) return false;
            if (set.Count == 0) _owners.Remove(name);
            TerritorySeals.OnOwnersChanged(name, set.Count + 1, set.Count);
            SaveSidecar(); BroadcastSync();
            return true;
        }

        /// <summary>The sender's Steam ID as the server sees it (the owner check).</summary>
        internal static string SenderSteamId(long sender)
            => IsLocalSender(sender) ? EwpDataBridge.GetLocalSteamId() : ResolveSenderSteamId(sender);   // Console grant = target id

        // ── Claim limit ───────────────────────────────────────────────────────────
        /// <summary>[Territories] Max territories per player (server-synced); 0 = unlimited.</summary>
        internal static int MaxPerPlayer()
        {
            try { return Mathf.Max(0, global::FiresRPGmaker.Managers.ConfigManager.Instance?.configMaxTerritoriesPerPlayer?.Value ?? 0); }
            catch { return 0; }
        }

        /// <summary>Territory NAMES this Steam ID owns (one name = one territory, however many areas share it).</summary>
        internal static int CountOwnedBy(string steamId)
        {
            if (string.IsNullOrEmpty(steamId)) return 0;
            int n = 0;
            foreach (var kv in _owners) if (kv.Value != null && kv.Value.Contains(steamId)) n++;
            return n;
        }

        internal static bool ClaimLimitReached(string steamId, out int owned, out int limit)
        {
            limit = MaxPerPlayer();
            owned = CountOwnedBy(steamId);
            return limit > 0 && owned >= limit;
        }

        /// <summary>Test hook: the claim-limit result for a Steam ID ("owned/limit reached=bool").</summary>
        internal static string TestClaimLimit(string steamId)
        {
            bool reached = ClaimLimitReached(steamId, out int owned, out int limit);
            return $"{owned}/{limit} reached={reached}";
        }

        private static void Refuse(long sender, string territoryName, string msg)
        {
            global::FiresRPGmaker.FiresLogger.LogDebug($"[Territory] grant refused for '{territoryName}': {msg}");
            TestLastGrant = $"refused name={territoryName} steam= by=? reason={msg}";   // Console grant = target id
            // Console grant = target id: "local" = 0L or our own id (listen host AND dedicated server); refund only when a
            // local player exists - a dedicated server console has no purchase to refund.
            if (IsLocalSender(sender)) { if (Player.m_localPlayer != null) TerritoryPurchase.OnGrantRefused(territoryName, msg); return; }
            try { ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcRefused, territoryName ?? "", msg ?? ""); } catch { }
        }

        private static void OnRefusedRpc(long sender, string territoryName, string msg)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;   // only the server sends it
            TerritoryPurchase.OnGrantRefused(territoryName, msg);
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
            try
            {
                DeserializeMap(pkg);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] OwnerSync received: {_owners.Count} territories");   // re-register RPCs per connection
            }
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
            if (IsLocalSender(sender)) return EwpDataBridge.GetLocalSteamId();   // host / local (empty on a dedicated server - ApplyGrant refuses it)
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
