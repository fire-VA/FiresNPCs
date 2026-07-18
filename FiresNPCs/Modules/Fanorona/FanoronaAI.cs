using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Fanorona
{
    /// <summary>
    /// Alpha-beta AI for Fanorona. Chains keep the turn (Apply leaves SideToMove unchanged while ChainStone ≥ 0), so
    /// the search only negates when the side actually flips. Eval = stone differential + a little mobility. Clone-based
    /// (no unmake) — the 45-int board makes copies cheap; safe on a worker thread against a Clone of the live game.
    /// </summary>
    public static class FanoronaAI
    {
        private const int Win = 100000;

        public static bool FindBest(FanoronaGame g, int depth, System.Random rng, out FanMove best)
        {
            best = default;
            var moves = g.LegalMoves();
            if (moves.Count == 0) return false;
            Order(g, moves);

            int alpha = -Win * 2, bestScore = int.MinValue;
            foreach (var m in moves)
            {
                var c = g.Clone(); c.Apply(m);
                int score = c.SideToMove == g.SideToMove
                    ? Search(c, depth, alpha, Win * 2, 1)
                    : -Search(c, depth - 1, -Win * 2, -alpha, 1);
                score += rng != null ? rng.Next(-3, 4) : 0;
                if (score > bestScore) { bestScore = score; best = m; }
                if (score > alpha) alpha = score;
            }
            return true;
        }

        private static int Search(FanoronaGame g, int depth, int alpha, int beta, int ply)
        {
            if (g.CountStones(g.SideToMove) == 0) return -Win + ply;
            int enemyStones = g.CountStones(g.SideToMove == FanColor.White ? FanColor.Black : FanColor.White);
            if (enemyStones == 0) return Win - ply;

            var moves = g.LegalMoves();
            if (moves.Count == 0) return -Win + ply;   // no move = loss
            if (depth <= 0 && ply < 40) return Eval(g);
            if (ply >= 40) return Eval(g);

            Order(g, moves);
            foreach (var m in moves)
            {
                var c = g.Clone(); c.Apply(m);
                int score = c.SideToMove == g.SideToMove
                    ? Search(c, depth, alpha, beta, ply + 1)          // chain continues — same perspective
                    : -Search(c, depth - 1, -beta, -alpha, ply + 1);
                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }
            return alpha;
        }

        private static void Order(FanoronaGame g, List<FanMove> moves)
        {
            moves.Sort((a, b) => Gain(g, b).CompareTo(Gain(g, a)));
        }

        // Immediate stones removed by the move — cheap MVV-style ordering.
        private static int Gain(FanoronaGame g, FanMove m)
        {
            if (m.IsStop) return 0;
            if (!m.IsCapture) return 0;
            int enemy = 3 - (int)g.SideToMove, n = 0;
            int x, y, dx, dy;
            if (m.Approach)
            {
                x = FanoronaGame.PX(m.To) + FanoronaGame.DX[m.Dir]; y = FanoronaGame.PY(m.To) + FanoronaGame.DY[m.Dir];
                dx = FanoronaGame.DX[m.Dir]; dy = FanoronaGame.DY[m.Dir];
            }
            else
            {
                x = FanoronaGame.PX(m.From) - FanoronaGame.DX[m.Dir]; y = FanoronaGame.PY(m.From) - FanoronaGame.DY[m.Dir];
                dx = -FanoronaGame.DX[m.Dir]; dy = -FanoronaGame.DY[m.Dir];
            }
            while (FanoronaGame.In(x, y) && g.Board[FanoronaGame.Pt(x, y)] == enemy) { n++; x += dx; y += dy; }
            return n;
        }

        /// <summary>Static eval from the side-to-move's perspective.</summary>
        public static int Eval(FanoronaGame g)
        {
            int me = (int)g.SideToMove, enemy = 3 - me;
            int mine = 0, theirs = 0;
            for (int i = 0; i < FanoronaGame.Points; i++)
            {
                if (g.Board[i] == me) mine++;
                else if (g.Board[i] == enemy) theirs++;
            }
            return (mine - theirs) * 100 + g.LegalMoves().Count;
        }
    }
}
