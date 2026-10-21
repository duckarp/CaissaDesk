namespace CaissaDesk.Core;

public static class MoveGenerator
{
    private static readonly (int df, int dr)[] KnightOffsets =
    [
        (1, 2), (2, 1), (2, -1), (1, -2),
        (-1, -2), (-2, -1), (-2, 1), (-1, 2)
    ];

    private static readonly (int df, int dr)[] KingOffsets =
    [
        (0, 1), (1, 1), (1, 0), (1, -1),
        (0, -1), (-1, -1), (-1, 0), (-1, 1)
    ];

    private static readonly (int df, int dr)[] RookDirections =
    [
        (0, 1), (0, -1), (1, 0), (-1, 0)
    ];

    private static readonly (int df, int dr)[] BishopDirections =
    [
        (1, 1), (1, -1), (-1, 1), (-1, -1)
    ];

    public static List<Move> GenerateLegalMoves(BoardPosition pos)
    {
        var pseudo = GeneratePseudoLegalMoves(pos);
        var legal = new List<Move>(pseudo.Count);

        var us = pos.ActiveColor;
        foreach (var move in pseudo)
        {
            var next = ApplyMove(pos, move);
            var kingSq = next.FindKing(us);
            if (kingSq != Square.None && !IsSquareAttacked(next, kingSq, us == PieceColor.White ? PieceColor.Black : PieceColor.White))
            {
                legal.Add(move);
            }
        }

        return legal;
    }

    public static Dictionary<string, List<string>> GetLegalDestinations(BoardPosition pos)
    {
        var legalMoves = GenerateLegalMoves(pos);
        var dests = new Dictionary<string, List<string>>();

        foreach (var move in legalMoves)
        {
            string from = move.From.Name();
            string to = move.To.Name();

            if (!dests.TryGetValue(from, out var list))
            {
                list = new List<string>();
                dests[from] = list;
            }

            if (!list.Contains(to))
            {
                list.Add(to);
            }
        }

        return dests;
    }

    public static bool IsInCheck(BoardPosition pos, PieceColor color)
    {
        var kingSq = pos.FindKing(color);
        if (kingSq == Square.None) return false;
        var enemy = color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        return IsSquareAttacked(pos, kingSq, enemy);
    }

    public static bool IsCheckmate(BoardPosition pos)
    {
        return IsInCheck(pos, pos.ActiveColor) && GenerateLegalMoves(pos).Count == 0;
    }

    public static bool IsStalemate(BoardPosition pos)
    {
        return !IsInCheck(pos, pos.ActiveColor) && GenerateLegalMoves(pos).Count == 0;
    }

    public static bool IsSquareAttacked(BoardPosition pos, Square sq, PieceColor attackerColor)
    {
        int f = sq.File();
        int r = sq.Rank();

        // 1. Attacked by Pawns
        int pawnRank = attackerColor == PieceColor.White ? r - 1 : r + 1;
        if (pawnRank >= 0 && pawnRank <= 7)
        {
            if (f > 0)
            {
                var p = pos[f - 1, pawnRank];
                if (p.Color == attackerColor && p.Type == PieceType.Pawn) return true;
            }
            if (f < 7)
            {
                var p = pos[f + 1, pawnRank];
                if (p.Color == attackerColor && p.Type == PieceType.Pawn) return true;
            }
        }

        // 2. Attacked by Knights
        foreach (var (df, dr) in KnightOffsets)
        {
            int tf = f + df;
            int tr = r + dr;
            if (tf >= 0 && tf < 8 && tr >= 0 && tr < 8)
            {
                var p = pos[tf, tr];
                if (p.Color == attackerColor && p.Type == PieceType.Knight) return true;
            }
        }

        // 3. Attacked by King
        foreach (var (df, dr) in KingOffsets)
        {
            int tf = f + df;
            int tr = r + dr;
            if (tf >= 0 && tf < 8 && tr >= 0 && tr < 8)
            {
                var p = pos[tf, tr];
                if (p.Color == attackerColor && p.Type == PieceType.King) return true;
            }
        }

        // 4. Attacked by Rook or Queen (Straight lines)
        foreach (var (df, dr) in RookDirections)
        {
            int tf = f + df;
            int tr = r + dr;
            while (tf >= 0 && tf < 8 && tr >= 0 && tr < 8)
            {
                var p = pos[tf, tr];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackerColor && (p.Type == PieceType.Rook || p.Type == PieceType.Queen))
                        return true;
                    break;
                }
                tf += df;
                tr += dr;
            }
        }

        // 5. Attacked by Bishop or Queen (Diagonals)
        foreach (var (df, dr) in BishopDirections)
        {
            int tf = f + df;
            int tr = r + dr;
            while (tf >= 0 && tf < 8 && tr >= 0 && tr < 8)
            {
                var p = pos[tf, tr];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackerColor && (p.Type == PieceType.Bishop || p.Type == PieceType.Queen))
                        return true;
                    break;
                }
                tf += df;
                tr += dr;
            }
        }

        return false;
    }

    private static List<Move> GeneratePseudoLegalMoves(BoardPosition pos)
    {
        var moves = new List<Move>(48);
        var us = pos.ActiveColor;
        var them = us == PieceColor.White ? PieceColor.Black : PieceColor.White;

        for (int sqIndex = 0; sqIndex < 64; sqIndex++)
        {
            var p = pos.Squares[sqIndex];
            if (p.IsEmpty || p.Color != us)
                continue;

            var sq = (Square)sqIndex;
            int f = sq.File();
            int r = sq.Rank();

            switch (p.Type)
            {
                case PieceType.Pawn:
                    GeneratePawnMoves(pos, sq, f, r, us, them, moves);
                    break;
                case PieceType.Knight:
                    GenerateKnightMoves(pos, sq, f, r, them, moves);
                    break;
                case PieceType.Bishop:
                    GenerateSlidingMoves(pos, sq, f, r, BishopDirections, them, moves);
                    break;
                case PieceType.Rook:
                    GenerateSlidingMoves(pos, sq, f, r, RookDirections, them, moves);
                    break;
                case PieceType.Queen:
                    GenerateSlidingMoves(pos, sq, f, r, RookDirections, them, moves);
                    GenerateSlidingMoves(pos, sq, f, r, BishopDirections, them, moves);
                    break;
                case PieceType.King:
                    GenerateKingMoves(pos, sq, f, r, them, moves);
                    GenerateCastlingMoves(pos, us, them, moves);
                    break;
            }
        }

        return moves;
    }

    private static void GeneratePawnMoves(BoardPosition pos, Square from, int f, int r, PieceColor us, PieceColor them, List<Move> moves)
    {
        int forwardDir = us == PieceColor.White ? 1 : -1;
        int startRank = us == PieceColor.White ? 1 : 6;
        int promoRank = us == PieceColor.White ? 7 : 0;

        // 1. Single forward push
        int tr = r + forwardDir;
        if (tr >= 0 && tr <= 7 && pos[f, tr].IsEmpty)
        {
            var to = SquareExtensions.FromCoords(f, tr);
            if (tr == promoRank)
            {
                AddPromotions(from, to, moves, false);
            }
            else
            {
                moves.Add(new Move(from, to));

                // 2. Double forward push
                if (r == startRank)
                {
                    int doubleTr = r + forwardDir * 2;
                    if (pos[f, doubleTr].IsEmpty)
                    {
                        moves.Add(new Move(from, SquareExtensions.FromCoords(f, doubleTr)));
                    }
                }
            }
        }

        // 3. Captures left & right
        foreach (int df in new[] { -1, 1 })
        {
            int tf = f + df;
            if (tf >= 0 && tf <= 7 && tr >= 0 && tr <= 7)
            {
                var to = SquareExtensions.FromCoords(tf, tr);
                var targetPiece = pos[to];

                if (!targetPiece.IsEmpty && targetPiece.Color == them)
                {
                    if (tr == promoRank)
                        AddPromotions(from, to, moves, true);
                    else
                        moves.Add(new Move(from, to, Flags: MoveFlags.Capture));
                }
                else if (pos.EnPassantSquare.HasValue && pos.EnPassantSquare.Value == to)
                {
                    moves.Add(new Move(from, to, Flags: MoveFlags.EnPassant | MoveFlags.Capture));
                }
            }
        }
    }

    private static void AddPromotions(Square from, Square to, List<Move> moves, bool isCapture)
    {
        var flags = MoveFlags.Promotion | (isCapture ? MoveFlags.Capture : MoveFlags.None);
        moves.Add(new Move(from, to, PieceType.Queen, flags));
        moves.Add(new Move(from, to, PieceType.Rook, flags));
        moves.Add(new Move(from, to, PieceType.Bishop, flags));
        moves.Add(new Move(from, to, PieceType.Knight, flags));
    }

    private static void GenerateKnightMoves(BoardPosition pos, Square from, int f, int r, PieceColor them, List<Move> moves)
    {
        foreach (var (df, dr) in KnightOffsets)
        {
            int tf = f + df;
            int tr = r + dr;
            if (tf >= 0 && tf < 8 && tr >= 0 && tr < 8)
            {
                var to = SquareExtensions.FromCoords(tf, tr);
                var p = pos[to];
                if (p.IsEmpty)
                    moves.Add(new Move(from, to));
                else if (p.Color == them)
                    moves.Add(new Move(from, to, Flags: MoveFlags.Capture));
            }
        }
    }

    private static void GenerateKingMoves(BoardPosition pos, Square from, int f, int r, PieceColor them, List<Move> moves)
    {
        foreach (var (df, dr) in KingOffsets)
        {
            int tf = f + df;
            int tr = r + dr;
            if (tf >= 0 && tf < 8 && tr >= 0 && tr < 8)
            {
                var to = SquareExtensions.FromCoords(tf, tr);
                var p = pos[to];
                if (p.IsEmpty)
                    moves.Add(new Move(from, to));
                else if (p.Color == them)
                    moves.Add(new Move(from, to, Flags: MoveFlags.Capture));
            }
        }
    }

    private static void GenerateCastlingMoves(BoardPosition pos, PieceColor us, PieceColor them, List<Move> moves)
    {
        if (us == PieceColor.White)
        {
            if (pos.Squares[(byte)Square.E1].Type == PieceType.King && pos.Squares[(byte)Square.E1].Color == PieceColor.White)
            {
                // Kingside (e1 -> g1)
                if ((pos.Castling & CastlingRights.WhiteKing) != 0 &&
                    pos.Squares[(byte)Square.F1].IsEmpty &&
                    pos.Squares[(byte)Square.G1].IsEmpty &&
                    pos.Squares[(byte)Square.H1].Type == PieceType.Rook &&
                    pos.Squares[(byte)Square.H1].Color == PieceColor.White &&
                    !IsSquareAttacked(pos, Square.E1, them) &&
                    !IsSquareAttacked(pos, Square.F1, them) &&
                    !IsSquareAttacked(pos, Square.G1, them))
                {
                    moves.Add(new Move(Square.E1, Square.G1, Flags: MoveFlags.Castling));
                }

                // Queenside (e1 -> c1)
                if ((pos.Castling & CastlingRights.WhiteQueen) != 0 &&
                    pos.Squares[(byte)Square.D1].IsEmpty &&
                    pos.Squares[(byte)Square.C1].IsEmpty &&
                    pos.Squares[(byte)Square.B1].IsEmpty &&
                    pos.Squares[(byte)Square.A1].Type == PieceType.Rook &&
                    pos.Squares[(byte)Square.A1].Color == PieceColor.White &&
                    !IsSquareAttacked(pos, Square.E1, them) &&
                    !IsSquareAttacked(pos, Square.D1, them) &&
                    !IsSquareAttacked(pos, Square.C1, them))
                {
                    moves.Add(new Move(Square.E1, Square.C1, Flags: MoveFlags.Castling));
                }
            }
        }
        else
        {
            if (pos.Squares[(byte)Square.E8].Type == PieceType.King && pos.Squares[(byte)Square.E8].Color == PieceColor.Black)
            {
                // Kingside (e8 -> g8)
                if ((pos.Castling & CastlingRights.BlackKing) != 0 &&
                    pos.Squares[(byte)Square.F8].IsEmpty &&
                    pos.Squares[(byte)Square.G8].IsEmpty &&
                    pos.Squares[(byte)Square.H8].Type == PieceType.Rook &&
                    pos.Squares[(byte)Square.H8].Color == PieceColor.Black &&
                    !IsSquareAttacked(pos, Square.E8, them) &&
                    !IsSquareAttacked(pos, Square.F8, them) &&
                    !IsSquareAttacked(pos, Square.G8, them))
                {
                    moves.Add(new Move(Square.E8, Square.G8, Flags: MoveFlags.Castling));
                }

                // Queenside (e8 -> c8)
                if ((pos.Castling & CastlingRights.BlackQueen) != 0 &&
                    pos.Squares[(byte)Square.D8].IsEmpty &&
                    pos.Squares[(byte)Square.C8].IsEmpty &&
                    pos.Squares[(byte)Square.B8].IsEmpty &&
                    pos.Squares[(byte)Square.A8].Type == PieceType.Rook &&
                    pos.Squares[(byte)Square.A8].Color == PieceColor.Black &&
                    !IsSquareAttacked(pos, Square.E8, them) &&
                    !IsSquareAttacked(pos, Square.D8, them) &&
                    !IsSquareAttacked(pos, Square.C8, them))
                {
                    moves.Add(new Move(Square.E8, Square.C8, Flags: MoveFlags.Castling));
                }
            }
        }
    }

    private static void GenerateSlidingMoves(BoardPosition pos, Square from, int f, int r, (int df, int dr)[] directions, PieceColor them, List<Move> moves)
    {
        foreach (var (df, dr) in directions)
        {
            int tf = f + df;
            int tr = r + dr;
            while (tf >= 0 && tf < 8 && tr >= 0 && tr < 8)
            {
                var to = SquareExtensions.FromCoords(tf, tr);
                var p = pos[to];
                if (p.IsEmpty)
                {
                    moves.Add(new Move(from, to));
                }
                else
                {
                    if (p.Color == them)
                        moves.Add(new Move(from, to, Flags: MoveFlags.Capture));
                    break;
                }
                tf += df;
                tr += dr;
            }
        }
    }

    public static BoardPosition ApplyMove(BoardPosition pos, Move move)
    {
        var next = pos.Clone();
        var movingPiece = next[move.From];
        var targetPiece = next[move.To];

        // 1. Halfmove clock
        if (movingPiece.Type == PieceType.Pawn || !targetPiece.IsEmpty || move.IsEnPassant)
            next.HalfmoveClock = 0;
        else
            next.HalfmoveClock++;

        // 2. Fullmove number
        if (pos.ActiveColor == PieceColor.Black)
            next.FullmoveNumber++;

        // 3. Move piece
        next[move.From] = Piece.None;

        if (move.IsPromotion)
        {
            next[move.To] = new Piece(movingPiece.Color, move.Promotion);
        }
        else
        {
            next[move.To] = movingPiece;
        }

        // 4. En Passant capture removal
        if (move.IsEnPassant)
        {
            int capturedPawnRank = pos.ActiveColor == PieceColor.White ? move.To.Rank() - 1 : move.To.Rank() + 1;
            var capturedSq = SquareExtensions.FromCoords(move.To.File(), capturedPawnRank);
            next[capturedSq] = Piece.None;
        }

        // 5. Castling rook placement
        if (move.IsCastling)
        {
            if (move.To == Square.G1) { next[Square.H1] = Piece.None; next[Square.F1] = new Piece(PieceColor.White, PieceType.Rook); }
            else if (move.To == Square.C1) { next[Square.A1] = Piece.None; next[Square.D1] = new Piece(PieceColor.White, PieceType.Rook); }
            else if (move.To == Square.G8) { next[Square.H8] = Piece.None; next[Square.F8] = new Piece(PieceColor.Black, PieceType.Rook); }
            else if (move.To == Square.C8) { next[Square.A8] = Piece.None; next[Square.D8] = new Piece(PieceColor.Black, PieceType.Rook); }
        }

        // 6. Update Castling rights if king or rook moved or captured
        if (movingPiece.Type == PieceType.King)
        {
            if (movingPiece.Color == PieceColor.White)
                next.Castling &= ~CastlingRights.WhiteBoth;
            else
                next.Castling &= ~CastlingRights.BlackBoth;
        }

        if (move.From == Square.A1 || move.To == Square.A1) next.Castling &= ~CastlingRights.WhiteQueen;
        if (move.From == Square.H1 || move.To == Square.H1) next.Castling &= ~CastlingRights.WhiteKing;
        if (move.From == Square.A8 || move.To == Square.A8) next.Castling &= ~CastlingRights.BlackQueen;
        if (move.From == Square.H8 || move.To == Square.H8) next.Castling &= ~CastlingRights.BlackKing;

        // 7. En Passant square for next turn
        if (movingPiece.Type == PieceType.Pawn && Math.Abs(move.To.Rank() - move.From.Rank()) == 2)
        {
            int epRank = (move.From.Rank() + move.To.Rank()) / 2;
            next.EnPassantSquare = SquareExtensions.FromCoords(move.From.File(), epRank);
        }
        else
        {
            next.EnPassantSquare = null;
        }

        // 8. Turn flip
        next.ActiveColor = pos.ActiveColor == PieceColor.White ? PieceColor.Black : PieceColor.White;

        next.RecalculateZobristKey();
        return next;
    }
}
