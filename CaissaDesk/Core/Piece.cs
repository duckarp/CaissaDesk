namespace CaissaDesk.Core;

public enum PieceColor : byte
{
    White = 0,
    Black = 1
}

public enum PieceType : byte
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6
}

public readonly record struct Piece(PieceColor Color, PieceType Type)
{
    public static readonly Piece None = new(PieceColor.White, PieceType.None);

    public bool IsEmpty => Type == PieceType.None;
    public bool IsWhite => !IsEmpty && Color == PieceColor.White;
    public bool IsBlack => !IsEmpty && Color == PieceColor.Black;

    public char ToChar()
    {
        char c = Type switch
        {
            PieceType.Pawn => 'p',
            PieceType.Knight => 'n',
            PieceType.Bishop => 'b',
            PieceType.Rook => 'r',
            PieceType.Queen => 'q',
            PieceType.King => 'k',
            _ => '.'
        };
        return Color == PieceColor.White ? char.ToUpperInvariant(c) : c;
    }

    public static Piece FromChar(char c)
    {
        var color = char.IsUpper(c) ? PieceColor.White : PieceColor.Black;
        var type = char.ToLowerInvariant(c) switch
        {
            'p' => PieceType.Pawn,
            'n' => PieceType.Knight,
            'b' => PieceType.Bishop,
            'r' => PieceType.Rook,
            'q' => PieceType.Queen,
            'k' => PieceType.King,
            _ => PieceType.None
        };
        return type == PieceType.None ? None : new Piece(color, type);
    }
}
