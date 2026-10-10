using System;
using System.Collections.Generic;
using UnityEngine;
using FiresCore.Storage;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Server-authoritative banker balances over Core's LiteDB <see cref="BankRepository"/> (per-world vault).
    ///
    /// PROTOCOL
    /// --------
    ///   FiresRPGmaker_BankReq  — client → server. Wrapper (long playerId, string playerName, byte[] inner);
    ///       inner = (byte op, string profile, [string currency, int amount]). The server owns the DB, caps,
    ///       and interest math; the wrapper carries the canonical m_playerID/name because peer UID diverges
    ///       from m_playerID on dedicated servers (same reason GuildSyncManager wraps its routes).
    ///   FiresRPGmaker_BankRes  — server → the requesting peer. (byte res, ...):
    ///       BalanceFull → the full accepted-currency map (sync).
    ///       BalanceOne  → one currency after a deposit/withdraw, carrying the item deltas the client must
    ///                     apply to its own inventory (grant on withdraw, refund on deposit-overflow).
    ///
    /// Balances persist in the vault the server already configures per-world (VaultOfKnowledge). A listen-server
    /// host is its own server, so the loopback route hits the same in-process handler → single-player persists too.
    /// Interest accrues server-side on GetBalance at the viewed profile's rate (× VIP multiplier), so it is
    /// authoritative and cannot be spoofed by client timing.
    /// </summary>
    public static class BankerNetwork
    {
        public const string LogPrefix = "[BankerNet]";

        private const string RpcReq = "FiresRPGmaker_BankReq";
        private const string RpcRes = "FiresRPGmaker_BankRes";

        private enum Op  : byte { GetBalance = 1, Deposit = 2, Withdraw = 3 }
        private enum Res : byte { BalanceFull = 1, BalanceOne = 2 }

        // Client-side result hook. The open BankerPlayerPanel subscribes; the arg is a status line to show
        // (empty for a pure sync). Fired after the local cache is updated so a UI refresh reads fresh numbers.
        public static Action<string> OnResult;

        private static bool _registered;
        private static ZRoutedRpc _registeredOn;

        // Server-side interest clock: ownerId → last UtcTicks we accrued interest for them.
        // 0.0.558 (lead/Fire: weekly interest never paid on a server that restarts daily): persisted per world in
        // BepInEx/config/FiresRPGmaker/Bankers/interest_clock_<world>.txt ("ownerId<TAB>ticks" lines; not a .cfg, so the
        // config push never ships it to clients). Loaded on first use, written after every change.
        private static readonly Dictionary<string, long> _lastInterest = new Dictionary<string, long>();
        private static string _clockWorld;

        private static string ClockPath(string world) =>
            System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "FiresRPGmaker", "Bankers", "interest_clock_" + string.Join("_", (world ?? "world").Split(System.IO.Path.GetInvalidFileNameChars())) + ".txt");

        private static void EnsureClockLoaded()
        {
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : null;
            if (string.IsNullOrEmpty(world) || world == _clockWorld) return;
            _clockWorld = world;
            _lastInterest.Clear();
            try
            {
                string path = ClockPath(world);
                if (!System.IO.File.Exists(path)) return;
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    var p = line.Split('\t');
                    if (p.Length == 2 && long.TryParse(p[1], out long t)) _lastInterest[p[0]] = t;
                }
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Banker] interest clock loaded: {_lastInterest.Count} owner(s) for world '{world}'");
            }
            catch (Exception ex) { Debug.LogWarning($"[Banker] interest clock load failed: {ex.Message}"); }
        }

        private static void SaveClock()
        {
            if (string.IsNullOrEmpty(_clockWorld)) return;
            try
            {
                string path = ClockPath(_clockWorld);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                var sb = new System.Text.StringBuilder();
                foreach (var kv in _lastInterest) sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
                System.IO.File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex) { Debug.LogWarning($"[Banker] interest clock save failed: {ex.Message}"); }
        }

        // ── Registration (idempotent; mirrors GuildSyncManager instance-tracking) ─────────────────────────
        public static void Initialize()
        {
            var current = ZRoutedRpc.instance;
            if (current == null) return;
            if (_registered && _registeredOn == current) return;

            current.Register<ZPackage>(RpcReq, new Action<long, ZPackage>(RPC_BankReq));
            current.Register<ZPackage>(RpcRes, new Action<long, ZPackage>(RPC_BankRes));
            _registered   = true;
            _registeredOn = current;

            // New instance = world load / server change. Drop the client cache so one server's balances never
            // bleed onto another on a shared r2modman profile.
            BankerBalanceStore.ClearCache();
            FiresLogger.LogVerbose($"{LogPrefix} Registered on ZRoutedRpc {current.GetHashCode()}.");
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // ── Client → server sends ─────────────────────────────────────────────────────────────────────────
        public static void RequestBalance(string profile) => Send(Op.GetBalance, profile, null, 0);
        public static void RequestDeposit(string profile, string currency, int amount) => Send(Op.Deposit, profile, currency, amount);
        public static void RequestWithdraw(string profile, string currency, int amount) => Send(Op.Withdraw, profile, currency, amount);

        private static void Send(Op op, string profile, string currency, int amount)
        {
            if (ZRoutedRpc.instance == null) return;
            var local = Player.m_localPlayer;
            long playerId = local != null ? local.GetPlayerID() : 0L;
            if (playerId == 0L) return; // not fully spawned yet — panel re-polls on its refresh tick

            var inner = new ZPackage();
            inner.Write((byte)op);
            inner.Write(profile ?? "");
            if (op != Op.GetBalance)
            {
                inner.Write(currency ?? "");
                inner.Write(amount);
            }

            var wrapped = new ZPackage();
            wrapped.Write(playerId);
            wrapped.Write(local != null ? (local.GetPlayerName() ?? "") : "");
            wrapped.Write(inner.GetArray());
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcReq, wrapped);
        }

        // ── Server handler ────────────────────────────────────────────────────────────────────────────────
        private static void RPC_BankReq(long senderId, ZPackage pkg)
        {
            if (!IsServer || pkg == null) return;
            // Guarantee the shared Core vault is configured before ANY balance read/write. If a server-init step
            // threw before VaultOfKnowledge configured it, every Open() would throw and deposits would vanish;
            // this idempotent ensure closes that window (no-op once configured).
            global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.EnsureVaultConfigured();
            try
            {
                long playerId    = pkg.ReadLong();
                string playerName = pkg.ReadString();
                var inner = new ZPackage(pkg.ReadByteArray());

                var op = (Op)inner.ReadByte();
                string profile = inner.ReadString();
                // kg-parity owner key: platform user id, server-derived from the sender's socket
                // (account-scoped like kg — a migrated DB.db resolves without remapping; unspoofable).
                string ownerId = global::FiresCore.Identity.OwnerKeys.ResolveEconomyOwner(senderId, playerId);

                switch (op)
                {
                    case Op.GetBalance:
                        AccrueInterest(ownerId, playerName, profile);
                        ReplyFull(senderId, ownerId, profile);
                        break;
                    case Op.Deposit:
                    {
                        string currency = inner.ReadString();
                        int amount = inner.ReadInt();
                        ServerDeposit(senderId, ownerId, profile, currency, amount);
                        break;
                    }
                    case Op.Withdraw:
                    {
                        string currency = inner.ReadString();
                        int amount = inner.ReadInt();
                        ServerWithdraw(senderId, ownerId, profile, currency, amount);
                        break;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"{LogPrefix} RPC_BankReq error: {ex.Message}"); }
        }

        private static void ServerDeposit(long senderId, string ownerId, string profile, string currency, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(currency)) return;
            int hash = currency.GetStableHashCode();
            int limit = LimitFor(profile, currency);
            int current = CurrentBalance(ownerId, hash);
            int room = Mathf.Max(0, limit - current);
            int banked = Mathf.Min(amount, room);
            int refund = amount - banked;
            if (banked > 0 && !BankRepository.Deposit(ownerId, hash, banked))
            {
                // Storage write failed (BankRepository logged why + the vault config state). Refund the WHOLE
                // amount so the coins the client already consumed come back instead of vanishing; balance
                // stays as it was. This is the safety net that makes a deposit transactional from the player's
                // point of view even when the server-side persist is broken.
                ReplyOne(senderId, currency, current, deposited: 0, withdrawn: 0, refunded: amount);
                return;
            }
            ReplyOne(senderId, currency, current + banked, deposited: banked, withdrawn: 0, refunded: refund);
        }

        private static void ServerWithdraw(long senderId, string ownerId, string profile, string currency, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(currency)) return;
            int hash = currency.GetStableHashCode();
            int withdrawn = BankRepository.Withdraw(ownerId, hash, amount);
            ReplyOne(senderId, currency, CurrentBalance(ownerId, hash), deposited: 0, withdrawn: withdrawn, refunded: 0);
        }

        private static int CurrentBalance(string ownerId, int hash)
        {
            var items = BankRepository.GetItems(ownerId);
            return items.TryGetValue(hash, out int v) ? v : 0;
        }

        private static int LimitFor(string profile, string currency)
        {
            var p = BankerProfileManager.GetProfile(profile);
            if (p != null && p.CurrencyLimits.TryGetValue(currency, out int lim)) return lim;
            // Currency not in this profile (or profile missing) — allow but bounded, matching the loader's
            // bare-currency default so an unknown-to-us currency still round-trips instead of vanishing.
            return 1_000_000;
        }

        // Accrue whole-minute interest since we last touched this owner, at the viewed profile's rate.
        private static void AccrueInterest(string ownerId, string playerName, string profile)
        {
            var p = BankerProfileManager.GetProfile(profile);
            if (p == null || p.InterestPerMinute <= 0f) return;

            EnsureClockLoaded();
            long now = DateTime.UtcNow.Ticks;
            if (!_lastInterest.TryGetValue(ownerId, out long last))
            {
                _lastInterest[ownerId] = now; // first view — start the clock, no retroactive interest
                SaveClock();
                return;
            }
            // 0.0.557: whole PERIODS (the profile's interestperiod), not whole minutes; the remainder carries over.
            long ticksPerMin = p.PeriodTicks;
            long wholeMinutes = (now - last) / ticksPerMin;
            if (wholeMinutes < 1) return;
            _lastInterest[ownerId] = last + wholeMinutes * ticksPerMin; // keep the remainder, don't snap to now
            SaveClock();

            float vipMult = VipManager.BankerInterestMultiplier(VipManager.IsVip(ownerId, playerName));
            var items = BankRepository.GetItems(ownerId);
            foreach (var kv in p.CurrencyLimits)
            {
                int hash = kv.Key.GetStableHashCode();
                int balance = items.TryGetValue(hash, out int v) ? v : 0;
                if (balance <= 0) continue;
                int gain = Mathf.RoundToInt(balance * (p.InterestPerMinute / 100f) * wholeMinutes * vipMult);
                if (gain <= 0) continue;
                int room = Mathf.Max(0, kv.Value - balance);
                int apply = Mathf.Min(gain, room);
                if (apply > 0) BankRepository.Deposit(ownerId, hash, apply);
            }
        }

        // ── Server → client replies ───────────────────────────────────────────────────────────────────────
        private static void ReplyFull(long senderId, string ownerId, string profile)
        {
            var p = BankerProfileManager.GetProfile(profile);
            var items = BankRepository.GetItems(ownerId);
            var pkg = new ZPackage();
            pkg.Write((byte)Res.BalanceFull);
            if (p == null)
            {
                pkg.Write(0);
            }
            else
            {
                pkg.Write(p.CurrencyLimits.Count);
                foreach (var kv in p.CurrencyLimits)
                {
                    int hash = kv.Key.GetStableHashCode();
                    int bal = items.TryGetValue(hash, out int v) ? v : 0;
                    pkg.Write(kv.Key);
                    pkg.Write(bal);
                }
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        private static void ReplyOne(long senderId, string currency, int newBalance, int deposited, int withdrawn, int refunded)
        {
            var pkg = new ZPackage();
            pkg.Write((byte)Res.BalanceOne);
            pkg.Write(currency ?? "");
            pkg.Write(newBalance);
            pkg.Write(deposited);
            pkg.Write(withdrawn);
            pkg.Write(refunded);
            ZRoutedRpc.instance.InvokeRoutedRPC(senderId, RpcRes, pkg);
        }

        // ── Client handler ────────────────────────────────────────────────────────────────────────────────
        private static void RPC_BankRes(long senderId, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                var res = (Res)pkg.ReadByte();
                if (res == Res.BalanceFull)
                {
                    int count = pkg.ReadInt();
                    var map = new Dictionary<string, int>();
                    for (int i = 0; i < count; i++)
                    {
                        string currency = pkg.ReadString();
                        int bal = pkg.ReadInt();
                        map[currency] = bal;
                    }
                    BankerBalanceStore.ReplaceCache(map);
                    OnResult?.Invoke("");
                    return;
                }

                // BalanceOne
                string cur = pkg.ReadString();
                int newBalance = pkg.ReadInt();
                int deposited  = pkg.ReadInt();
                int withdrawn  = pkg.ReadInt();
                int refunded   = pkg.ReadInt();

                BankerBalanceStore.SetCached(cur, newBalance);

                var player = Player.m_localPlayer;
                if (player != null)
                {
                    if (withdrawn > 0) GiveItems(player, cur, withdrawn);
                    if (refunded  > 0) GiveItems(player, cur, refunded);
                }

                string status =
                    withdrawn > 0                 ? $"Withdrew {withdrawn} {cur}." :
                    deposited > 0 && refunded > 0 ? $"Deposited {deposited} {cur}; bank full, refunded {refunded}." :
                    refunded  > 0                 ? $"Couldn't bank {cur} — refunded {refunded}." :
                    deposited > 0                 ? $"Deposited {deposited} {cur}." : "";
                OnResult?.Invoke(status);
            }
            catch (Exception ex) { Debug.LogWarning($"{LogPrefix} RPC_BankRes error: {ex.Message}"); }
        }

        private static void GiveItems(Player player, string prefab, int amount)
        {
            var inv = player.GetInventory();
            var pf = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
            if (drop != null && inv.CanAddItem(drop.m_itemData, amount))
                inv.AddItem(prefab, amount, 1, 0, 0L, "", false);
            else if (drop != null)
                ItemDrop.DropItem(drop.m_itemData, amount, player.transform.position + player.transform.forward, Quaternion.identity);
        }
    }
}
