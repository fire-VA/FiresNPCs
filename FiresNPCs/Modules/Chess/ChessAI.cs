using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Chess
{
    /// <summary>
    /// Chess AI v2 — a real engine core over the perft-verified board: iterative deepening with a time budget,
    /// Zobrist-keyed transposition table, killer/history move ordering, quiescence with check evasions, and a tapered
    /// middle/endgame evaluation (material, PSTs, bishop pair, rook files, passed pawns). All classic public-domain
    /// techniques. Pure C# — runs on a worker thread against a <see cref="Clone"/>. Difficulty tiers map to
    /// depth/time (+ a deliberate error chance on Easy only).
    /// </summary>
    public static class ChessAI
    {
        private const int Mate = 100000;
        private static readonly int[] Value = { 0, 100, 320, 330, 500, 900, 0 };

        // ── piece-square tables (white view, a1 first; black mirrors rank) ─────────────────────────────────────
        private static readonly int[] PawnMid = {
            0,0,0,0,0,0,0,0, 5,10,10,-20,-20,10,10,5, 5,-5,-10,0,0,-10,-5,5, 0,0,0,20,20,0,0,0,
            5,5,10,25,25,10,5,5, 10,10,20,30,30,20,10,10, 50,50,50,50,50,50,50,50, 0,0,0,0,0,0,0,0 };
        private static readonly int[] PawnEnd = {
            0,0,0,0,0,0,0,0, 5,5,5,5,5,5,5,5, 10,10,10,10,10,10,10,10, 20,20,20,20,20,20,20,20,
            35,35,35,35,35,35,35,35, 55,55,55,55,55,55,55,55, 80,80,80,80,80,80,80,80, 0,0,0,0,0,0,0,0 };
        private static readonly int[] KnightT = {
            -50,-40,-30,-30,-30,-30,-40,-50, -40,-20,0,5,5,0,-20,-40, -30,5,10,15,15,10,5,-30, -30,0,15,20,20,15,0,-30,
            -30,5,15,20,20,15,5,-30, -30,0,10,15,15,10,0,-30, -40,-20,0,0,0,0,-20,-40, -50,-40,-30,-30,-30,-30,-40,-50 };
        private static readonly int[] BishopT = {
            -20,-10,-10,-10,-10,-10,-10,-20, -10,5,0,0,0,0,5,-10, -10,10,10,10,10,10,10,-10, -10,0,10,10,10,10,0,-10,
            -10,5,5,10,10,5,5,-10, -10,0,5,10,10,5,0,-10, -10,0,0,0,0,0,0,-10, -20,-10,-10,-10,-10,-10,-10,-20 };
        private static readonly int[] RookT = {
            0,0,0,5,5,0,0,0, -5,0,0,0,0,0,0,-5, -5,0,0,0,0,0,0,-5, -5,0,0,0,0,0,0,-5,
            -5,0,0,0,0,0,0,-5, -5,0,0,0,0,0,0,-5, 5,10,10,10,10,10,10,5, 0,0,0,0,0,0,0,0 };
        private static readonly int[] QueenT = {
            -20,-10,-10,-5,-5,-10,-10,-20, -10,0,5,0,0,0,0,-10, -10,5,5,5,5,5,0,-10, 0,0,5,5,5,5,0,-5,
            -5,0,5,5,5,5,0,-5, -10,0,5,5,5,5,0,-10, -10,0,0,0,0,0,0,-10, -20,-10,-10,-5,-5,-10,-10,-20 };
        private static readonly int[] KingMid = {
            20,30,10,0,0,10,30,20, 20,20,0,0,0,0,20,20, -10,-20,-20,-20,-20,-20,-20,-10, -20,-30,-30,-40,-40,-30,-30,-20,
            -30,-40,-40,-50,-50,-40,-40,-30, -30,-40,-40,-50,-50,-40,-40,-30, -30,-40,-40,-50,-50,-40,-40,-30, -30,-40,-40,-50,-50,-40,-40,-30 };
        private static readonly int[] KingEnd = {
            -50,-30,-30,-30,-30,-30,-30,-50, -30,-10,0,0,0,0,-10,-30, -30,0,15,20,20,15,0,-30, -30,0,20,30,30,20,0,-30,
            -30,0,20,30,30,20,0,-30, -30,0,15,20,20,15,0,-30, -30,-20,0,0,0,0,-20,-30, -50,-40,-30,-30,-30,-30,-40,-50 };

        // ── Zobrist keys + transposition table ──────────────────────────────────────────────────────────────────
        private static readonly ulong[,] ZPiece = new ulong[15, 64];
        private static readonly ulong[] ZCastle = new ulong[16];
        private static readonly ulong[] ZEp = new ulong[9];
        private static readonly ulong ZSide;

        private struct TTEntry { public ulong Key; public int Score; public ushort Move; public byte Depth; public byte Flag; }
        private const int TTBits = 20;                       // 1M entries
        private const byte FlagExact = 1, FlagLower = 2, FlagUpper = 3;

        static ChessAI()
        {
            ulong s = 0x9E3779B97F4A7C15UL;
            ulong Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }
            for (int p = 0; p < 15; p++) for (int q = 0; q < 64; q++) ZPiece[p, q] = Next();
            for (int i = 0; i < 16; i++) ZCastle[i] = Next();
            for (int i = 0; i < 9; i++) ZEp[i] = Next();
            ZSide = Next();
        }

        private static ulong Hash(ChessBoard b)
        {
            ulong h = 0;
            for (int sq = 0; sq < 64; sq++) if (b.Squares[sq] != 0) h ^= ZPiece[b.Squares[sq], sq];
            h ^= ZCastle[b.CastleRights & 15];
            h ^= ZEp[b.EpSquare >= 0 ? ChessBoard.FileOf(b.EpSquare) + 1 : 0];
            if (b.SideToMove == ChessColor.Black) h ^= ZSide;
            return h;
        }

        // ── search context (one per FindBest call — thread-safe across tables) ─────────────────────────────────
        private sealed class Ctx
        {
            public TTEntry[] TT = new TTEntry[1 << TTBits];
            public ChessMove[,] Killers = new ChessMove[128, 2];
            public int[,] History = new int[64, 64];
            public ulong[] Path = new ulong[512];            // game-history keys + current search line
            public int Base;                                 // slots 0..Base-1 = prior positions (last = root)
            public System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
            public long Budget;
            public bool Abort;
            public int Nodes;

            public bool TimeUp()
            {
                if (Abort) return true;
                if ((Nodes & 1023) == 0 && Budget > 0 && Clock.ElapsedMilliseconds > Budget) Abort = true;
                return Abort;
            }
        }

        public static ChessBoard Clone(ChessBoard src)
        {
            var b = new ChessBoard();
            for (int i = 0; i < 64; i++) b.Squares[i] = src.Squares[i];
            b.SideToMove = src.SideToMove;
            b.CastleRights = src.CastleRights;
            b.EpSquare = src.EpSquare;
            b.HalfmoveClock = src.HalfmoveClock;
            b.FullmoveNumber = src.FullmoveNumber;
            return b;
        }

        /// <summary>Compatibility entry — fixed depth, no clock.</summary>
        public static bool FindBest(ChessBoard b, int depth, System.Random rng, out ChessMove best)
            => FindBestTimed(b, depth, 0, rng, 0, null, out best);

        /// <summary>Position key for game-level repetition tracking (threefold draws / shuffle avoidance).</summary>
        public static ulong HashPosition(ChessBoard b) => Hash(b);

        public static bool FindBestTimed(ChessBoard b, int maxDepth, int timeMs, System.Random rng, float errorChance, out ChessMove best)
            => FindBestTimed(b, maxDepth, timeMs, rng, errorChance, null, out best);

        /// <summary>Iterative-deepening search to <paramref name="maxDepth"/> (bounded by <paramref name="timeMs"/>
        /// when &gt; 0). <paramref name="errorChance"/> (0..1, Easy only) sometimes plays the second-best root move.
        /// <paramref name="priorKeys"/> = recent game position hashes (oldest→newest, INCLUDING the current position);
        /// any position repeating one of them scores as a draw, so the engine neither shuffles nor walks into
        /// threefold when ahead.</summary>
        public static bool FindBestTimed(ChessBoard b, int maxDepth, int timeMs, System.Random rng, float errorChance,
            List<ulong> priorKeys, out ChessMove best)
        {
            best = default;
            var rootMoves = ChessMoveGen.GenerateLegal(b);
            if (rootMoves.Count == 0) return false;

            var ctx = new Ctx { Budget = timeMs };
            if (priorKeys != null && priorKeys.Count > 0)
            {
                int take = System.Math.Min(priorKeys.Count, 120);
                for (int i = 0; i < take; i++) ctx.Path[i] = priorKeys[priorKeys.Count - take + i];
                ctx.Base = take;
            }
            else
            {
                ctx.Path[0] = Hash(b);
                ctx.Base = 1;
            }
            ctx.Clock.Start();

            var scores = new int[rootMoves.Count];
            ChessMove bestSoFar = rootMoves[0];
            ChessMove secondSoFar = rootMoves.Count > 1 ? rootMoves[1] : rootMoves[0];

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                OrderRoot(b, rootMoves, scores, bestSoFar);
                int alpha = -Mate * 2;
                ChessMove iterBest = default, iterSecond = default;
                int iterBestScore = int.MinValue, iterSecondScore = int.MinValue;
                bool completed = true;

                for (int i = 0; i < rootMoves.Count; i++)
                {
                    var m = rootMoves[i];
                    b.MakeMove(m);
                    int score = -Search(ctx, b, depth - 1, -Mate * 2, -alpha, 1);
                    b.UnmakeMove();
                    if (ctx.Abort) { completed = false; break; }
                    scores[i] = score;
                    if (score > iterBestScore)
                    {
                        iterSecond = iterBest; iterSecondScore = iterBestScore;
                        iterBest = m; iterBestScore = score;
                    }
                    else if (score > iterSecondScore) { iterSecond = m; iterSecondScore = score; }
                    if (score > alpha) alpha = score;
                }

                if (completed)
                {
                    bestSoFar = iterBest;
                    secondSoFar = iterSecondScore > int.MinValue ? iterSecond : iterBest;
                    if (iterBestScore > Mate - 200) break;   // found a forced mate — stop
                }
                if (ctx.Abort) break;
            }

            best = bestSoFar;
            if (errorChance > 0f && rng != null && rng.NextDouble() < errorChance) best = secondSoFar;
            return true;
        }

        private static void OrderRoot(ChessBoard b, List<ChessMove> moves, int[] scores, ChessMove pv)
        {
            // Previous-iteration scores drive the root order; the PV move goes first.
            var idx = new int[moves.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            var arr = moves.ToArray();
            System.Array.Sort(scores, arr, 0, moves.Count);
            System.Array.Reverse(arr);
            System.Array.Reverse(scores);
            for (int i = 0; i < arr.Length; i++)
                if (Same(arr[i], pv) && i != 0) { var t = arr[i]; arr[i] = arr[0]; arr[0] = t; break; }
            for (int i = 0; i < arr.Length; i++) moves[i] = arr[i];
        }

        private static bool Same(ChessMove a, ChessMove b) => a.From == b.From && a.To == b.To && a.Promo == b.Promo;

        private static ushort Pack(ChessMove m) => (ushort)(m.From | (m.To << 6) | ((int)m.Promo << 12));

        // ── the search ──────────────────────────────────────────────────────────────────────────────────────────
        private static int Search(Ctx ctx, ChessBoard b, int depth, int alpha, int beta, int ply)
        {
            ctx.Nodes++;
            if (ctx.TimeUp()) return 0;
            if (b.HalfmoveClock >= 100) return 0;

            bool inCheck = ChessMoveGen.InCheck(b, b.SideToMove);
            if (inCheck) depth++;                            // check extension
            if (depth <= 0) return Quiesce(ctx, b, alpha, beta, 0);

            ulong key = Hash(b);

            // repetition — scan same-side slots back through the search line into game history (twofold = draw)
            int slot = ctx.Base - 1 + ply;
            if (slot < ctx.Path.Length)
            {
                ctx.Path[slot] = key;
                if (ply > 0)
                {
                    int floor = slot - System.Math.Min(slot, b.HalfmoveClock);
                    for (int j = slot - 2; j >= floor; j -= 2)
                        if (ctx.Path[j] == key) return 0;
                }
            }

            ref var tte = ref ctx.TT[key & ((1 << TTBits) - 1)];
            ushort ttMove = 0;
            if (tte.Key == key)
            {
                ttMove = tte.Move;
                if (tte.Depth >= depth && ply > 0)
                {
                    int s = tte.Score;
                    if (tte.Flag == FlagExact) return s;
                    if (tte.Flag == FlagLower && s >= beta) return s;
                    if (tte.Flag == FlagUpper && s <= alpha) return s;
                }
            }

            var moves = ChessMoveGen.GenerateLegal(b);
            if (moves.Count == 0) return inCheck ? -Mate + ply : 0;

            OrderMoves(ctx, b, moves, ttMove, ply);

            int bestScore = int.MinValue;
            ChessMove bestMove = default;
            byte flag = FlagUpper;
            foreach (var m in moves)
            {
                b.MakeMove(m);
                int score = -Search(ctx, b, depth - 1, -beta, -alpha, ply + 1);
                b.UnmakeMove();
                if (ctx.Abort) return 0;

                if (score > bestScore) { bestScore = score; bestMove = m; }
                if (score > alpha)
                {
                    alpha = score;
                    flag = FlagExact;
                    if (alpha >= beta)
                    {
                        flag = FlagLower;
                        if (b.Squares[m.To] == 0 && !m.IsEnPassant)
                        {
                            // quiet cutoff → killer + history credit
                            if (ply < 128 && !Same(ctx.Killers[ply, 0], m))
                            { ctx.Killers[ply, 1] = ctx.Killers[ply, 0]; ctx.Killers[ply, 0] = m; }
                            ctx.History[m.From, m.To] += depth * depth;
                        }
                        break;
                    }
                }
            }

            tte.Key = key; tte.Score = bestScore; tte.Move = Pack(bestMove);
            tte.Depth = (byte)System.Math.Max(0, System.Math.Min(255, depth)); tte.Flag = flag;
            return bestScore;
        }

        private static int Quiesce(Ctx ctx, ChessBoard b, int alpha, int beta, int qply)
        {
            ctx.Nodes++;
            if (ctx.TimeUp()) return 0;

            bool inCheck = ChessMoveGen.InCheck(b, b.SideToMove);
            if (!inCheck)
            {
                int stand = Evaluate(b);
                if (stand >= beta) return beta;
                if (stand > alpha) alpha = stand;
                if (qply >= 16) return alpha;
            }

            var moves = ChessMoveGen.GenerateLegal(b);
            if (moves.Count == 0) return inCheck ? -Mate + 64 + qply : alpha;

            OrderMoves(ctx, b, moves, 0, -1);
            foreach (var m in moves)
            {
                bool capture = b.Squares[m.To] != 0 || m.IsEnPassant;
                if (!inCheck && !capture && m.Promo != ChessPiece.Queen) continue;   // in check: search ALL evasions
                b.MakeMove(m);
                int score = -Quiesce(ctx, b, -beta, -alpha, qply + 1);
                b.UnmakeMove();
                if (ctx.Abort) return 0;
                if (score >= beta) return beta;
                if (score > alpha) alpha = score;
            }
            return alpha;
        }

        private static void OrderMoves(Ctx ctx, ChessBoard b, List<ChessMove> moves, ushort ttMove, int ply)
        {
            int n = moves.Count;
            var keys = new int[n];
            for (int i = 0; i < n; i++)
            {
                var m = moves[i];
                int s = 0;
                if (ttMove != 0 && Pack(m) == ttMove) s = 1_000_000;
                else
                {
                    int victim = b.Squares[m.To];
                    if (victim != 0) s = 100_000 + Value[(int)ChessBoard.TypeOf(victim)] * 10 - Value[(int)ChessBoard.TypeOf(b.Squares[m.From])] / 10;
                    else if (m.IsEnPassant) s = 100_000 + Value[1] * 10;
                    else if (m.Promo == ChessPiece.Queen) s = 90_000;
                    else if (ply >= 0 && ply < 128 && (Same(ctx.Killers[ply, 0], m) || Same(ctx.Killers[ply, 1], m))) s = 80_000;
                    else s = ctx.History[m.From, m.To];
                }
                keys[i] = -s;   // ascending sort → negate for descending
            }
            var arr = moves.ToArray();
            System.Array.Sort(keys, arr);
            for (int i = 0; i < n; i++) moves[i] = arr[i];
        }

        // ── tapered evaluation ──────────────────────────────────────────────────────────────────────────────────
        /// <summary>Static eval from the SIDE-TO-MOVE's perspective.</summary>
        public static int Evaluate(ChessBoard b)
        {
            int mid = 0, end = 0, phase = 0;
            int wBishops = 0, bBishops = 0;

            // pawn file occupancy for rook files + passed pawns
            int wPawnFiles = 0, bPawnFiles = 0;
            var wPawnMaxRank = new int[8]; var bPawnMinRank = new int[8];
            for (int f = 0; f < 8; f++) { wPawnMaxRank[f] = -1; bPawnMinRank[f] = 8; }
            for (int sq = 0; sq < 64; sq++)
            {
                int code = b.Squares[sq];
                if (code == 0 || ChessBoard.TypeOf(code) != ChessPiece.Pawn) continue;
                int f = ChessBoard.FileOf(sq), r = ChessBoard.RankOf(sq);
                if (ChessBoard.ColorOf(code) == ChessColor.White) { wPawnFiles |= 1 << f; if (r > wPawnMaxRank[f]) wPawnMaxRank[f] = r; }
                else { bPawnFiles |= 1 << f; if (r < bPawnMinRank[f]) bPawnMinRank[f] = r; }
            }

            for (int sq = 0; sq < 64; sq++)
            {
                int code = b.Squares[sq];
                if (code == 0) continue;
                var t = ChessBoard.TypeOf(code);
                bool white = ChessBoard.ColorOf(code) == ChessColor.White;
                int idx = white ? sq : sq ^ 56;
                int f = ChessBoard.FileOf(sq), r = ChessBoard.RankOf(sq);
                int vm = Value[(int)t], ve = Value[(int)t];

                switch (t)
                {
                    case ChessPiece.Pawn:
                        vm += PawnMid[idx]; ve += PawnEnd[idx];
                        // passed pawn: no enemy pawn ahead on this or adjacent files
                        bool passed = true;
                        for (int df = -1; df <= 1 && passed; df++)
                        {
                            int ff = f + df;
                            if (ff < 0 || ff > 7) continue;
                            if (white) { if ((bPawnFiles & (1 << ff)) != 0 && bPawnMinRank[ff] > r) passed = false; }
                            else { if ((wPawnFiles & (1 << ff)) != 0 && wPawnMaxRank[ff] < r) passed = false; }
                        }
                        if (passed) { int adv = white ? r : 7 - r; vm += 10 + adv * 6; ve += 20 + adv * 12; }
                        break;
                    case ChessPiece.Knight: vm += KnightT[idx]; ve += KnightT[idx]; phase += 1; break;
                    case ChessPiece.Bishop:
                        vm += BishopT[idx]; ve += BishopT[idx]; phase += 1;
                        if (white) wBishops++; else bBishops++;
                        break;
                    case ChessPiece.Rook:
                        vm += RookT[idx]; ve += RookT[idx]; phase += 2;
                        bool ownPawn = ((white ? wPawnFiles : bPawnFiles) & (1 << f)) != 0;
                        bool anyPawn = ((wPawnFiles | bPawnFiles) & (1 << f)) != 0;
                        if (!anyPawn) { vm += 18; ve += 12; }
                        else if (!ownPawn) { vm += 9; ve += 6; }
                        break;
                    case ChessPiece.Queen: vm += QueenT[idx]; ve += QueenT[idx]; phase += 4; break;
                    case ChessPiece.King: vm += KingMid[idx]; ve += KingEnd[idx]; break;
                }

                if (white) { mid += vm; end += ve; } else { mid -= vm; end -= ve; }
            }

            if (wBishops >= 2) { mid += 30; end += 40; }
            if (bBishops >= 2) { mid -= 30; end -= 40; }

            // taper: phase 24 = full middlegame, 0 = bare endgame
            if (phase > 24) phase = 24;
            int score = (mid * phase + end * (24 - phase)) / 24;
            score += b.SideToMove == ChessColor.White ? 10 : -10;   // tempo
            return b.SideToMove == ChessColor.White ? score : -score;
        }

        /// <summary>Terminal-state check for the UI: null while the game is live, else the end-of-game message.</summary>
        public static string Outcome(ChessBoard b, List<ChessMove> legal)
        {
            if (legal.Count == 0)
                return ChessMoveGen.InCheck(b, b.SideToMove)
                    ? (b.SideToMove == ChessColor.White ? "Checkmate — Black wins!" : "Checkmate — White wins!")
                    : "Stalemate — a draw.";
            if (b.HalfmoveClock >= 100) return "Draw — fifty moves without progress.";

            int minors = 0, others = 0;
            for (int sq = 0; sq < 64; sq++)
            {
                var t = ChessBoard.TypeOf(b.Squares[sq]);
                if (t == ChessPiece.None || t == ChessPiece.King) continue;
                if (t == ChessPiece.Knight || t == ChessPiece.Bishop) minors++;
                else others++;
            }
            if (others == 0 && minors <= 1) return "Draw — insufficient material.";
            return null;
        }
    }
}
