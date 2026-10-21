using System.Text;

namespace CaissaDesk.Core;

public static class FenParser
{
    public static BoardPosition Parse(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
            fen = BoardPosition.StartFen;

        var pos = new BoardPosition();
        string[] parts = fen.Trim().Split(' ');
        if (parts.Length < 1)
            return pos;

        // 1. Piece placement
        string ranks = parts[0];
        string[] rankRows = ranks.Split('/');
        if (rankRows.Length != 8)
            throw new FormatException($"Invalid FEN board ranks: {ranks}");

        for (int rankIndex = 7; rankIndex >= 0; rankIndex--)
        {
            string row = rankRows[7 - rankIndex];
            int fileIndex = 0;
            foreach (char c in row)
            {
                if (char.IsDigit(c))
                {
                    fileIndex += c - '0';
                }
                else
                {
                    if (fileIndex < 8)
                    {
                        pos[fileIndex, rankIndex] = Piece.FromChar(c);
                        fileIndex++;
                    }
                }
            }
        }

        // 2. Active color
        if (parts.Length > 1)
        {
            pos.ActiveColor = parts[1].Equals("b", StringComparison.OrdinalIgnoreCase)
                ? PieceColor.Black
                : PieceColor.White;
        }

        // 3. Castling rights
        pos.Castling = CastlingRights.None;
        if (parts.Length > 2 && parts[2] != "-")
        {
            string castling = parts[2];
            if (castling.Contains('K')) pos.Castling |= CastlingRights.WhiteKing;
            if (castling.Contains('Q')) pos.Castling |= CastlingRights.WhiteQueen;
            if (castling.Contains('k')) pos.Castling |= CastlingRights.BlackKing;
            if (castling.Contains('q')) pos.Castling |= CastlingRights.BlackQueen;
        }

        // 4. En passant
        if (parts.Length > 3 && parts[3] != "-")
        {
            var sq = SquareExtensions.Parse(parts[3]);
            if (sq != Square.None)
                pos.EnPassantSquare = sq;
        }

        // 5. Halfmove clock
        if (parts.Length > 4 && int.TryParse(parts[4], out int halfmove))
        {
            pos.HalfmoveClock = halfmove;
        }

        // 6. Fullmove number
        if (parts.Length > 5 && int.TryParse(parts[5], out int fullmove))
        {
            pos.FullmoveNumber = Math.Max(1, fullmove);
        }

        pos.RecalculateZobristKey();
        return pos;
    }

    public static string ToFen(BoardPosition pos, bool includeCounters = true)
    {
        var sb = new StringBuilder();

        // 1. Piece placement
        for (int r = 7; r >= 0; r--)
        {
            int emptyCount = 0;
            for (int f = 0; f < 8; f++)
            {
                var p = pos[f, r];
                if (p.IsEmpty)
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        sb.Append(emptyCount);
                        emptyCount = 0;
                    }
                    sb.Append(p.ToChar());
                }
            }
            if (emptyCount > 0)
                sb.Append(emptyCount);
            if (r > 0)
                sb.Append('/');
        }

        // 2. Turn
        sb.Append(' ');
        sb.Append(pos.ActiveColor == PieceColor.White ? 'w' : 'b');

        // 3. Castling
        sb.Append(' ');
        if (pos.Castling == CastlingRights.None)
        {
            sb.Append('-');
        }
        else
        {
            if ((pos.Castling & CastlingRights.WhiteKing) != 0) sb.Append('K');
            if ((pos.Castling & CastlingRights.WhiteQueen) != 0) sb.Append('Q');
            if ((pos.Castling & CastlingRights.BlackKing) != 0) sb.Append('k');
            if ((pos.Castling & CastlingRights.BlackQueen) != 0) sb.Append('q');
        }

        // 4. En passant
        sb.Append(' ');
        sb.Append(pos.EnPassantSquare.HasValue ? pos.EnPassantSquare.Value.Name() : "-");

        if (includeCounters)
        {
            // 5. Halfmove clock
            sb.Append(' ');
            sb.Append(pos.HalfmoveClock);

            // 6. Fullmove number
            sb.Append(' ');
            sb.Append(pos.FullmoveNumber);
        }

        return sb.ToString();
    }
}
