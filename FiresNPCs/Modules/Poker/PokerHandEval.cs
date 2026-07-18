using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Poker
{
    /// <summary>
    /// Texas Hold'em hand strength. <see cref="Evaluate"/> takes 5–7 cards (2 hole + up to 5 board) and returns the
    /// best 5-card hand as a single comparable score (higher = stronger) — enumerate every 5-card subset, score each,
    /// keep the max. The score packs the category in the high bits and five tiebreak ranks below it, so two scores
    /// compare exactly like poker hands (kickers included). The wheel (A-2-3-4-5) is handled as a five-high straight.
    /// Pure logic, no Unity — unit-testable via firesnpcs_pokertest.
    /// </summary>
    public static class PokerHandEval
    {
        public static readonly string[] CategoryNames =
        {
            "High Card", "Pair", "Two Pair", "Three of a Kind", "Straight",
            "Flush", "Full House", "Four of a Kind", "Straight Flush",
        };

        public static long Evaluate(IReadOnlyList<Card> cards)
        {
            long best = -1;
            int n = cards.Count;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                    for (int c = b + 1; c < n; c++)
                        for (int d = c + 1; d < n; d++)
                            for (int e = d + 1; e < n; e++)
                            {
                                long s = Score5(cards[a], cards[b], cards[c], cards[d], cards[e]);
                                if (s > best) best = s;
                            }
            return best;
        }

        public static int Category(long score) => (int)(score >> 20);
        public static string Describe(long score) => score < 0 ? "-" : CategoryNames[Category(score)];

        // Score exactly five cards. Category (0–8) in bits 20+, then five tiebreak ranks (4 bits each) ordered so a
        // plain long-compare ranks hands correctly.
        public static long Score5(Card c0, Card c1, Card c2, Card c3, Card c4)
        {
            var ranks = new[] { c0.Rank, c1.Rank, c2.Rank, c3.Rank, c4.Rank };
            bool flush = c0.Suit == c1.Suit && c1.Suit == c2.Suit && c2.Suit == c3.Suit && c3.Suit == c4.Suit;

            var cnt = new int[15];
            foreach (var r in ranks) cnt[r]++;

            // Distinct ranks high→low; a straight needs five distinct, consecutive (or the A-5-4-3-2 wheel).
            var distinct = new List<int>();
            for (int r = 14; r >= 2; r--) if (cnt[r] > 0) distinct.Add(r);

            bool straight = false; int straightHigh = 0;
            if (distinct.Count == 5)
            {
                if (distinct[0] - distinct[4] == 4) { straight = true; straightHigh = distinct[0]; }
                else if (distinct[0] == 14 && distinct[1] == 5 && distinct[4] == 2) { straight = true; straightHigh = 5; }
            }

            // Counts high→low decide the category (quads [4,1], full house [3,2], …).
            var counts = new List<int>();
            for (int r = 14; r >= 2; r--) if (cnt[r] > 0) counts.Add(cnt[r]);
            counts.Sort((x, y) => y - x);

            int cat;
            if (straight && flush) cat = 8;
            else if (counts[0] == 4) cat = 7;
            else if (counts[0] == 3 && counts.Count > 1 && counts[1] == 2) cat = 6;
            else if (flush) cat = 5;
            else if (straight) cat = 4;
            else if (counts[0] == 3) cat = 3;
            else if (counts[0] == 2 && counts.Count > 1 && counts[1] == 2) cat = 2;
            else if (counts[0] == 2) cat = 1;
            else cat = 0;

            int[] packed;
            if (cat == 8 || cat == 4)
                packed = new[] { straightHigh, straightHigh - 1, straightHigh - 2, straightHigh - 3, straightHigh - 4 };
            else
            {
                // Order the five ranks by count desc, then rank desc — pairs/trips/quads bubble to the front, kickers
                // follow in strength order. (Flush non-straight: all counts 1, so this is plain rank-desc.)
                var order = new List<int>(ranks);
                order.Sort((x, y) => cnt[x] != cnt[y] ? cnt[y] - cnt[x] : y - x);
                packed = order.ToArray();
            }

            return ((long)cat << 20)
                 | ((long)packed[0] << 16) | ((long)packed[1] << 12) | ((long)packed[2] << 8)
                 | ((long)packed[3] << 4) | (long)packed[4];
        }
    }
}
