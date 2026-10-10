using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Poker
{
    /// <summary>
    /// A serviceable NPC poker brain: gauge hand strength (pre-flop hole heuristic; post-flop the real evaluator
    /// category), weigh it against the pot odds of the current call, and fold / check-call / raise accordingly with a
    /// dash of randomness so it isn't a read-open robot. Drives the current seat through the same <see cref="PokerGame"/>
    /// action methods a human uses — no special path. Not a solver, just a fair, fast table opponent.
    /// </summary>
    public static class PokerAI
    {
        private static readonly float[] CatStrength = { 0.16f, 0.36f, 0.56f, 0.70f, 0.80f, 0.86f, 0.92f, 0.97f, 1.00f };

        /// <summary>Table strength: Easy / Medium / Hard / Master (hot-reloads).</summary>
        public static BepInEx.Configuration.ConfigEntry<string> DifficultyCfg;

        private struct Tier { public float Noise, RaiseThresh, ValueRaise, CallCushion, Bluff; }

        // Noise = read inaccuracy band; CallCushion = edge over pot odds demanded before calling
        // (Easy is NEGATIVE — the classic calling station); Bluff = no-equity raise chance.
        private static Tier GetTier()
        {
            switch ((DifficultyCfg?.Value ?? "Medium").Trim().ToLowerInvariant())
            {
                case "easy": return new Tier { Noise = 0.26f, RaiseThresh = 0.72f, ValueRaise = 0.40f, CallCushion = -0.06f, Bluff = 0.02f };
                case "hard": return new Tier { Noise = 0.10f, RaiseThresh = 0.58f, ValueRaise = 0.80f, CallCushion = 0.06f, Bluff = 0.08f };
                case "master": return new Tier { Noise = 0.06f, RaiseThresh = 0.55f, ValueRaise = 0.88f, CallCushion = 0.08f, Bluff = 0.12f };
                default: return new Tier { Noise = 0.16f, RaiseThresh = 0.62f, ValueRaise = 0.70f, CallCushion = 0.04f, Bluff = 0.05f };
            }
        }

        public static void TakeTurn(PokerGame g, System.Random rng)
        {
            var s = g.Current;
            if (s == null) return;

            var t = GetTier();
            float strength = Strength(g, s) + (float)(rng.NextDouble() * t.Noise * 2 - t.Noise);
            int call = g.ToCall(s);
            int pot = g.Pot;

            if (call == 0)
            {
                if (strength > t.RaiseThresh && g.CanRaise(s)) g.Raise(BetTo(g, s, pot, 0.6f));
                else if (strength > t.RaiseThresh - 0.12f && g.CanRaise(s) && rng.NextDouble() < 0.28) g.Raise(BetTo(g, s, pot, 0.45f));
                else g.CheckCall();
            }
            else
            {
                float potOdds = (float)call / (pot + call);
                if (strength > 0.8f && g.CanRaise(s) && rng.NextDouble() < t.ValueRaise) g.Raise(BetTo(g, s, pot, 0.8f));
                else if (strength >= potOdds + t.CallCushion) g.CheckCall();
                else if (g.CanRaise(s) && rng.NextDouble() < t.Bluff) g.Raise(BetTo(g, s, pot, 0.7f));
                else g.Fold();
            }
        }

        private static int BetTo(PokerGame g, PokerSeat s, int pot, float frac)
        {
            int inc = System.Math.Max(g.MinRaise, (int)(pot * frac));
            return System.Math.Min(g.MaxRaiseTo(s), g.CurrentBet + inc);
        }

        private static float Strength(PokerGame g, PokerSeat s)
        {
            if (g.Board.Count == 0) return Preflop(s);
            var seven = new List<Card> { s.A, s.B };
            seven.AddRange(g.Board);
            int cat = PokerHandEval.Category(PokerHandEval.Evaluate(seven));
            return CatStrength[cat];
        }

        // Rough pre-flop hole strength ~0..1: pairs scale by rank; otherwise high/low card, suited + connected bonuses.
        private static float Preflop(PokerSeat s)
        {
            int hi = System.Math.Max(s.A.Rank, s.B.Rank);
            int lo = System.Math.Min(s.A.Rank, s.B.Rank);
            if (s.A.Rank == s.B.Rank) return 0.5f + s.A.Rank / 28f;

            float v = hi / 14f * 0.42f + lo / 14f * 0.22f;
            if (s.A.Suit == s.B.Suit) v += 0.08f;
            int gap = hi - lo;
            if (gap == 1) v += 0.06f; else if (gap == 2) v += 0.03f;
            return v;
        }
    }
}
