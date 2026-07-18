using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Chess
{
    /// <summary>
    /// Move generation + attack detection for <see cref="ChessBoard"/>. Pseudo-legal moves are filtered to legal by
    /// making each move and checking the mover's king isn't left in check. Verified by <see cref="ChessPerft"/>.
    /// </summary>
    public static class ChessMoveGen
    {
        private static readonly int[] KnightF = { 1, 2, 2, 1, -1, -2, -2, -1 };
        private static readonly int[] KnightR = { 2, 1, -1, -2, -2, -1, 1, 2 };
        private static readonly int[] KingF = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] KingR = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] DiagF = { 1, 1, -1, -1 }, DiagR = { 1, -1, 1, -1 };
        private static readonly int[] OrthF = { 1, -1, 0, 0 }, OrthR = { 0, 0, 1, -1 };
        private static readonly int[] Side = { -1, 1 };

        public static bool IsSquareAttacked(ChessBoard b, int sq, ChessColor by)
        {
            int f = ChessBoard.FileOf(sq), r = ChessBoard.RankOf(sq);

            // Pawn: an attacking pawn of `by` sits one rank toward its own side, on an adjacent file.
            int pr = by == ChessColor.White ? r - 1 : r + 1;
            if (pr >= 0 && pr < 8)
                foreach (int df in Side)
                {
                    int pf = f + df;
                    if (pf < 0 || pf > 7) continue;
                    int c = b.Squares[ChessBoard.Sq(pf, pr)];
                    if (c != 0 && ChessBoard.ColorOf(c) == by && ChessBoard.TypeOf(c) == ChessPiece.Pawn) return true;
                }

            for (int i = 0; i < 8; i++)
            {
                int nf = f + KnightF[i], nr = r + KnightR[i];
                if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
                int c = b.Squares[ChessBoard.Sq(nf, nr)];
                if (c != 0 && ChessBoard.ColorOf(c) == by && ChessBoard.TypeOf(c) == ChessPiece.Knight) return true;
            }
            for (int i = 0; i < 8; i++)
            {
                int nf = f + KingF[i], nr = r + KingR[i];
                if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
                int c = b.Squares[ChessBoard.Sq(nf, nr)];
                if (c != 0 && ChessBoard.ColorOf(c) == by && ChessBoard.TypeOf(c) == ChessPiece.King) return true;
            }
            if (RayHits(b, f, r, DiagF, DiagR, by, ChessPiece.Bishop)) return true;
            if (RayHits(b, f, r, OrthF, OrthR, by, ChessPiece.Rook)) return true;
            return false;
        }

        private static bool RayHits(ChessBoard b, int f, int r, int[] df, int[] dr, ChessColor by, ChessPiece slider)
        {
            for (int i = 0; i < 4; i++)
            {
                int nf = f, nr = r;
                while (true)
                {
                    nf += df[i]; nr += dr[i];
                    if (nf < 0 || nf > 7 || nr < 0 || nr > 7) break;
                    int c = b.Squares[ChessBoard.Sq(nf, nr)];
                    if (c == 0) continue;
                    if (ChessBoard.ColorOf(c) == by)
                    {
                        var t = ChessBoard.TypeOf(c);
                        if (t == slider || t == ChessPiece.Queen) return true;
                    }
                    break;
                }
            }
            return false;
        }

        public static bool InCheck(ChessBoard b, ChessColor c)
        {
            int k = b.KingSquare(c);
            return k >= 0 && IsSquareAttacked(b, k, c == ChessColor.White ? ChessColor.Black : ChessColor.White);
        }

        public static List<ChessMove> GenerateLegal(ChessBoard b)
        {
            var pseudo = GeneratePseudo(b);
            var legal = new List<ChessMove>(pseudo.Count);
            var us = b.SideToMove;
            foreach (var m in pseudo)
            {
                b.MakeMove(m);
                if (!InCheck(b, us)) legal.Add(m);
                b.UnmakeMove();
            }
            return legal;
        }

        public static List<ChessMove> GeneratePseudo(ChessBoard b)
        {
            var list = new List<ChessMove>(48);
            var us = b.SideToMove;
            for (int sq = 0; sq < 64; sq++)
            {
                int code = b.Squares[sq];
                if (code == 0 || ChessBoard.ColorOf(code) != us) continue;
                switch (ChessBoard.TypeOf(code))
                {
                    case ChessPiece.Pawn: PawnMoves(b, sq, us, list); break;
                    case ChessPiece.Knight: StepMoves(b, sq, us, KnightF, KnightR, list); break;
                    case ChessPiece.King: StepMoves(b, sq, us, KingF, KingR, list); CastleMoves(b, sq, us, list); break;
                    case ChessPiece.Bishop: SlideMoves(b, sq, us, DiagF, DiagR, list); break;
                    case ChessPiece.Rook: SlideMoves(b, sq, us, OrthF, OrthR, list); break;
                    case ChessPiece.Queen: SlideMoves(b, sq, us, DiagF, DiagR, list); SlideMoves(b, sq, us, OrthF, OrthR, list); break;
                }
            }
            return list;
        }

        private static void StepMoves(ChessBoard b, int sq, ChessColor us, int[] df, int[] dr, List<ChessMove> list)
        {
            int f = ChessBoard.FileOf(sq), r = ChessBoard.RankOf(sq);
            for (int i = 0; i < df.Length; i++)
            {
                int nf = f + df[i], nr = r + dr[i];
                if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
                int to = ChessBoard.Sq(nf, nr);
                if (b.Squares[to] != 0 && ChessBoard.ColorOf(b.Squares[to]) == us) continue;
                list.Add(new ChessMove(sq, to));
            }
        }

        private static void SlideMoves(ChessBoard b, int sq, ChessColor us, int[] df, int[] dr, List<ChessMove> list)
        {
            int f = ChessBoard.FileOf(sq), r = ChessBoard.RankOf(sq);
            for (int i = 0; i < df.Length; i++)
            {
                int nf = f, nr = r;
                while (true)
                {
                    nf += df[i]; nr += dr[i];
                    if (nf < 0 || nf > 7 || nr < 0 || nr > 7) break;
                    int to = ChessBoard.Sq(nf, nr);
                    int c = b.Squares[to];
                    if (c == 0) { list.Add(new ChessMove(sq, to)); continue; }
                    if (ChessBoard.ColorOf(c) != us) list.Add(new ChessMove(sq, to));
                    break;
                }
            }
        }

        private static void PawnMoves(ChessBoard b, int sq, ChessColor us, List<ChessMove> list)
        {
            int f = ChessBoard.FileOf(sq), r = ChessBoard.RankOf(sq);
            int dir = us == ChessColor.White ? 1 : -1;
            int startRank = us == ChessColor.White ? 1 : 6;
            int promoRank = us == ChessColor.White ? 7 : 0;

            int r1 = r + dir;
            if (r1 >= 0 && r1 < 8 && b.Squares[ChessBoard.Sq(f, r1)] == 0)
            {
                AddPawn(sq, ChessBoard.Sq(f, r1), r1 == promoRank, list);
                if (r == startRank && b.Squares[ChessBoard.Sq(f, r + 2 * dir)] == 0)
                    list.Add(new ChessMove(sq, ChessBoard.Sq(f, r + 2 * dir), ChessPiece.None, false, false, true));
            }
            foreach (int cf in Side)
            {
                int nf = f + cf, nr = r + dir;
                if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
                int to = ChessBoard.Sq(nf, nr);
                int c = b.Squares[to];
                if (c != 0 && ChessBoard.ColorOf(c) != us) AddPawn(sq, to, nr == promoRank, list);
                else if (to == b.EpSquare) list.Add(new ChessMove(sq, to, ChessPiece.None, true, false, false));
            }
        }

        private static void AddPawn(int from, int to, bool promo, List<ChessMove> list)
        {
            if (promo)
            {
                list.Add(new ChessMove(from, to, ChessPiece.Queen));
                list.Add(new ChessMove(from, to, ChessPiece.Rook));
                list.Add(new ChessMove(from, to, ChessPiece.Bishop));
                list.Add(new ChessMove(from, to, ChessPiece.Knight));
            }
            else list.Add(new ChessMove(from, to));
        }

        private static void CastleMoves(ChessBoard b, int sq, ChessColor us, List<ChessMove> list)
        {
            var them = us == ChessColor.White ? ChessColor.Black : ChessColor.White;
            if (InCheck(b, us)) return;

            if (us == ChessColor.White && sq == ChessBoard.Sq(4, 0))
            {
                if ((b.CastleRights & Castle.WK) != 0 && b.Squares[ChessBoard.Sq(5, 0)] == 0 && b.Squares[ChessBoard.Sq(6, 0)] == 0
                    && !IsSquareAttacked(b, ChessBoard.Sq(5, 0), them) && !IsSquareAttacked(b, ChessBoard.Sq(6, 0), them))
                    list.Add(new ChessMove(sq, ChessBoard.Sq(6, 0), ChessPiece.None, false, true, false));
                if ((b.CastleRights & Castle.WQ) != 0 && b.Squares[ChessBoard.Sq(3, 0)] == 0 && b.Squares[ChessBoard.Sq(2, 0)] == 0 && b.Squares[ChessBoard.Sq(1, 0)] == 0
                    && !IsSquareAttacked(b, ChessBoard.Sq(3, 0), them) && !IsSquareAttacked(b, ChessBoard.Sq(2, 0), them))
                    list.Add(new ChessMove(sq, ChessBoard.Sq(2, 0), ChessPiece.None, false, true, false));
            }
            else if (us == ChessColor.Black && sq == ChessBoard.Sq(4, 7))
            {
                if ((b.CastleRights & Castle.BK) != 0 && b.Squares[ChessBoard.Sq(5, 7)] == 0 && b.Squares[ChessBoard.Sq(6, 7)] == 0
                    && !IsSquareAttacked(b, ChessBoard.Sq(5, 7), them) && !IsSquareAttacked(b, ChessBoard.Sq(6, 7), them))
                    list.Add(new ChessMove(sq, ChessBoard.Sq(6, 7), ChessPiece.None, false, true, false));
                if ((b.CastleRights & Castle.BQ) != 0 && b.Squares[ChessBoard.Sq(3, 7)] == 0 && b.Squares[ChessBoard.Sq(2, 7)] == 0 && b.Squares[ChessBoard.Sq(1, 7)] == 0
                    && !IsSquareAttacked(b, ChessBoard.Sq(3, 7), them) && !IsSquareAttacked(b, ChessBoard.Sq(2, 7), them))
                    list.Add(new ChessMove(sq, ChessBoard.Sq(2, 7), ChessPiece.None, false, true, false));
            }
        }
    }
}
