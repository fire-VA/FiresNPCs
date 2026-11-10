using System;
using System.Collections.Generic;
using FiresCore.Storage;
using FiresCore.Tables;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Poker
{
    /// <summary>
    /// SERVER manager for open (cash) Hold'em tables — one session per PLACED poker table piece (keyed by its ZDOID),
    /// playing for REAL currency. Sit down → the buy-in is withdrawn from your BANK (server-side, the Banker ledger —
    /// no client trust anywhere in the money path) → you're seated instantly vs house bots; more players at the same
    /// physical table hot-swap the bots between hands. Leaving (or busting) pays your stack back into the bank.
    /// Bot stacks are house money: refilled between hands, evaporated when the table closes. Transport = the
    /// BankerNetwork idiom (playerId in the request, reply to the raw sender, instance-tracked registration).
    /// </summary>
    public static class PokerCashTables
    {
        private const string RpcJoin = "FiresRPG_PokerCashJoin";
        private const string RpcJoinResult = "FiresRPG_PokerCashJoinResult";

        public static BepInEx.Configuration.ConfigEntry<string> CurrencyCfg;
        public static BepInEx.Configuration.ConfigEntry<int> BuyInCfg, BigBlindCfg;

        private static bool _registered;
        private static ZRoutedRpc _registeredOn;
        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        private static string Currency => CurrencyCfg != null && !string.IsNullOrEmpty(CurrencyCfg.Value) ? CurrencyCfg.Value : "Coins";
        private static int BuyIn => BuyInCfg != null ? Mathf.Max(50, BuyInCfg.Value) : 500;
        private static int BigBlind => BigBlindCfg != null ? Mathf.Max(2, BigBlindCfg.Value) : 10;

        public static void Initialize()
        {
            var cur = ZRoutedRpc.instance;
            if (cur == null) return;
            if (_registered && _registeredOn == cur) return;
            cur.Register<ZPackage>(RpcJoin, new Action<long, ZPackage>(RPC_Join));
            cur.Register<ZPackage>(RpcJoinResult, new Action<long, ZPackage>(RPC_JoinResult));
            _registered = true; _registeredOn = cur;
        }

        // ── client ──────────────────────────────────────────────────────────────────────────────────────────────
        /// <summary>Ask the server for a seat at THIS placed table, buy-in drawn from the bank.</summary>
        public static void RequestJoin(ZNetView tablePiece)
        {
            Initialize();
            var local = Player.m_localPlayer;
            if (local == null || tablePiece == null || !tablePiece.IsValid() || ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(local.GetPlayerID());
            pkg.Write(local.GetPlayerName() ?? "");
            pkg.Write(tablePiece.GetZDO().m_uid);
            ZRoutedRpc.instance.InvokeRoutedRPC(global::FiresRPGmaker.Managers.RpcRegistration.ServerPeer(RpcJoin), RpcJoin, pkg);
        }

        private static void RPC_JoinResult(long sender, ZPackage pkg)
        {
            try
            {
                bool ok = pkg.ReadBool();
                string msg = pkg.ReadString();
                if (!ok && Player.m_localPlayer != null && !string.IsNullOrEmpty(msg))
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, msg);
            }
            catch { }
        }

        // ── server ──────────────────────────────────────────────────────────────────────────────────────────────
        private static void RPC_Join(long sender, ZPackage pkg)
        {
            if (!IsServer || pkg == null) return;
            try
            {
                long claimedPlayerId = pkg.ReadLong();
                pkg.ReadString();   // client-sent name: ignored (spoofable)
                ZDOID uid = pkg.ReadZDOID();

                // Security (audit 10-23): identity comes from the routed sender only. The seat id is the sender's
                // own character playerID (read from its ZDO), the name is the peer's, and the bank owner is the
                // server-derived economy key (the same key the Banker deposits under).
                long playerId = global::FiresRPGmaker.Modules.Marketplace.EconomySenderGuard.SenderPlayerId(sender);
                if (playerId == 0L || playerId != claimedPlayerId)
                {
                    Debug.LogWarning($"[PokerCash] join refused for sender {sender}: player id {claimedPlayerId} is not the sender's character ({playerId})");
                    Reply(sender, false, "Couldn't verify your character — try again.");
                    return;
                }
                string name = global::FiresRPGmaker.Modules.Marketplace.EconomySenderGuard.SenderName(sender);
                if (!IsRealTableNearSender(sender, uid, out string tableWhy))
                {
                    Debug.LogWarning($"[PokerCash] join refused for sender {sender}: table {uid} {tableWhy}");
                    Reply(sender, false, "Stand at the poker table to sit down.");
                    return;
                }
                string tableId = "pokercash_" + uid;

                string currency = Currency;
                int buyIn = BuyIn;
                int hash = currency.GetStableHashCode();
                string owner = global::FiresCore.Identity.OwnerKeys.ResolveEconomyOwner(sender, playerId);
                _ownerByPlayerId[playerId] = owner;

                var session = FiresTableNet.Get(tableId);
                var host = session?.Host as PokerNetHost;

                if (session != null && session.Find(playerId) != null)
                {
                    Reply(sender, true, "");   // already seated — the invite rebroadcast reopens their window
                    FiresTableNet.SendInvites(session);
                    return;
                }

                int reservedSeat = -1;
                if (host != null && !host.TryReserveJoin(playerId, out reservedSeat))
                {
                    Reply(sender, false, "That table is full.");
                    return;
                }

                // Bank-funded buy-in, exact or nothing (partial withdrawals go straight back).
                int got = BankRepository.Withdraw(owner, hash, buyIn);
                if (got < buyIn)
                {
                    if (got > 0) BankRepository.Deposit(owner, hash, got);
                    Reply(sender, false, "Bank at least " + buyIn + " " + currency + " with the Banker to sit down.");
                    return;
                }

                if (session == null)
                {
                    var humans = new List<FiresTableSeat> { new FiresTableSeat { Id = playerId, Name = name, Seat = 0 } };
                    host = new PokerNetHost(humans, buyIn, BigBlind, new System.Random(), 4, cash: true, currency: currency);
                    // Pay back into the SAME bank key the buy-in came from (seat ids are playerIDs, bank keys are owner keys).
                    host.OnCashOut = (pid, stack) =>
                    {
                        // Kept after a cash-out: a player who leaves mid-hand is paid again for the showdown residual, which must
                        // reach the same account (one entry per player who ever sat, rewritten on rejoin).
                        string to = OwnerFor(pid);
                        if (stack > 0) BankRepository.Deposit(to, hash, stack);
                    };
                    session = FiresTableNet.Open(tableId, PokerNetHost.CashGameKey, host);
                    if (session == null)
                    {
                        BankRepository.Deposit(owner, hash, buyIn);   // seat fell through — money back
                        Reply(sender, false, "The table isn't ready — try again.");
                        return;
                    }
                    host.BindSession(session, humans);
                }
                else
                {
                    FiresTableNet.SeatHuman(session, playerId, name, reservedSeat);
                    host.QueueJoin(playerId, name, reservedSeat, buyIn);
                }

                FiresTableNet.SendInvites(session);
                Reply(sender, true, "");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PokerCash] join failed: " + ex);
            }
        }

        /// <summary>How close (metres) the sender's character must be to the placed table it joins.</summary>
        private const float TableJoinRadius = 8f;   // interact range + table size + server-side position lag
        /// <summary>The placed poker table piece (ChessTablePiece Def "piece_fires_pokertable", Interact = 3).</summary>
        private const string PokerTablePrefab = "piece_fires_pokertable";
        private static readonly int PokerTablePrefabHash = PokerTablePrefab.GetStableHashCode();

        // Seat playerID -> the economy owner key its buy-in was withdrawn from (server-side; cash-out pays it back there).
        private static readonly Dictionary<long, string> _ownerByPlayerId = new Dictionary<long, string>();

        private static string OwnerFor(long playerId) =>
            _ownerByPlayerId.TryGetValue(playerId, out var o) && !string.IsNullOrEmpty(o) ? o : playerId.ToString();

        // The ZDOID must be a live placed poker table (its prefab carries PokerTableInteractable) within reach of the sender.
        private static bool IsRealTableNearSender(long sender, ZDOID uid, out string why)
        {
            why = "";
            var zdo = uid.IsNone() || ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(uid);
            if (zdo == null) { why = "does not exist"; return false; }
            // Prefab-hash check (no ZNetScene/bundle needed, so it holds on a dedicated server too).
            if (zdo.GetPrefab() != PokerTablePrefabHash)
            { why = "is not a poker table"; return false; }
            if (!global::FiresRPGmaker.Modules.Marketplace.EconomySenderGuard.TryGetSenderPosition(sender, out var pos))
            { why = "- sender position unknown"; return false; }
            if (Vector3.Distance(pos, zdo.GetPosition()) > TableJoinRadius)
            { why = $"is more than {TableJoinRadius} m from the sender"; return false; }
            return true;
        }

        private static void Reply(long sender, bool ok, string msg)
        {
            var pkg = new ZPackage();
            pkg.Write(ok);
            pkg.Write(msg ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcJoinResult, pkg);
        }

        // Routes register on world load, client + server — the FiresQueueSystem idiom.
        [HarmonyPatch(typeof(Game), "Start")]
        private static class Game_Start_Init
        {
            private static void Postfix() { try { Initialize(); } catch { } }
        }
    }
}
