using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Chess
{
    public enum ChessColor { White = 0, Black = 1 }
    public enum ChessPiece { None = 0, Pawn = 1, Knight = 2, Bishop = 3, Rook = 4, Queen = 5, King = 6 }

    /// <summary>Castling-right bit flags.</summary>
    public static class Castle { public const int WK = 1, WQ = 2, BK = 4, BQ = 8; }

    /// <summary>A single ply. <see cref="Promo"/> is None unless a pawn promotion.</summary>
    public struct ChessMove
    {
        public int From, To;
        public ChessPiece Promo;
        public bool IsEnPassant, IsCastle, IsDoublePush;

        public ChessMove(int from, int to, ChessPiece promo = ChessPiece.None,
                         bool ep = false, bool castle = false, bool dbl = false)
        { From = from; To = to; Promo = promo; IsEnPassant = ep; IsCastle = castle; IsDoublePush = dbl; }

        public override string ToString()
        {
            string s = ChessBoard.SquareName(From) + ChessBoard.SquareName(To);
            if (Promo != ChessPiece.None) s += "nbrq"[(int)Promo - 2];
            return s;
        }
    }

    /// <summary>
    /// 8×8 mailbox chess board. squares[0]=a1 … squares[63]=h8. A square holds 0 (empty) or a piece code =
    /// (color*8 + type), type 1–6. Full make/unmake with castling, en-passant, promotion, and rights tracking so the
    /// move generator can search and perft can verify.
    /// </summary>
    public sealed class ChessBoard
    {
        public readonly int[] Squares = new int[64];
        public ChessColor SideToMove;
        public int CastleRights;   // WK|WQ|BK|BQ
        public int EpSquare;       // -1, or the square a pawn may capture onto en passant
        public int HalfmoveClock;
        public int FullmoveNumber;

        private struct Undo { public int Captured, CastleRights, EpSquare, HalfmoveClock; public ChessMove Move; }
        private readonly Stack<Undo> _history = new Stack<Undo>();

        public static int Code(ChessColor c, ChessPiece p) => p == ChessPiece.None ? 0 : ((int)c * 8 + (int)p);
        public static ChessPiece TypeOf(int code) => (ChessPiece)(code & 7);
        public static ChessColor ColorOf(int code) => (ChessColor)((code >> 3) & 1);

        public static int Sq(int file, int rank) => rank * 8 + file;   // file 0–7 (a–h), rank 0–7 (1–8)
        public static int FileOf(int sq) => sq & 7;
        public static int RankOf(int sq) => sq >> 3;
        public static string SquareName(int sq) => sq < 0 ? "-" : ((char)('a' + FileOf(sq))).ToString() + (char)('1' + RankOf(sq));

        public ChessBoard() { SetStartPosition(); }

        public void SetStartPosition()
        {
            for (int i = 0; i < 64; i++) Squares[i] = 0;
            ChessPiece[] back = { ChessPiece.Rook, ChessPiece.Knight, ChessPiece.Bishop, ChessPiece.Queen,
                                  ChessPiece.King, ChessPiece.Bishop, ChessPiece.Knight, ChessPiece.Rook };
            for (int f = 0; f < 8; f++)
            {
                Squares[Sq(f, 0)] = Code(ChessColor.White, back[f]);
                Squares[Sq(f, 1)] = Code(ChessColor.White, ChessPiece.Pawn);
                Squares[Sq(f, 6)] = Code(ChessColor.Black, ChessPiece.Pawn);
                Squares[Sq(f, 7)] = Code(ChessColor.Black, back[f]);
            }
            SideToMove = ChessColor.White;
            CastleRights = Castle.WK | Castle.WQ | Castle.BK | Castle.BQ;
            EpSquare = -1; HalfmoveClock = 0; FullmoveNumber = 1;
            _history.Clear();
        }

        public int PieceCode(int sq) => Squares[sq];
        public ChessPiece PieceTypeAt(int sq) => TypeOf(Squares[sq]);
        public bool IsEmpty(int sq) => Squares[sq] == 0;
        public bool IsColor(int sq, ChessColor c) => Squares[sq] != 0 && ColorOf(Squares[sq]) == c;

        public int KingSquare(ChessColor c)
        {
            int king = Code(c, ChessPiece.King);
            for (int i = 0; i < 64; i++) if (Squares[i] == king) return i;
            return -1;
        }

        public void MakeMove(ChessMove m)
        {
            var undo = new Undo { Captured = 0, CastleRights = CastleRights, EpSquare = EpSquare, HalfmoveClock = HalfmoveClock, Move = m };
            int piece = Squares[m.From];
            var us = SideToMove;
            var them = us == ChessColor.White ? ChessColor.Black : ChessColor.White;
            var pt = TypeOf(piece);

            int newEp = -1;
            HalfmoveClock++;

            if (m.IsEnPassant)
            {
                int capSq = us == ChessColor.White ? m.To - 8 : m.To + 8;
                undo.Captured = Squares[capSq];
                Squares[capSq] = 0;
                HalfmoveClock = 0;
            }
            else if (Squares[m.To] != 0)
            {
                undo.Captured = Squares[m.To];
                HalfmoveClock = 0;
            }

            Squares[m.To] = piece;
            Squares[m.From] = 0;

            if (pt == ChessPiece.Pawn)
            {
                HalfmoveClock = 0;
                if (m.IsDoublePush) newEp = us == ChessColor.White ? m.From + 8 : m.From - 8;
                if (m.Promo != ChessPiece.None) Squares[m.To] = Code(us, m.Promo);
            }

            if (m.IsCastle)
            {
                if (m.To == Sq(6, 0)) { Squares[Sq(5, 0)] = Squares[Sq(7, 0)]; Squares[Sq(7, 0)] = 0; }
                else if (m.To == Sq(2, 0)) { Squares[Sq(3, 0)] = Squares[Sq(0, 0)]; Squares[Sq(0, 0)] = 0; }
                else if (m.To == Sq(6, 7)) { Squares[Sq(5, 7)] = Squares[Sq(7, 7)]; Squares[Sq(7, 7)] = 0; }
                else if (m.To == Sq(2, 7)) { Squares[Sq(3, 7)] = Squares[Sq(0, 7)]; Squares[Sq(0, 7)] = 0; }
            }

            // King move drops both rights; rook move/capture from a home square drops that side.
            if (pt == ChessPiece.King) CastleRights &= us == ChessColor.White ? ~(Castle.WK | Castle.WQ) : ~(Castle.BK | Castle.BQ);
            ClearRookRight(m.From);
            ClearRookRight(m.To);

            EpSquare = newEp;
            if (us == ChessColor.Black) FullmoveNumber++;
            SideToMove = them;
            _history.Push(undo);
        }

        public void UnmakeMove()
        {
            var undo = _history.Pop();
            var m = undo.Move;
            var them = SideToMove;                               // side that just moved is the other one
            var us = them == ChessColor.White ? ChessColor.Black : ChessColor.White;

            if (us == ChessColor.Black) FullmoveNumber--;
            SideToMove = us;
            CastleRights = undo.CastleRights;
            EpSquare = undo.EpSquare;
            HalfmoveClock = undo.HalfmoveClock;

            int moved = Squares[m.To];
            // Undo promotion: the piece that left From was a pawn.
            if (m.Promo != ChessPiece.None) moved = Code(us, ChessPiece.Pawn);
            Squares[m.From] = moved;
            Squares[m.To] = 0;

            if (m.IsEnPassant)
            {
                int capSq = us == ChessColor.White ? m.To - 8 : m.To + 8;
                Squares[capSq] = undo.Captured;
            }
            else if (undo.Captured != 0)
            {
                Squares[m.To] = undo.Captured;
            }

            if (m.IsCastle)
            {
                if (m.To == Sq(6, 0)) { Squares[Sq(7, 0)] = Squares[Sq(5, 0)]; Squares[Sq(5, 0)] = 0; }
                else if (m.To == Sq(2, 0)) { Squares[Sq(0, 0)] = Squares[Sq(3, 0)]; Squares[Sq(3, 0)] = 0; }
                else if (m.To == Sq(6, 7)) { Squares[Sq(7, 7)] = Squares[Sq(5, 7)]; Squares[Sq(5, 7)] = 0; }
                else if (m.To == Sq(2, 7)) { Squares[Sq(0, 7)] = Squares[Sq(3, 7)]; Squares[Sq(3, 7)] = 0; }
            }
        }

        /// <summary>Compact state string (board + turn + rights) — stored in the world board's ZDO so a game in
        /// progress lives in the world save and every client can mirror it.</summary>
        public static string Serialize(ChessBoard b)
        {
            var sb = new System.Text.StringBuilder(90);
            for (int i = 0; i < 64; i++) sb.Append((char)('A' + b.Squares[i]));
            sb.Append('|').Append((int)b.SideToMove).Append('|').Append(b.CastleRights).Append('|').Append(b.EpSquare)
              .Append('|').Append(b.HalfmoveClock).Append('|').Append(b.FullmoveNumber);
            return sb.ToString();
        }

        public static bool Deserialize(string s, ChessBoard b)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return false;
                var parts = s.Split('|');
                if (parts.Length < 6 || parts[0].Length != 64) return false;
                for (int i = 0; i < 64; i++)
                {
                    int code = parts[0][i] - 'A';
                    if (code < 0 || code > 14) return false;
                    b.Squares[i] = code;
                }
                b.SideToMove = (ChessColor)int.Parse(parts[1]);
                b.CastleRights = int.Parse(parts[2]);
                b.EpSquare = int.Parse(parts[3]);
                b.HalfmoveClock = int.Parse(parts[4]);
                b.FullmoveNumber = int.Parse(parts[5]);
                return true;
            }
            catch { return false; }
        }

        private void ClearRookRight(int sq)
        {
            if (sq == Sq(0, 0)) CastleRights &= ~Castle.WQ;
            else if (sq == Sq(7, 0)) CastleRights &= ~Castle.WK;
            else if (sq == Sq(0, 7)) CastleRights &= ~Castle.BQ;
            else if (sq == Sq(7, 7)) CastleRights &= ~Castle.BK;
        }
    }
}
