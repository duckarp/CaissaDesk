namespace CaissaDesk.Core;

public record struct CapturedPiece(PieceType Type, PieceColor Color);

public class BoardMaterialState
{
    public List<CapturedPiece> WhiteCaptured { get; init; } = new();
    public List<CapturedPiece> BlackCaptured { get; init; } = new();
    public int WhiteScore { get; init; }
    public int BlackScore { get; init; }
    public int WhiteAdvantage => Math.Max(0, WhiteScore - BlackScore);
    public int BlackAdvantage => Math.Max(0, BlackScore - WhiteScore);
    public bool HasCaptures => WhiteCaptured.Count > 0 || BlackCaptured.Count > 0;
}

public static class MaterialHelper
{
    public static int GetPieceValue(PieceType type) => type switch
    {
        PieceType.Pawn => 1,
        PieceType.Knight => 3,
        PieceType.Bishop => 3,
        PieceType.Rook => 5,
        PieceType.Queen => 9,
        _ => 0
    };

    public static BoardMaterialState GetMaterialState(BoardPosition? position)
    {
        if (position == null) return new BoardMaterialState();

        int whitePawns = 0, whiteKnights = 0, whiteBishops = 0, whiteRooks = 0, whiteQueens = 0;
        int blackPawns = 0, blackKnights = 0, blackBishops = 0, blackRooks = 0, blackQueens = 0;

        for (int i = 0; i < 64; i++)
        {
            var p = position.Squares[i];
            if (p.IsEmpty || p.Type == PieceType.King) continue;

            if (p.Color == PieceColor.White)
            {
                switch (p.Type)
                {
                    case PieceType.Pawn: whitePawns++; break;
                    case PieceType.Knight: whiteKnights++; break;
                    case PieceType.Bishop: whiteBishops++; break;
                    case PieceType.Rook: whiteRooks++; break;
                    case PieceType.Queen: whiteQueens++; break;
                }
            }
            else
            {
                switch (p.Type)
                {
                    case PieceType.Pawn: blackPawns++; break;
                    case PieceType.Knight: blackKnights++; break;
                    case PieceType.Bishop: blackBishops++; break;
                    case PieceType.Rook: blackRooks++; break;
                    case PieceType.Queen: blackQueens++; break;
                }
            }
        }

        int whiteScore = whitePawns * 1 + whiteKnights * 3 + whiteBishops * 3 + whiteRooks * 5 + whiteQueens * 9;
        int blackScore = blackPawns * 1 + blackKnights * 3 + blackBishops * 3 + blackRooks * 5 + blackQueens * 9;

        // Promoted pieces offset missing pawns
        int blackExtra = Math.Max(0, blackKnights - 2) + Math.Max(0, blackBishops - 2) + Math.Max(0, blackRooks - 2) + Math.Max(0, blackQueens - 1);
        int blackPawnsCaptured = Math.Max(0, 8 - blackPawns - blackExtra);

        // Captured by White = starting Black pieces - current Black pieces on board
        var whiteCaptured = new List<CapturedPiece>();
        AddCaptured(whiteCaptured, PieceType.Pawn, PieceColor.Black, blackPawnsCaptured);
        AddCaptured(whiteCaptured, PieceType.Knight, PieceColor.Black, Math.Max(0, 2 - blackKnights));
        AddCaptured(whiteCaptured, PieceType.Bishop, PieceColor.Black, Math.Max(0, 2 - blackBishops));
        AddCaptured(whiteCaptured, PieceType.Rook, PieceColor.Black, Math.Max(0, 2 - blackRooks));
        AddCaptured(whiteCaptured, PieceType.Queen, PieceColor.Black, Math.Max(0, 1 - blackQueens));

        int whiteExtra = Math.Max(0, whiteKnights - 2) + Math.Max(0, whiteBishops - 2) + Math.Max(0, whiteRooks - 2) + Math.Max(0, whiteQueens - 1);
        int whitePawnsCaptured = Math.Max(0, 8 - whitePawns - whiteExtra);

        // Captured by Black = starting White pieces - current White pieces on board
        var blackCaptured = new List<CapturedPiece>();
        AddCaptured(blackCaptured, PieceType.Pawn, PieceColor.White, whitePawnsCaptured);
        AddCaptured(blackCaptured, PieceType.Knight, PieceColor.White, Math.Max(0, 2 - whiteKnights));
        AddCaptured(blackCaptured, PieceType.Bishop, PieceColor.White, Math.Max(0, 2 - whiteBishops));
        AddCaptured(blackCaptured, PieceType.Rook, PieceColor.White, Math.Max(0, 2 - whiteRooks));
        AddCaptured(blackCaptured, PieceType.Queen, PieceColor.White, Math.Max(0, 1 - whiteQueens));

        return new BoardMaterialState
        {
            WhiteCaptured = whiteCaptured,
            BlackCaptured = blackCaptured,
            WhiteScore = whiteScore,
            BlackScore = blackScore
        };
    }

    private static void AddCaptured(List<CapturedPiece> list, PieceType type, PieceColor color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            list.Add(new CapturedPiece(type, color));
        }
    }
}
