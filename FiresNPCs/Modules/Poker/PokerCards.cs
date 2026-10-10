using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Poker
{
    public enum Suit { Clubs = 0, Diamonds = 1, Hearts = 2, Spades = 3 }

    /// <summary>A single playing card. Rank 2..14 (11=J, 12=Q, 13=K, 14=A). Index 0..51 = (rank-2)*4 + suit,
    /// the canonical ordering used to map to the Kenney card sprites.</summary>
    public readonly struct Card
    {
        public readonly int Rank;
        public readonly Suit Suit;

        public Card(int rank, Suit suit) { Rank = rank; Suit = suit; }

        public int Index => (Rank - 2) * 4 + (int)Suit;
        public static Card FromIndex(int i) => new Card(i / 4 + 2, (Suit)(i % 4));

        public char RankChar()
        {
            switch (Rank)
            {
                case 14: return 'A';
                case 13: return 'K';
                case 12: return 'Q';
                case 11: return 'J';
                case 10: return 'T';
                default: return (char)('0' + Rank);
            }
        }

        public char SuitChar() => "cdhs"[(int)Suit];
        public override string ToString() => $"{RankChar()}{SuitChar()}";
    }

    /// <summary>A 52-card deck with a Fisher-Yates shuffle over a caller-supplied RNG (so a server can drive a
    /// deterministic, auditable deal). Draw pops from the top; Remaining reports what's left.</summary>
    public sealed class Deck
    {
        private readonly int[] _cards = new int[52];
        private int _next;
        private readonly System.Random _rng;

        public Deck(System.Random rng)
        {
            _rng = rng ?? new System.Random();
            Shuffle();
        }

        public void Shuffle()
        {
            for (int i = 0; i < 52; i++) _cards[i] = i;
            for (int i = 51; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                int t = _cards[i]; _cards[i] = _cards[j]; _cards[j] = t;
            }
            _next = 0;
        }

        public int Remaining => 52 - _next;

        public Card Draw()
        {
            if (_next >= 52) Shuffle();
            return Card.FromIndex(_cards[_next++]);
        }

        public void Deal(List<Card> into, int count)
        {
            for (int i = 0; i < count; i++) into.Add(Draw());
        }
    }
}
