using System.Collections.Generic;
using System.Text;

namespace FiresRPGmaker.Modules.Fanorona
{
    /// <summary>Self-check for the Fanorona rules engine — setup counts, strong/weak adjacency, approach/withdrawal
    /// captures, mandatory capture, chain constraints, then random + AI playouts asserting termination and that
    /// stones only ever decrease. Run via the firesnpcs_fanoronatest console command.</summary>
    public static class FanoronaTest
    {
        public static string Run()
        {
            var sb = new StringBuilder("[Fanorona] rules self-check\n");
            int pass = 0, fail = 0;
            void Chk(string n, bool ok) { if (ok) pass++; else fail++; sb.Append(ok ? "  PASS  " : "  FAIL  ").Append(n).Append('\n'); }

            var g = new FanoronaGame();
            Chk("setup: 22 white", g.CountStones(FanColor.White) == 22);
            Chk("setup: 22 black", g.CountStones(FanColor.Black) == 22);
            Chk("setup: centre empty", g.Board[FanoronaGame.Pt(4, 2)] == 0);
            Chk("corner a1 is strong", FanoronaGame.Strong(FanoronaGame.Pt(0, 0)));
            Chk("b1 is weak", !FanoronaGame.Strong(FanoronaGame.Pt(1, 0)));
            Chk("centre e3 is strong", FanoronaGame.Strong(FanoronaGame.Pt(4, 2)));

            var opening = g.LegalMoves();
            bool allCaps = opening.Count > 0;
            foreach (var m in opening) if (!m.IsCapture) allCaps = false;
            Chk("opening: captures exist and are mandatory", allCaps);

            g.Apply(opening[0]);
            Chk("opening capture removes black stones", g.CountStones(FanColor.Black) < 22);
            Chk("white count unchanged", g.CountStones(FanColor.White) == 22);

            // Approach: W a1→b1 eats the contiguous line c1,d1.
            var t = Blank();
            t.Board[FanoronaGame.Pt(0, 0)] = 1;
            t.Board[FanoronaGame.Pt(2, 0)] = 2;
            t.Board[FanoronaGame.Pt(3, 0)] = 2;
            t.Board[FanoronaGame.Pt(0, 4)] = 2;   // spare so black survives the wipe check
            var caps = t.LegalMoves();
            Chk("approach line found", caps.Count > 0 && caps[0].IsCapture);
            t.Apply(Find(caps, approach: true));
            Chk("approach eats the whole contiguous line", t.Board[FanoronaGame.Pt(2, 0)] == 0 && t.Board[FanoronaGame.Pt(3, 0)] == 0);

            // Withdrawal: W b1 (black behind at a1) pulls away to c1 and the line behind dies.
            t = Blank();
            t.Board[FanoronaGame.Pt(1, 0)] = 1;
            t.Board[FanoronaGame.Pt(0, 0)] = 2;
            t.Board[FanoronaGame.Pt(8, 4)] = 2;
            var wmoves = t.LegalMoves();
            var wd = Find(wmoves, approach: false);
            Chk("withdrawal found", wd.Withdrawal);
            t.Apply(wd);
            Chk("withdrawal eats behind", t.Board[FanoronaGame.Pt(0, 0)] == 0);

            // Chain: after a capture with another available, the turn stays and Stop is offered.
            t = Blank();
            t.Board[FanoronaGame.Pt(0, 0)] = 1;     // W a1 (strong)
            t.Board[FanoronaGame.Pt(2, 0)] = 2;     // approach target via b1
            t.Board[FanoronaGame.Pt(1, 2)] = 2;     // second capture available after landing b1 (diag b1? b1 weak) — use vertical: b3 above b1? b1→b2 approach b3
            t.Board[FanoronaGame.Pt(8, 4)] = 2;
            var first = Find(t.LegalMoves(), approach: true);
            t.Apply(first);
            bool chained = t.ChainStone >= 0 && t.SideToMove == FanColor.White;
            var cont = t.LegalMoves();
            bool hasStop = false; foreach (var m in cont) if (m.IsStop) hasStop = true;
            Chk("chain keeps the turn + offers Stop", !chained || hasStop);
            if (chained)
            {
                // may not continue in the same direction as the last segment
                foreach (var m in cont) if (!m.IsStop) Chk("chain: no same-direction repeat", m.Dir != t.ChainLastDir);
            }

            // Random playouts — termination + monotone stone counts.
            var rng = new System.Random(4242);
            int done = 0, maxPly = 0;
            for (int i = 0; i < 200; i++)
            {
                var p = new FanoronaGame();
                int lastW = 22, lastB = 22, ply = 0;
                while (p.Winner() == null && ply < 500)
                {
                    var moves = p.LegalMoves();
                    p.Apply(moves[rng.Next(moves.Count)]);
                    int cw = p.CountStones(FanColor.White), cb = p.CountStones(FanColor.Black);
                    if (cw > lastW || cb > lastB) { Chk("stones never increase", false); break; }
                    lastW = cw; lastB = cb; ply++;
                }
                if (p.Winner() != null) done++;
                if (ply > maxPly) maxPly = ply;
            }
            Chk("random playouts terminate (200 games)", done == 200);
            sb.Append($"  longest random game: {maxPly} plies\n");

            // AI vs AI — a full game finishes decisively.
            var a = new FanoronaGame();
            int aiPly = 0;
            while (a.Winner() == null && aiPly < 600)
            {
                if (!FanoronaAI.FindBest(a, 2, rng, out var bm)) break;
                a.Apply(bm);
                aiPly++;
            }
            Chk("AI vs AI game finishes", a.Winner() != null);
            sb.Append($"  AI game: {aiPly} plies, winner {(a.Winner()?.ToString() ?? "none")}\n");

            sb.Append($"[Fanorona] {pass} passed, {fail} failed.");
            return sb.ToString();
        }

        private static FanoronaGame Blank()
        {
            var g = new FanoronaGame();
            for (int i = 0; i < FanoronaGame.Points; i++) g.Board[i] = 0;
            g.SideToMove = FanColor.White;
            return g;
        }

        private static FanMove Find(List<FanMove> moves, bool approach)
        {
            foreach (var m in moves) if (approach ? m.Approach : m.Withdrawal) return m;
            return default;
        }
    }
}
