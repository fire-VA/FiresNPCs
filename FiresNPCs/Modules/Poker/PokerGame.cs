using System.Collections.Generic;
using System.Linq;

namespace FiresRPGmaker.Modules.Poker
{
    public enum PokerPhase { NotStarted, Preflop, Flop, Turn, River, Complete }

    /// <summary>A player's seat + per-hand state. Chips is the stack; Bet is chips in THIS betting round; Committed is
    /// total chips in THIS hand (drives side pots).</summary>
    public sealed class PokerSeat
    {
        public int Index;
        public string Name;
        public bool IsAI;
        public bool Occupied = true;
        public int Chips;

        public Card A, B;
        public bool HasCards;
        public bool Folded;
        public bool Mucked;        // showdown choice — hand stays pot-eligible, just not revealed
        public bool AllIn;
        public bool ActedThisRound;
        public int Bet;
        public int Committed;
        public int Won;            // filled at showdown, for the UI
        public string LastAction;  // "Fold" / "Check" / "Call 20" / "Raise to 60" / "All-in 120" — shown above the seat

        public bool InHand => Occupied && HasCards && !Folded;   // still contesting the pot
        public bool CanAct => InHand && !AllIn;
    }

    public sealed class PotResult { public string Desc; public List<int> Winners = new List<int>(); public int Amount; }

    /// <summary>
    /// Server-authoritative-ready Texas Hold'em engine — pure logic, no Unity. Drives one table: blinds, dealing, the
    /// four betting rounds, all-in side pots, and showdown/payout via <see cref="PokerHandEval"/>. The caller (UI or a
    /// sim) reads state + the legal-action helpers, then calls Fold/CheckCall/Raise for the seat in <see cref="ToAct"/>;
    /// AI seats are driven the same way by <see cref="PokerAI"/>. Fold-out (all but one fold) and all-in run-outs are
    /// handled. Same engine will back the multiplayer table — the network layer only relays actions + state.
    /// </summary>
    public sealed class PokerGame
    {
        public readonly List<PokerSeat> Seats = new List<PokerSeat>();
        public readonly List<Card> Board = new List<Card>();
        public readonly List<string> Log = new List<string>();
        public readonly List<PotResult> Results = new List<PotResult>();

        public PokerPhase Phase = PokerPhase.NotStarted;
        public int Button = -1;
        public int ToAct = -1;
        public int CurrentBet;      // highest Bet this round — the amount to match
        public int MinRaise;        // smallest legal raise increment right now
        public int SmallBlind, BigBlind;
        public int HandNumber;
        /// <summary>Seat of the last bet/raise in the current betting round (-1 = checked around). At showdown this is
        /// who must show first (TDA); if -1, first live seat left of the button shows first.</summary>
        public int LastAggressor = -1;
        /// <summary>Showdown reveal order (seat indexes, aggressor first), filled by ResolveShowdown for the UI.</summary>
        public readonly List<int> ShowOrder = new List<int>();
        /// <summary>Raised (with a message) if a duplicate card is ever detected among live cards — the tripwire for
        /// the reported same-card sighting. Subscribe from the UI to surface it loudly.</summary>
        public static System.Action<string> IntegrityAlarm;

        private Deck _deck;
        private readonly System.Random _rng;

        public PokerGame(System.Random rng) { _rng = rng ?? new System.Random(); }

        public int Pot { get { int p = 0; foreach (var s in Seats) p += s.Committed; return p; } }
        public PokerSeat Current => (ToAct >= 0 && ToAct < Seats.Count) ? Seats[ToAct] : null;
        public bool HandInProgress => Phase != PokerPhase.NotStarted && Phase != PokerPhase.Complete;
        public int InHandCount => Seats.Count(s => s.InHand);
        private int SeatCount => Seats.Count(s => s.Occupied);

        // ── legal-action helpers (the UI/AI read these for the seat to act) ─────────────────────────────────────
        public int ToCall(PokerSeat s) => System.Math.Min(CurrentBet - s.Bet, s.Chips);
        public bool CanCheck(PokerSeat s) => s.Bet >= CurrentBet;
        public int MinRaiseTo(PokerSeat s) => System.Math.Min(s.Bet + s.Chips, CurrentBet + MinRaise);  // capped to all-in
        public int MaxRaiseTo(PokerSeat s) => s.Bet + s.Chips;                                          // shove
        public bool CanRaise(PokerSeat s) => s.Chips > ToCall(s);                                       // has chips beyond the call

        // ── hand lifecycle ──────────────────────────────────────────────────────────────────────────────────────
        public void StartHand()
        {
            foreach (var s in Seats) if (s.Chips <= 0) s.Occupied = false;   // busted players leave the table
            var live = Seats.Where(s => s.Occupied && s.Chips > 0).ToList();
            if (live.Count < 2) { Phase = PokerPhase.Complete; return; }

            HandNumber++;
            Board.Clear(); Results.Clear(); Log.Clear();
            _deck = new Deck(_rng);

            foreach (var s in Seats)
            {
                s.Folded = false; s.Mucked = false; s.AllIn = false; s.HasCards = false; s.ActedThisRound = false;
                s.Bet = 0; s.Committed = 0; s.Won = 0; s.LastAction = null;
            }
            LastAggressor = -1;
            ShowOrder.Clear();

            Button = NextOccupied(Button < 0 ? Seats.Count - 1 : Button);

            // Blinds. Heads-up: the button is the small blind and acts first pre-flop.
            int sb, bb;
            if (SeatCount == 2) { sb = Button; bb = NextOccupied(Button); }
            else { sb = NextOccupied(Button); bb = NextOccupied(sb); }
            PostBlind(Seats[sb], SmallBlind);
            PostBlind(Seats[bb], BigBlind);
            CurrentBet = BigBlind;
            MinRaise = BigBlind;

            // Deal to everyone dealt INTO the hand (the pre-blind live list) — a player who went all-in posting a
            // blind still gets cards; filtering on current chips here left them with default Card{0} that renders as
            // an identical blank (the "two of the same card" bug).
            foreach (var s in live) { s.A = _deck.Draw(); s.B = _deck.Draw(); s.HasCards = true; }
            VerifyIntegrity("deal");

            Phase = PokerPhase.Preflop;
            Log.Add($"— Hand #{HandNumber}: button {Seats[Button].Name}, blinds {SmallBlind}/{BigBlind} —");
            ToAct = FirstToActPreflop(bb);
            if (ToAct < 0) AdvancePhase();   // e.g. both blinds all-in — run the board out
        }

        private void PostBlind(PokerSeat s, int amount)
        {
            int put = System.Math.Min(amount, s.Chips);
            s.Chips -= put; s.Bet += put; s.Committed += put;
            if (s.Chips == 0) s.AllIn = true;
        }

        private int FirstToActPreflop(int bb) => NextActable(bb);
        private int FirstToActPostflop() => NextActable(Button);

        // ── actions (operate on Current) ────────────────────────────────────────────────────────────────────────
        public void Fold()
        {
            var s = Current; if (s == null) return;
            s.Folded = true; s.ActedThisRound = true;
            s.LastAction = "Fold";
            Log.Add($"{s.Name} folds.");
            AfterAction();
        }

        public void CheckCall()
        {
            var s = Current; if (s == null) return;
            int call = ToCall(s);
            if (call > 0)
            {
                s.Chips -= call; s.Bet += call; s.Committed += call;
                if (s.Chips == 0) s.AllIn = true;
                Log.Add(s.AllIn ? $"{s.Name} calls {call} (all-in)." : $"{s.Name} calls {call}.");
            }
            else Log.Add($"{s.Name} checks.");
            s.LastAction = call > 0 ? (s.AllIn ? $"Call {call} (all-in)" : $"Call {call}") : "Check";
            s.ActedThisRound = true;
            AfterAction();
        }

        /// <summary>Raise the total this-round bet TO <paramref name="raiseTo"/> chips (clamped to a legal min / the
        /// player's stack). A shove that can't cover a full min-raise is still allowed as an all-in.</summary>
        public void Raise(int raiseTo)
        {
            var s = Current; if (s == null) return;
            raiseTo = System.Math.Min(raiseTo, MaxRaiseTo(s));
            int minLegal = System.Math.Min(MaxRaiseTo(s), CurrentBet + MinRaise);
            if (raiseTo < minLegal) raiseTo = raiseTo >= MaxRaiseTo(s) ? raiseTo : minLegal;  // allow short all-in only

            int add = raiseTo - s.Bet;
            s.Chips -= add; s.Bet += add; s.Committed += add;
            if (s.Chips == 0) s.AllIn = true;

            int raiseSize = s.Bet - CurrentBet;
            bool fullRaise = raiseSize >= MinRaise;
            if (fullRaise) MinRaise = raiseSize;
            CurrentBet = System.Math.Max(CurrentBet, s.Bet);
            if (raiseSize > 0) LastAggressor = s.Index;

            // TDA rule: only a FULL raise re-opens the action. A short all-in (less than a full raise) must be
            // called/folded by players yet to act, but players who already acted do NOT get a new raising turn.
            if (fullRaise)
                foreach (var o in Seats) { if (o != s && o.CanAct) o.ActedThisRound = false; }
            s.ActedThisRound = true;
            s.LastAction = s.AllIn ? $"All-in {s.Bet}" : $"Raise to {s.Bet}";
            Log.Add(s.AllIn ? $"{s.Name} raises to {s.Bet} (all-in)." : $"{s.Name} raises to {s.Bet}.");
            AfterAction();
        }

        private void AfterAction()
        {
            if (InHandCount <= 1) { ResolveFoldOut(); return; }
            if (RoundComplete()) { AdvancePhase(); return; }
            ToAct = NextActable(ToAct);
        }

        private bool RoundComplete()
        {
            foreach (var s in Seats)
                if (s.CanAct && (!s.ActedThisRound || s.Bet != CurrentBet)) return false;
            return true;
        }

        private void AdvancePhase()
        {
            // Reset the betting round.
            foreach (var s in Seats) { s.Bet = 0; s.ActedThisRound = false; s.LastAction = null; }
            CurrentBet = 0; MinRaise = BigBlind;

            switch (Phase)
            {
                case PokerPhase.Preflop: Board.Add(_deck.Draw()); Board.Add(_deck.Draw()); Board.Add(_deck.Draw()); Phase = PokerPhase.Flop; Log.Add($"Flop: {Board[0]} {Board[1]} {Board[2]}"); break;
                case PokerPhase.Flop: Board.Add(_deck.Draw()); Phase = PokerPhase.Turn; Log.Add($"Turn: {Board[3]}"); break;
                case PokerPhase.Turn: Board.Add(_deck.Draw()); Phase = PokerPhase.River; Log.Add($"River: {Board[4]}"); break;
                case PokerPhase.River: ResolveShowdown(); return;
            }
            LastAggressor = -1;   // fresh street — the river's final aggressor is preserved for showdown order
            ToAct = FirstToActPostflop();
            if (ToAct < 0) AdvancePhase();   // no one left to act (all-in run-out) — deal the next street
        }

        private void ResolveFoldOut()
        {
            var winner = Seats.First(s => s.InHand);
            int pot = Pot;
            winner.Chips += pot; winner.Won = pot;
            Results.Add(new PotResult { Desc = "Pot", Amount = pot, Winners = { winner.Index } });
            Log.Add($"{winner.Name} wins {pot} (all others folded).");
            Phase = PokerPhase.Complete;
            ToAct = -1;
        }

        private void ResolveShowdown()
        {
            while (Board.Count < 5) Board.Add(_deck.Draw());   // safety for all-in run-outs
            VerifyIntegrity("showdown");

            // Reveal order (TDA): the river's last aggressor shows first; checked around → first live seat left of
            // the button. Then clockwise.
            ShowOrder.Clear();
            int first = LastAggressor >= 0 && Seats[LastAggressor].InHand ? LastAggressor : -1;
            if (first < 0)
                for (int i = 1; i <= Seats.Count && first < 0; i++)
                { int j = (Button + i) % Seats.Count; if (Seats[j].InHand) first = j; }
            if (first >= 0)
                for (int i = 0; i < Seats.Count; i++)
                { int j = (first + i) % Seats.Count; if (Seats[j].InHand) ShowOrder.Add(j); }

            var scores = new Dictionary<int, long>();
            foreach (var s in Seats)
                if (s.InHand)
                {
                    var seven = new List<Card> { s.A, s.B }; seven.AddRange(Board);
                    scores[s.Index] = PokerHandEval.Evaluate(seven);
                    Log.Add($"{s.Name}: {s.A} {s.B} — {PokerHandEval.Describe(scores[s.Index])}");
                }

            // Side pots from the distinct commit levels.
            var contenders = Seats.Where(s => s.Committed > 0).ToList();
            var levels = contenders.Select(s => s.Committed).Distinct().OrderBy(v => v).ToList();
            int prev = 0;
            foreach (int level in levels)
            {
                int layer = level - prev; prev = level;
                var contribs = contenders.Where(s => s.Committed >= level).ToList();
                int amount = layer * contribs.Count;
                if (amount <= 0) continue;

                var eligible = contribs.Where(s => s.InHand).ToList();
                if (eligible.Count == 0) { foreach (var c in contribs) c.Chips += layer; continue; }   // refund uncalled

                long best = eligible.Max(s => scores[s.Index]);
                var winners = eligible.Where(s => scores[s.Index] == best).OrderBy(s => SeatOrderFromButton(s.Index)).ToList();
                int share = amount / winners.Count, rem = amount % winners.Count;
                var res = new PotResult { Desc = prev == levels.Last() && level == levels[0] ? "Pot" : $"Pot (≤{level})", Amount = amount };
                for (int i = 0; i < winners.Count; i++)
                {
                    int w = share + (i < rem ? 1 : 0);
                    winners[i].Chips += w; winners[i].Won += w; res.Winners.Add(winners[i].Index);
                }
                Results.Add(res);
                Log.Add($"{string.Join(", ", winners.Select(x => x.Name))} win {amount} ({PokerHandEval.Describe(best)}).");
            }
            Phase = PokerPhase.Complete;
            ToAct = -1;
        }

        /// <summary>Tripwire for the reported duplicate-card sighting: verifies every live card (all dealt holes +
        /// the board) is a distinct, valid deck index. On violation, logs and raises <see cref="IntegrityAlarm"/> with
        /// a full dump so the trigger is captured in the act.</summary>
        private void VerifyIntegrity(string where)
        {
            var seen = new HashSet<int>();
            string bad = null;
            foreach (var s in Seats)
            {
                if (!s.HasCards) continue;
                if (s.A.Index < 0 || s.A.Index > 51 || s.B.Index < 0 || s.B.Index > 51) { bad = $"{s.Name} holds an invalid card"; break; }
                if (s.A.Index == s.B.Index) { bad = $"{s.Name} was dealt the same card twice ({s.A})"; break; }
                if (!seen.Add(s.A.Index)) { bad = $"{s.A} dealt to two seats ({s.Name})"; break; }
                if (!seen.Add(s.B.Index)) { bad = $"{s.B} dealt to two seats ({s.Name})"; break; }
            }
            if (bad == null)
                foreach (var c in Board)
                    if (!seen.Add(c.Index)) { bad = $"board repeats {c}"; break; }
            if (bad == null) return;

            var dump = new System.Text.StringBuilder($"[Poker] CARD INTEGRITY VIOLATION at {where} (hand #{HandNumber}): {bad}. ");
            foreach (var s in Seats) if (s.HasCards) dump.Append($"{s.Name}={s.A},{s.B} ");
            dump.Append("board=").Append(string.Join(",", Board));
            Log.Add(dump.ToString());
            IntegrityAlarm?.Invoke(dump.ToString());
        }

        // ── seat cycling ────────────────────────────────────────────────────────────────────────────────────────
        private int NextOccupied(int from)
        {
            for (int i = 1; i <= Seats.Count; i++) { int j = (from + i) % Seats.Count; if (Seats[j].Occupied) return j; }
            return from;
        }

        private int NextActable(int from)
        {
            for (int i = 1; i <= Seats.Count; i++) { int j = (from + i) % Seats.Count; if (Seats[j].CanAct) return j; }
            return -1;
        }

        private int SeatOrderFromButton(int index)
        {
            int o = (index - Button - 1 + Seats.Count) % Seats.Count;
            return o;
        }
    }
}
