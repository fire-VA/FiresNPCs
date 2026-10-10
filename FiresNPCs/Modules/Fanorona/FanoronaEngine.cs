using System;
using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Fanorona
{
    public enum FanColor { White = 1, Black = 2 }

    /// <summary>One sliding step. Approach/Withdrawal mark the capture mode (both false = paika, the non-capturing
    /// move legal only when no capture exists anywhere). IsStop ends a capture chain voluntarily.</summary>
    public struct FanMove
    {
        public int From, To, Dir;
        public bool Approach, Withdrawal, IsStop;
        public bool IsCapture => Approach || Withdrawal;
    }

    /// <summary>
    /// Fanorona (fanorontsivy, the Malagasy 5×9 board): stones sit on intersections; orthogonal lines everywhere and
    /// diagonals only on the strong points ((x+y) even — the red-X intersections of the carved board). A move slides
    /// to an adjacent empty point. Captures: by APPROACH (the contiguous enemy line ahead of the destination dies) or
    /// WITHDRAWAL (the contiguous enemy line behind the vacated point dies) — mover's choice when both apply.
    /// Captures are MANDATORY when any exist; after a capture the same stone may chain further captures (never
    /// revisiting a point this turn, never twice consecutively in the same direction), stopping voluntarily any time.
    /// A side with no stones — or no legal move — loses. Pure C#; verified by <see cref="FanoronaTest"/>.
    /// </summary>
    public sealed class FanoronaGame
    {
        public const int W = 9, H = 5, Points = 45;

        public readonly int[] Board = new int[Points];   // 0 empty, 1 white, 2 black
        public FanColor SideToMove = FanColor.White;
        public int ChainStone = -1;
        public int ChainLastDir = -1;
        public readonly List<int> ChainVisited = new List<int>();
        public int MoveCount;
        public int LastRemoved;

        public static readonly int[] DX = { 1, -1, 0, 0, 1, -1, 1, -1 };
        public static readonly int[] DY = { 0, 0, 1, -1, 1, -1, -1, 1 };

        public static int PX(int p) => p % W;
        public static int PY(int p) => p / W;
        public static int Pt(int x, int y) => y * W + x;
        public static bool In(int x, int y) => x >= 0 && x < W && y >= 0 && y < H;
        public static bool Strong(int p) => ((p % W) + (p / W)) % 2 == 0;
        public static string Name(int p) => ((char)('a' + PX(p))).ToString() + (PY(p) + 1);

        public FanoronaGame() { SetStart(); }

        public void SetStart()
        {
            for (int i = 0; i < Points; i++) Board[i] = 0;
            for (int x = 0; x < W; x++)
            {
                Board[Pt(x, 0)] = 1; Board[Pt(x, 1)] = 1;   // white — bottom two rows
                Board[Pt(x, 3)] = 2; Board[Pt(x, 4)] = 2;   // black — top two rows
            }
            int[] mid = { 2, 1, 2, 1, 0, 1, 2, 1, 2 };       // B W B W · W B W B (mirror-symmetric middle row)
            for (int x = 0; x < W; x++) Board[Pt(x, 2)] = mid[x];

            SideToMove = FanColor.White;
            ChainStone = -1; ChainLastDir = -1; ChainVisited.Clear();
            MoveCount = 0; LastRemoved = 0;
        }

        public int CountStones(FanColor c) { int n = 0; for (int i = 0; i < Points; i++) if (Board[i] == (int)c) n++; return n; }

        /// <summary>All legal moves for the side to move. Mid-chain: only that stone's further captures + Stop.</summary>
        public List<FanMove> LegalMoves()
        {
            var list = new List<FanMove>();
            int me = (int)SideToMove, enemy = 3 - me;

            if (ChainStone >= 0)
            {
                AddMovesFor(ChainStone, list, enemy, capturesOnly: true, chain: true);
                list.Add(new FanMove { IsStop = true, From = ChainStone, To = ChainStone });
                return list;
            }

            for (int p = 0; p < Points; p++)
                if (Board[p] == me) AddMovesFor(p, list, enemy, capturesOnly: true, chain: false);
            if (list.Count > 0) return list;   // captures are mandatory

            for (int p = 0; p < Points; p++)
                if (Board[p] == me) AddMovesFor(p, list, enemy, capturesOnly: false, chain: false);
            return list;
        }

        private void AddMovesFor(int p, List<FanMove> list, int enemy, bool capturesOnly, bool chain)
        {
            int dirs = Strong(p) ? 8 : 4;
            for (int d = 0; d < dirs; d++)
            {
                int nx = PX(p) + DX[d], ny = PY(p) + DY[d];
                if (!In(nx, ny)) continue;
                int to = Pt(nx, ny);
                if (Board[to] != 0) continue;
                if (chain)
                {
                    if (d == ChainLastDir) continue;              // not twice running in one direction
                    if (ChainVisited.Contains(to)) continue;      // never revisit a point this turn
                }

                int ax = nx + DX[d], ay = ny + DY[d];
                bool app = In(ax, ay) && Board[Pt(ax, ay)] == enemy;
                int wx = PX(p) - DX[d], wy = PY(p) - DY[d];
                bool wd = In(wx, wy) && Board[Pt(wx, wy)] == enemy;

                if (app) list.Add(new FanMove { From = p, To = to, Dir = d, Approach = true });
                if (wd) list.Add(new FanMove { From = p, To = to, Dir = d, Withdrawal = true });
                if (!app && !wd && !capturesOnly) list.Add(new FanMove { From = p, To = to, Dir = d });
            }
        }

        /// <summary>Apply a move. After a capture, the turn stays with the mover while further chain captures exist
        /// (ChainStone ≥ 0); Apply a Stop move (or a non-continuable capture) to pass the turn.</summary>
        public void Apply(FanMove m)
        {
            MoveCount++;
            if (m.IsStop) { EndTurn(); return; }

            int me = (int)SideToMove, enemy = 3 - me;
            bool wasChain = ChainStone >= 0;
            Board[m.To] = me; Board[m.From] = 0;

            int removed = 0;
            if (m.Approach)
            {
                int x = PX(m.To) + DX[m.Dir], y = PY(m.To) + DY[m.Dir];
                while (In(x, y) && Board[Pt(x, y)] == enemy) { Board[Pt(x, y)] = 0; removed++; x += DX[m.Dir]; y += DY[m.Dir]; }
            }
            else if (m.Withdrawal)
            {
                int x = PX(m.From) - DX[m.Dir], y = PY(m.From) - DY[m.Dir];
                while (In(x, y) && Board[Pt(x, y)] == enemy) { Board[Pt(x, y)] = 0; removed++; x -= DX[m.Dir]; y -= DY[m.Dir]; }
            }
            LastRemoved = removed;

            if (removed > 0)
            {
                if (!wasChain) { ChainVisited.Clear(); ChainVisited.Add(m.From); }
                ChainVisited.Add(m.To);
                ChainStone = m.To;
                ChainLastDir = m.Dir;
                var cont = new List<FanMove>();
                AddMovesFor(m.To, cont, enemy, capturesOnly: true, chain: true);
                if (cont.Count == 0) EndTurn();
            }
            else EndTurn();
        }

        private void EndTurn()
        {
            ChainStone = -1; ChainLastDir = -1; ChainVisited.Clear();
            SideToMove = SideToMove == FanColor.White ? FanColor.Black : FanColor.White;
        }

        /// <summary>Null while the game is live; else the winner (no stones or no legal move = loss).</summary>
        public FanColor? Winner()
        {
            if (CountStones(FanColor.White) == 0) return FanColor.Black;
            if (CountStones(FanColor.Black) == 0) return FanColor.White;
            if (LegalMoves().Count == 0) return SideToMove == FanColor.White ? FanColor.Black : FanColor.White;
            return null;
        }

        public FanoronaGame Clone()
        {
            var g = new FanoronaGame();
            Array.Copy(Board, g.Board, Points);
            g.SideToMove = SideToMove;
            g.ChainStone = ChainStone; g.ChainLastDir = ChainLastDir;
            g.ChainVisited.Clear(); g.ChainVisited.AddRange(ChainVisited);
            g.MoveCount = MoveCount;
            return g;
        }
    }
}
