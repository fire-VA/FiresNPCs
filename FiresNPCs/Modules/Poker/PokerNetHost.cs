using System;
using System.Collections.Generic;
using FiresCore.Tables;

namespace FiresRPGmaker.Modules.Poker
{
    /// <summary>
    /// SERVER-side table brain for online Hold'em — a <see cref="PokerGame"/> behind Core's
    /// <see cref="FiresTableNet"/>. Owns the pacing the solo UI used to own: AI think delays, the human turn clock
    /// (auto check/fold), the between-hands pause, and tournament end. <see cref="BuildState"/> is the whole client
    /// contract: a per-viewer snapshot where hole cards are redacted at the source — a viewer gets their own two
    /// cards always, and everyone else's only at showdown from the reveal order (mucked hands stay hidden forever).
    /// Constructor-pure (no ZNet needed) so the entire loop is verifiable offline; <see cref="AttachSession"/> wires
    /// the live transport.
    /// </summary>
    public sealed class PokerNetHost : IFiresTableHost
    {
        public const string GameKey = "poker";
        public const string CashGameKey = "pokercash";
        public const byte ActFold = 1, ActCheckCall = 2, ActRaiseTo = 3, ActResync = 4;
        public const int EvtTournamentOver = 100, EvtCashOut = 101;

        private const byte WireVersion = 2;
        private const byte CardHidden = 255;

        private readonly PokerGame _game;
        private readonly System.Random _rng;
        private readonly Dictionary<long, int> _seatOf = new Dictionary<long, int>();   // playerId → poker seat index
        private readonly Dictionary<int, long> _leaving = new Dictionary<int, long>();  // seat → departed player; fold on sight, vacate after hand
        private FiresTableNet.Session _session;

        // Cash mode — real-currency open table: bots are house money (refilled between hands, evaporate at close),
        // humans hot-swap bots between hands, leaving pays the stack out through OnCashOut.
        private readonly bool _cash;
        private readonly string _currency;
        private readonly int _buyIn;
        /// <summary>SERVER: fired when a human leaves a cash table — (playerId, stack to pay out). Set by the manager.</summary>
        public Action<long, int> OnCashOut;
        private struct PendingJoin { public long Id; public string Name; public int SeatIdx; public int BuyIn; }
        private readonly List<PendingJoin> _pendingJoins = new List<PendingJoin>();
        private readonly HashSet<int> _reservedSeats = new HashSet<int>();

        private float _clock;               // accumulated Tick time — the only clock the host uses
        private float _aiActAt = -1f;
        private float _nextHandAt = -1f;
        private float _turnDeadline = -1f;
        private int _turnSeat = -1;
        private bool _over;

        private static readonly string[] AiNames = { "Sigrid", "Toke", "Runa", "Geir", "Astrid" };

        public PokerNetHost(IReadOnlyList<FiresTableSeat> humans, int buyIn, int bigBlind, System.Random rng,
            int fillToSeats = 4, bool cash = false, string currency = null)
        {
            _rng = rng ?? new System.Random();
            _cash = cash;
            _currency = currency ?? "";
            _buyIn = buyIn;
            _game = new PokerGame(_rng) { SmallBlind = Math.Max(1, bigBlind / 2), BigBlind = bigBlind };

            int idx = 0;
            foreach (var h in humans)
            {
                _game.Seats.Add(new PokerSeat { Index = idx, Name = h.Name, IsAI = false, Chips = buyIn });
                _seatOf[h.Id] = idx;
                idx++;
            }
            for (int i = 0; idx < fillToSeats && i < AiNames.Length; i++, idx++)
                _game.Seats.Add(new PokerSeat { Index = idx, Name = AiNames[i], IsAI = true, Chips = buyIn });

            _game.StartHand();
            ArmPacing();
        }

        public void AttachSession(FiresTableNet.Session session) => _session = session;

        /// <summary>Mirror the game's seating into the Core session (humans by id, AI as bots) and attach it.</summary>
        public void BindSession(FiresTableNet.Session session, IReadOnlyList<FiresTableSeat> humans)
        {
            foreach (var h in humans) FiresTableNet.SeatHuman(session, h.Id, h.Name, h.Seat);
            foreach (var s in _game.Seats) if (s.IsAI) FiresTableNet.SeatBot(session, s.Name, s.Index);
            AttachSession(session);
        }

        /// <summary>Cash tables: reserve a bot seat for an incoming player (they swap in between hands).</summary>
        public bool TryReserveJoin(long playerId, out int seatIdx)
        {
            seatIdx = -1;
            if (!_cash || _over || _seatOf.ContainsKey(playerId)) return false;
            foreach (var s in _game.Seats)
            {
                if (!s.IsAI || _reservedSeats.Contains(s.Index)) continue;
                seatIdx = s.Index;
                _reservedSeats.Add(seatIdx);
                return true;
            }
            return false;
        }

        public void QueueJoin(long playerId, string name, int seatIdx, int buyIn)
        {
            if (!_cash || _over) return;
            _pendingJoins.Add(new PendingJoin { Id = playerId, Name = name, SeatIdx = seatIdx, BuyIn = buyIn });
            if (!_game.HandInProgress && _nextHandAt < 0f) _nextHandAt = _clock + 1f;   // idle table — deal them in now
        }

        private float TurnSeconds
        {
            get
            {
                try
                {
                    var cfg = UI.Screens.PlayerPanels.Minigames.PokerTableGame.TurnSecondsCfg;
                    if (cfg != null) return Math.Max(10, cfg.Value);
                }
                catch { }
                return 60f;
            }
        }

        // ── IFiresTableHost ─────────────────────────────────────────────────────────────────────────────────────
        public void OnAction(long playerId, ZPackage payload)
        {
            if (_over || payload == null) return;
            if (!_seatOf.TryGetValue(playerId, out int seatIdx)) return;

            try
            {
                byte code = payload.ReadByte();
                if (code == ActResync) { Push(); return; }   // reopened window asking for the current world

                var cur = _game.Current;
                if (cur == null || cur.Index != seatIdx || cur.IsAI) return;   // not your turn — drop silently
                switch (code)
                {
                    case ActFold: _game.Fold(); break;
                    case ActCheckCall: _game.CheckCall(); break;
                    case ActRaiseTo:
                        int raiseTo = payload.ReadInt();
                        if (!_game.CanRaise(cur)) { _game.CheckCall(); break; }
                        _game.Raise(raiseTo);   // engine clamps to legal min / stack
                        break;
                    default: return;
                }
            }
            catch { return; }

            ArmPacing();
            Push();
        }

        public void OnJoin(FiresTableSeat seat) => Push();   // first attach — hand them the world

        public void OnLeave(FiresTableSeat seat, bool timedOut)
        {
            if (_over || seat == null) return;
            if (!_seatOf.TryGetValue(seat.Id, out int seatIdx)) return;
            _seatOf.Remove(seat.Id);
            _leaving[seatIdx] = seat.Id;

            var ps = seatIdx < _game.Seats.Count ? _game.Seats[seatIdx] : null;
            if (_cash && ps != null)
            {
                // Pay the stack out NOW (chips committed to the live pot stay in it); zero it so nothing double-counts.
                int stack = ps.Chips;
                ps.Chips = 0;
                if (!ps.InHand) ps.Occupied = false;
                try { OnCashOut?.Invoke(seat.Id, stack); } catch { }
                if (_session != null && stack > 0)
                {
                    var e = new ZPackage();
                    e.Write(stack);
                    e.Write(_currency ?? "");
                    FiresTableNet.SendEventTo(_session, seat, EvtCashOut, e);
                }
            }
            if (ps != null && _game.Current == ps) { _game.Fold(); ArmPacing(); }

            // Nobody human left → the table evaporates (bots don't play for an empty room; house money with it).
            if (_seatOf.Count == 0) { _over = true; if (_session != null) FiresTableNet.Close(_session.Id, "table emptied"); return; }
            Push();
        }

        public void Tick(float dt)
        {
            if (_over) return;
            _clock += dt;

            if (_game.HandInProgress)
            {
                var cur = _game.Current;
                if (cur != null)
                {
                    if (_leaving.ContainsKey(cur.Index)) { _game.Fold(); ArmPacing(); Push(); return; }
                    if (cur.IsAI)
                    {
                        if (_aiActAt >= 0f && _clock >= _aiActAt)
                        {
                            PokerAI.TakeTurn(_game, _rng);
                            ArmPacing();
                            Push();
                        }
                    }
                    else if (_turnDeadline >= 0f && _clock >= _turnDeadline)
                    {
                        if (_game.CanCheck(cur)) _game.CheckCall(); else _game.Fold();   // clock ran out
                        ArmPacing();
                        Push();
                    }
                }
            }
            else if (_nextHandAt >= 0f && _clock >= _nextHandAt)
            {
                _nextHandAt = -1f;
                VacateLeavers();
                if (_seatOf.Count == 0 && _pendingJoins.Count == 0)
                {
                    _over = true;
                    if (_session != null) FiresTableNet.Close(_session.Id, "table emptied");
                    return;
                }
                if (_cash) BetweenHandsCash();
                if (_over) return;   // the boundary kicked the last human — table closed mid-housekeeping
                if (LiveStacks() < 2) { if (!_cash) EndTournament(); return; }   // cash: refills make this transient
                int hn = _game.HandNumber;
                _game.StartHand();
                if (_game.HandNumber == hn) { if (!_cash) EndTournament(); return; }   // blind-all-in runouts ARE real hands
                ArmPacing();
                Push();
            }
        }

        public ZPackage BuildState(long viewerId)
        {
            int viewerSeat = _seatOf.TryGetValue(viewerId, out int vs) ? vs : -1;
            bool complete = _game.Phase == PokerPhase.Complete;

            var pkg = new ZPackage();
            pkg.Write(WireVersion);
            pkg.Write((byte)_game.Phase);
            pkg.Write(_game.HandNumber);
            pkg.Write(_game.Button);
            pkg.Write(_game.ToAct);
            pkg.Write(_game.CurrentBet);
            pkg.Write(_game.MinRaise);
            pkg.Write(_game.SmallBlind);
            pkg.Write(_game.BigBlind);
            pkg.Write(_game.Pot);
            pkg.Write(viewerSeat);
            pkg.Write(_cash);
            pkg.Write(_currency ?? "");

            pkg.Write(_game.Seats.Count);
            foreach (var s in _game.Seats)
            {
                pkg.Write(s.Index);
                pkg.Write(s.Name ?? "");
                pkg.Write(s.IsAI);
                pkg.Write(s.Occupied);
                pkg.Write(s.Chips);
                pkg.Write(s.Bet);
                pkg.Write(s.Committed);
                pkg.Write(s.HasCards);
                pkg.Write(s.Folded);
                pkg.Write(s.Mucked);
                pkg.Write(s.AllIn);
                pkg.Write(s.Won);
                pkg.Write(s.LastAction ?? "");
                bool reveal = s.HasCards && (s.Index == viewerSeat || (complete && s.InHand && !s.Mucked));
                pkg.Write(reveal ? (byte)s.A.Index : CardHidden);
                pkg.Write(reveal ? (byte)s.B.Index : CardHidden);
            }

            pkg.Write(_game.Board.Count);
            foreach (var c in _game.Board) pkg.Write((byte)c.Index);

            // viewer's legal actions (zeros when it isn't their turn)
            var cur2 = _game.Current;
            bool myTurn = cur2 != null && viewerSeat >= 0 && cur2.Index == viewerSeat;
            pkg.Write(myTurn);
            pkg.Write(myTurn ? _game.ToCall(cur2) : 0);
            pkg.Write(myTurn && _game.CanCheck(cur2));
            pkg.Write(myTurn && _game.CanRaise(cur2));
            pkg.Write(myTurn ? _game.MinRaiseTo(cur2) : 0);
            pkg.Write(myTurn ? _game.MaxRaiseTo(cur2) : 0);
            pkg.Write(_turnDeadline >= 0f ? Math.Max(0f, _turnDeadline - _clock) : -1f);

            int logStart = Math.Max(0, _game.Log.Count - 40);
            pkg.Write(logStart);                              // absolute index of the first tail line (feed cursor)
            pkg.Write(_game.Log.Count - logStart);
            for (int i = logStart; i < _game.Log.Count; i++) pkg.Write(_game.Log[i]);

            pkg.Write(_game.Results.Count);
            foreach (var r in _game.Results)
            {
                pkg.Write(r.Desc ?? "");
                pkg.Write(r.Amount);
                pkg.Write(r.Winners.Count);
                foreach (int w in r.Winners) pkg.Write(w);
            }
            pkg.Write(_game.ShowOrder.Count);
            foreach (int i in _game.ShowOrder) pkg.Write(i);

            return pkg;
        }

        // ── pacing ──────────────────────────────────────────────────────────────────────────────────────────────
        private void ArmPacing()
        {
            _aiActAt = -1f; _turnDeadline = -1f; _turnSeat = -1;

            if (_game.Phase == PokerPhase.Complete)
            {
                if (_nextHandAt < 0f) _nextHandAt = _clock + 8f;   // room for the client's staged runout reveal
                return;
            }
            _nextHandAt = -1f;

            var cur = _game.Current;
            if (cur == null) return;
            if (cur.IsAI) _aiActAt = _clock + 1.2f + (float)_rng.NextDouble() * 1.4f;
            else { _turnDeadline = _clock + TurnSeconds; _turnSeat = cur.Index; }
        }

        private void VacateLeavers()
        {
            foreach (var kv in _leaving)
            {
                if (kv.Key >= _game.Seats.Count) continue;
                var s = _game.Seats[kv.Key];
                if (_cash && s.Chips > 0)
                {
                    // An all-in leaver won at showdown after quitting — the residual is still their money.
                    try { OnCashOut?.Invoke(kv.Value, s.Chips); } catch { }
                    s.Chips = 0;
                }
                s.Occupied = false;
            }
            _leaving.Clear();
        }

        // Cash-table hand boundary: swap reserved bots for arrived players, kick busted humans (cash-out 0 already
        // implied — their stack IS 0), refill busted bot stacks with house money.
        private void BetweenHandsCash()
        {
            foreach (var j in _pendingJoins)
            {
                if (j.SeatIdx >= _game.Seats.Count) continue;
                var s = _game.Seats[j.SeatIdx];
                s.Name = j.Name;
                s.IsAI = false;
                s.Chips = j.BuyIn;      // bot's remaining stack evaporates — house money never enters the economy
                s.Occupied = true;
                _seatOf[j.Id] = j.SeatIdx;
                _reservedSeats.Remove(j.SeatIdx);
            }
            _pendingJoins.Clear();

            // Busted humans leave the table (their seat returns to the house).
            var busted = new List<long>();
            foreach (var kv in _seatOf)
                if (kv.Value < _game.Seats.Count && _game.Seats[kv.Value].Chips <= 0) busted.Add(kv.Key);
            foreach (long pid in busted)
            {
                var coreSeat = _session?.Find(pid);
                if (coreSeat != null) FiresTableNet.Unseat(_session, coreSeat, timedOut: false);   // → OnLeave (cash-out 0)
                else _seatOf.Remove(pid);
            }

            // Seats without a human become house bots again, refilled.
            for (int i = 0; i < _game.Seats.Count; i++)
            {
                var s = _game.Seats[i];
                bool humanSeat = false;
                foreach (var kv in _seatOf) if (kv.Value == i) { humanSeat = true; break; }
                if (humanSeat) continue;
                if (!s.IsAI) { s.IsAI = true; s.Name = AiNames[i % AiNames.Length]; }
                if (!_reservedSeats.Contains(i)) { s.Chips = _buyIn; s.Occupied = true; }
            }
        }

        private int LiveStacks()
        {
            int n = 0;
            foreach (var s in _game.Seats) if (s.Occupied && s.Chips > 0) n++;
            return n;
        }

        private void EndTournament()
        {
            _over = true;
            PokerSeat winner = null;
            foreach (var s in _game.Seats) if (s.Occupied && s.Chips > 0 && (winner == null || s.Chips > winner.Chips)) winner = s;
            if (_session != null)
            {
                var pkg = new ZPackage();
                pkg.Write(winner != null ? winner.Name : "");
                pkg.Write(winner != null ? winner.Chips : 0);
                FiresTableNet.SendEventAll(_session, EvtTournamentOver, pkg);
                FiresTableNet.Close(_session.Id, "tournament over");
            }
        }

        private void Push()
        {
            if (_session != null) FiresTableNet.PushState(_session);
        }
    }
}
