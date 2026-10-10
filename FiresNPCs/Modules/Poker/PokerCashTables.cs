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
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcJoin, pkg);
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
                long playerId = pkg.ReadLong();
                string name = pkg.ReadString();
                ZDOID uid = pkg.ReadZDOID();
                string tableId = "pokercash_" + uid;

                string currency = Currency;
                int buyIn = BuyIn;
                int hash = currency.GetStableHashCode();
                string owner = playerId.ToString();

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
                    host.OnCashOut = (pid, stack) => { if (stack > 0) BankRepository.Deposit(pid.ToString(), hash, stack); };
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
