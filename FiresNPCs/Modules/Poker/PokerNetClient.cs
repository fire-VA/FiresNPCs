using System;
using FiresCore.Tables;
using UnityEngine;

namespace FiresRPGmaker.Modules.Poker
{
    /// <summary>
    /// CLIENT-side glue for online Hold'em: listens on <see cref="FiresTableNet"/>, answers a poker invite by
    /// attaching + opening the table UI (closing the queue lobby they just voted in), caches the latest per-viewer
    /// snapshot so a reopened window renders instantly, and parks the dock chip when the window is minimized —
    /// the chip's heartbeat keeps the seat alive and it flares when the action's on you.
    /// </summary>
    public static class PokerNetClient
    {
        private static bool _init;
        private static string _active;
        private static byte[] _latest;
        private static int _latestVersion;
        private static bool _chipMine;

        /// <summary>Snapshot stream for the open table window — (tableId, version, payload).</summary>
        public static event Action<string, int, ZPackage> StateForUi;
        /// <summary>Table events for the open window — (tableId, code, payload).</summary>
        public static event Action<string, int, ZPackage> EventForUi;

        public static string ActiveTable => _active;
        public static ZPackage Latest => _latest != null ? new ZPackage(_latest) : null;

        public static void Init()
        {
            if (_init) return;
            _init = true;
            FiresTableNet.StateReceived += OnState;
            FiresTableNet.EventReceived += OnEvent;
            global::FiresCore.Queue.FiresQueueLobby.Closed += OnLobbyClosed;
        }

        // Closing the lobby while queued parks the dock chip — the queue keeps your spot either way, this keeps
        // it VISIBLE. Flashes when the rest of the roster is readied and your vote is the holdout.
        private static void OnLobbyClosed(string queueId)
        {
            if (queueId != PokerLobby.QueueId) return;
            if (!global::FiresCore.Queue.FiresQueueSystem.LocalInQueue(queueId)) return;
            ParkQueueChip();
        }

        public static void ParkQueueChip()
        {
            _chipMine = true;
            Modules.TableGames.QueueDockChip.Show(
                "Hold'em — queued for a table…",
                "Hold'em — they're waiting on YOU! Click to ready up.",
                () =>
                {
                    var roster = global::FiresCore.Queue.FiresQueueSystem.Roster(PokerLobby.QueueId);
                    if (roster.Count < 2 || global::FiresCore.Queue.FiresQueueSystem.LocalReady(PokerLobby.QueueId)) return false;
                    foreach (var m in roster)
                        if (m.Id != (Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L) && !m.Ready) return false;
                    return true;   // everyone else is ready — you're the vote holding the deal
                },
                () => { _chipMine = false; global::FiresCore.Queue.FiresQueueLobby.Show(PokerLobby.QueueId); },
                () => { _chipMine = false; global::FiresCore.Queue.FiresQueueSystem.Leave(PokerLobby.QueueId); },
                null);
        }

        private static void OnState(string tableId, int version, ZPackage pkg)
        {
            if (_active == null || tableId != _active) return;
            _latest = pkg.GetArray();
            _latestVersion = version;
            StateForUi?.Invoke(tableId, version, new ZPackage(_latest));
        }

        private static void OnEvent(string tableId, int code, ZPackage pkg)
        {
            if (code == FiresTableNet.EvtInvite)
            {
                if (!FiresTableNet.TryParseInvite(pkg, out var gameKey, out var seats)
                    || (gameKey != PokerNetHost.GameKey && gameKey != PokerNetHost.CashGameKey)) return;
                long me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
                if (me == 0L) return;
                bool mine = false;
                foreach (var s in seats) if (s.Id == me) { mine = true; break; }
                if (!mine) return;

                bool sameTable = _active == tableId;
                _active = tableId;
                FiresTableNet.Attach(tableId);
                if (sameTable) return;   // hot-join rebroadcast — don't yank open a window someone minimized
                _latest = null;
                if (global::FiresCore.Queue.FiresQueueLobby.IsOpen)
                    global::FiresCore.Queue.FiresQueueLobby.Toggle(PokerLobby.QueueId);   // they just voted — straight to the felt
                HideChip();
                OpenTable();
                return;
            }

            // Cash-out receipts arrive after a deliberate Quit already cleared the active table — always surface them.
            if (code == PokerNetHost.EvtCashOut)
            {
                try
                {
                    var own = new ZPackage(pkg.GetArray());
                    int amount = own.ReadInt();
                    string currency = own.ReadString();
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Cashed out " + amount + " " + currency + " to your bank.");
                }
                catch { }
                EventForUi?.Invoke(tableId, code, new ZPackage(pkg.GetArray()));
                return;
            }

            if (_active == null || tableId != _active) return;

            if (code == FiresTableNet.EvtUnseated)
            {
                EventForUi?.Invoke(tableId, code, new ZPackage(pkg.GetArray()));
                _active = null;
                _latest = null;
                HideChip();
                return;
            }

            byte[] raw = pkg.GetArray();
            if (code == FiresTableNet.EvtClosed || code == PokerNetHost.EvtTournamentOver)
            {
                EventForUi?.Invoke(tableId, code, new ZPackage(raw));
                if (code == PokerNetHost.EvtTournamentOver)
                {
                    try
                    {
                        var own = new ZPackage(raw);
                        string name = own.ReadString();
                        int chips = own.ReadInt();
                        if (!string.IsNullOrEmpty(name))
                            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, name + " wins the table with " + chips + " chips!");
                    }
                    catch { }
                }
                _active = null;
                _latest = null;
                HideChip();
            }
            else EventForUi?.Invoke(tableId, code, new ZPackage(raw));
        }

        public static void OpenTable()
        {
            if (_active == null) return;
            UI.Screens.PlayerPanels.Minigames.PokerTableGame.OpenNet(_active);
        }

        /// <summary>Park the dock chip for the active table (window minimized). Heartbeats the seat; flares on your turn.</summary>
        public static void ParkChip()
        {
            var table = _active;
            if (table == null) return;
            _chipMine = true;
            Modules.TableGames.QueueDockChip.Show(
                "Hold'em — table live",
                "Hold'em — YOUR TURN! Click to return.",
                () => LatestMyTurn(),
                () => { _chipMine = false; OpenTable(); },
                () => { _chipMine = false; Quit(); },
                () => { if (table == _active) FiresTableNet.Ping(table); });
        }

        /// <summary>Leave the tournament queue (from the popup or anywhere else) and drop the status chip with it.</summary>
        public static void LeaveQueue()
        {
            global::FiresCore.Queue.FiresQueueSystem.Leave(PokerLobby.QueueId);
            HideChip();
        }

        /// <summary>Leave the tournament for good.</summary>
        public static void Quit()
        {
            if (_active == null) return;
            FiresTableNet.Detach(_active);
            _active = null;
            _latest = null;
        }

        private static void HideChip()
        {
            if (!_chipMine) return;
            _chipMine = false;
            Modules.TableGames.QueueDockChip.Hide();
        }

        // Skip-parse the cached snapshot far enough to read the myTurn flag (wire v2 — see PokerNetHost.BuildState).
        private static bool LatestMyTurn()
        {
            if (_latest == null) return false;
            try
            {
                var p = new ZPackage(_latest);
                if (p.ReadByte() != 2) return false;
                p.ReadByte();
                for (int i = 0; i < 9; i++) p.ReadInt();
                p.ReadBool(); p.ReadString();   // cash flag + currency
                int seats = p.ReadInt();
                for (int i = 0; i < seats; i++)
                {
                    p.ReadInt(); p.ReadString(); p.ReadBool(); p.ReadBool();
                    p.ReadInt(); p.ReadInt(); p.ReadInt();
                    p.ReadBool(); p.ReadBool(); p.ReadBool(); p.ReadBool();
                    p.ReadInt(); p.ReadString(); p.ReadByte(); p.ReadByte();
                }
                int board = p.ReadInt();
                for (int i = 0; i < board; i++) p.ReadByte();
                return p.ReadBool();
            }
            catch { return false; }
        }
    }
}
