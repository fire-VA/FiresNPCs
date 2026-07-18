using System.Text;

namespace FiresRPGmaker.Modules.Chess
{
    /// <summary>
    /// Perft (performance test) — counts the leaf nodes of the legal move tree to a given depth. The start-position
    /// counts are a famous, exact fingerprint of a correct generator; if these match, castling/en-passant/promotion/
    /// check handling are all correct. Run before building any chess UI (mirrors PokerHandEval/PokerTest discipline).
    /// </summary>
    public static class ChessPerft
    {
        public static long Perft(ChessBoard b, int depth)
        {
            if (depth == 0) return 1;
            var moves = ChessMoveGen.GenerateLegal(b);
            if (depth == 1) return moves.Count;
            long nodes = 0;
            for (int i = 0; i < moves.Count; i++) { b.MakeMove(moves[i]); nodes += Perft(b, depth - 1); b.UnmakeMove(); }
            return nodes;
        }

        // Exact start-position perft counts.
        private static readonly long[] Expected = { 1, 20, 400, 8902, 197281, 4865609, 119060324 };

        public static string Run(int maxDepth = 4)
        {
            var b = new ChessBoard();
            var sb = new StringBuilder();
            bool allOk = true;
            for (int d = 1; d <= maxDepth; d++)
            {
                long n = Perft(b, d);
                bool known = d < Expected.Length;
                bool ok = !known || n == Expected[d];
                allOk &= ok;
                sb.AppendLine($"perft({d}) = {n}   expected {(known ? Expected[d].ToString() : "?")}   {(ok ? "OK" : "FAIL")}");
            }
            sb.AppendLine(allOk ? "ALL PERFT OK" : "PERFT MISMATCH");
            return sb.ToString();
        }
    }
}
