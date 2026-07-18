using System.Collections.Generic;
using System.Text;

namespace FiresRPGmaker.Modules.Poker
{
    /// <summary>Self-check for <see cref="PokerHandEval"/> — asserts the category ranking, the wheel straight, and
    /// kicker comparisons, then shows one 7-card showdown. Run via the firesnpcs_pokertest console command.</summary>
    public static class PokerTest
    {
        public static string Run()
        {
            var sb = new StringBuilder("[Poker] hand-evaluator self-check\n");
            int pass = 0, fail = 0;
            void Chk(string n, bool ok) { if (ok) pass++; else fail++; sb.Append(ok ? "  PASS  " : "  FAIL  ").Append(n).Append('\n'); }

            long royal = S("As Ks Qs Js Ts");
            long stflush = S("9s 8s 7s 6s 5s");
            long quads = S("9c 9d 9h 9s Kc");
            long boat = S("Ac Ad Ah Kc Kd");
            long flush = S("Ah Jh 8h 5h 2h");
            long straight = S("Tc 9d 8h 7s 6c");
            long wheel = S("Ac 2d 3h 4s 5c");
            long sixhi = S("2c 3d 4h 5s 6c");
            long trips = S("Qc Qd Qh 8s 2c");
            long twopair = S("Ac Ad Kc Kd Qh");
            long twopairLoK = S("Ac Ad Kc Kd Jh");
            long pair = S("Ac Ad Kc Qd Jh");
            long pairLoK = S("Ac Ad Kc Qd Th");
            long high = S("Ac Kd Qh Js 9c");

            Chk("royal flush is a straight flush", Cat(royal) == 8);
            Chk("royal > lower straight flush", royal > stflush);
            Chk("straight flush > quads", stflush > quads);
            Chk("quads > full house", quads > boat);
            Chk("full house > flush", boat > flush);
            Chk("flush > straight", flush > straight);
            Chk("straight > trips", straight > trips);
            Chk("trips > two pair", trips > twopair);
            Chk("two pair > pair", twopair > pair);
            Chk("pair > high card", pair > high);
            Chk("wheel A-2-3-4-5 is a straight", Cat(wheel) == 4);
            Chk("six-high straight beats the wheel", sixhi > wheel);
            Chk("two pair kicker: AAKK-Q > AAKK-J", twopair > twopairLoK);
            Chk("pair kicker: AA-KQJ > AA-KQT", pair > pairLoK);

            // 7-card: a board flush must be found from the best five.
            long seven = E("Ah Kh   Qh Jh 2h 7c 3d");
            Chk("7-card finds the board flush", Cat(seven) == 5);

            // Sample showdown.
            long p1 = E("As Ad   Ks 7d 2c 9h 4s");   // pair of aces
            long p2 = E("Kc Kd   Ks 7d 2c 9h 4s");   // set of kings
            sb.Append($"  showdown: P1 {PokerHandEval.Describe(p1)} vs P2 {PokerHandEval.Describe(p2)} -> ")
              .Append(p2 > p1 ? "P2 wins" : "P1 wins").Append('\n');

            sb.Append($"[Poker] {pass} passed, {fail} failed.");
            return sb.ToString();
        }

        /// <summary>Play N all-AI hands and report chip conservation (the pot math must balance to the chip) + the
        /// last hand's log — end-to-end proof the betting engine plays legal poker without stalling.</summary>
        public static string Sim(int hands)
        {
            var rng = new System.Random(20260708);
            var g = new PokerGame(rng) { SmallBlind = 10, BigBlind = 20 };
            for (int i = 0; i < 4; i++) g.Seats.Add(new PokerSeat { Index = i, Name = "AI" + (i + 1), IsAI = true, Chips = 1000 });
            int start = 0; foreach (var s in g.Seats) start += s.Chips;

            var sb = new StringBuilder("[Poker] engine simulation (4 AI, 1000 chips, 10/20)\n");
            int played = 0;
            for (int h = 0; h < hands; h++)
            {
                int liveSeats = 0; foreach (var s in g.Seats) if (s.Chips > 0) liveSeats++;
                if (liveSeats < 2) break;
                g.StartHand();
                int guard = 0;
                while (g.HandInProgress && guard++ < 2000) PokerAI.TakeTurn(g, rng);
                if (guard >= 2000) { sb.Append("  ERROR: hand did not terminate (stuck).\n"); break; }
                played++;
            }
            int end = 0; foreach (var s in g.Seats) end += s.Chips;

            sb.Append($"  played {played} hand(s)\n");
            foreach (var s in g.Seats) sb.Append($"    {s.Name}: {s.Chips} chips{(s.Chips <= 0 ? " (busted)" : "")}\n");
            sb.Append($"  chip conservation: {start} -> {end}  [{(start == end ? "OK" : "MISMATCH!")}]\n");
            sb.Append("  last hand:\n");
            foreach (var line in g.Log) sb.Append("    " + line + "\n");
            return sb.ToString();
        }

        /// <summary>Card-integrity stress: many seeds × many tables, deliberately including short stacks (blind
        /// all-ins — the reported duplicate sighting's scenario). Every hand asserts all dealt cards + board are
        /// distinct valid deck indexes and that no seat holds the same card twice. The engine's own tripwire
        /// (<see cref="PokerGame.IntegrityAlarm"/>) also fires during the run if it ever sees a violation.</summary>
        public static string SimDup(int handsPerSeed)
        {
            var sb = new StringBuilder("[Poker] card-integrity stress (multi-seed, short-stack heavy)\n");
            int totalHands = 0, bad = 0;
            string alarm = null;
            PokerGame.IntegrityAlarm = msg => alarm = alarm ?? msg;

            int[][] stackSets = { new[] { 1000, 1000, 1000, 1000 }, new[] { 25, 1000, 35, 800 }, new[] { 15, 30, 45, 60 } };
            for (int seed = 1; seed <= 30; seed++)
                foreach (var stacks in stackSets)
                {
                    var rng = new System.Random(seed * 7919 + stacks[0]);
                    var g = new PokerGame(rng) { SmallBlind = 10, BigBlind = 20 };
                    for (int i = 0; i < 4; i++) g.Seats.Add(new PokerSeat { Index = i, Name = "AI" + (i + 1), IsAI = true, Chips = stacks[i] });

                    for (int h = 0; h < handsPerSeed; h++)
                    {
                        int liveSeats = 0; foreach (var s in g.Seats) if (s.Chips > 0) liveSeats++;
                        if (liveSeats < 2) break;
                        g.StartHand();
                        int guard = 0;
                        while (g.HandInProgress && guard++ < 2000) PokerAI.TakeTurn(g, rng);
                        totalHands++;

                        var seen = new HashSet<int>();
                        var cards = new List<Card>();
                        foreach (var s in g.Seats) if (s.HasCards) { cards.Add(s.A); cards.Add(s.B); }
                        cards.AddRange(g.Board);
                        bool dup = false;
                        foreach (var c in cards) { if (c.Index < 0 || c.Index > 51 || !seen.Add(c.Index)) { dup = true; break; } }
                        foreach (var s in g.Seats) if (s.HasCards && s.A.Index == s.B.Index) dup = true;
                        if (dup) { bad++; if (bad <= 6) sb.Append($"  BAD seed-hand: {string.Join(" ", cards)}\n"); }
                    }
                }

            PokerGame.IntegrityAlarm = null;
            sb.Append($"  {totalHands} hands across 90 tables; {bad} violation(s).\n");
            if (alarm != null) sb.Append("  ENGINE TRIPWIRE: ").Append(alarm).Append('\n');
            sb.Append(bad == 0 && alarm == null ? "  ALL CARDS UNIQUE — EVERY HAND\n" : "  PROBLEM FOUND\n");
            return sb.ToString();
        }

        private static int Cat(long score) => PokerHandEval.Category(score);

        private static long S(string five)
        {
            var c = Parse(five);
            return PokerHandEval.Score5(c[0], c[1], c[2], c[3], c[4]);
        }

        private static long E(string cards) => PokerHandEval.Evaluate(Parse(cards));

        private static List<Card> Parse(string s)
        {
            var list = new List<Card>();
            foreach (var tok in s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                int rank;
                switch (char.ToUpperInvariant(tok[0]))
                {
                    case 'A': rank = 14; break; case 'K': rank = 13; break; case 'Q': rank = 12; break;
                    case 'J': rank = 11; break; case 'T': rank = 10; break; default: rank = tok[0] - '0'; break;
                }
                Suit suit;
                switch (char.ToLowerInvariant(tok[1]))
                {
                    case 'c': suit = Suit.Clubs; break; case 'd': suit = Suit.Diamonds; break;
                    case 'h': suit = Suit.Hearts; break; default: suit = Suit.Spades; break;
                }
                list.Add(new Card(rank, suit));
            }
            return list;
        }
    }
}
