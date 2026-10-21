namespace CaissaDesk.Core;

[Flags]
public enum MoveFlags : byte
{
    None = 0,
    Capture = 1 << 0,
    Castling = 1 << 1,
    EnPassant = 1 << 2,
    Promotion = 1 << 3
}

public readonly record struct Move(
    Square From,
    Square To,
    PieceType Promotion = PieceType.None,
    MoveFlags Flags = MoveFlags.None) : IEquatable<Move>
{
    public static readonly Move Empty = new(Square.None, Square.None);

    public bool IsEmpty => From == Square.None || To == Square.None || From == To;
    public bool IsCapture => (Flags & MoveFlags.Capture) != 0;
    public bool IsCastling => (Flags & MoveFlags.Castling) != 0;
    public bool IsEnPassant => (Flags & MoveFlags.EnPassant) != 0;
    public bool IsPromotion => (Flags & MoveFlags.Promotion) != 0 && Promotion != PieceType.None;

    public bool Equals(Move other)
    {
        if (IsEmpty && other.IsEmpty) return true;
        return From == other.From && To == other.To && Promotion == other.Promotion && Flags == other.Flags;
    }

    public override int GetHashCode()
    {
        if (IsEmpty) return 0;
        return HashCode.Combine(From, To, Promotion, Flags);
    }

    public string ToUci()
    {
        if (IsEmpty) return "0000";
        string promo = IsPromotion ? Promotion switch
        {
            PieceType.Queen => "q",
            PieceType.Rook => "r",
            PieceType.Bishop => "b",
            PieceType.Knight => "n",
            _ => ""
        } : "";
        return $"{From.Name()}{To.Name()}{promo}";
    }

    public override string ToString() => ToUci();
}
