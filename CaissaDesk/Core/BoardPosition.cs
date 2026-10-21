using System.Text;

namespace CaissaDesk.Core;

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKing = 1 << 0,
    WhiteQueen = 1 << 1,
    BlackKing = 1 << 2,
    BlackQueen = 1 << 3,
    WhiteBoth = WhiteKing | WhiteQueen,
    BlackBoth = BlackKing | BlackQueen,
    All = WhiteBoth | BlackBoth
}

public class BoardPosition
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public Piece[] Squares { get; } = new Piece[64];
    public PieceColor ActiveColor { get; set; } = PieceColor.White;
    public CastlingRights Castling { get; set; } = CastlingRights.All;
    public Square? EnPassantSquare { get; set; } = null;
    public int HalfmoveClock { get; set; } = 0;
    public int FullmoveNumber { get; set; } = 1;
    public ulong ZobristKey { get; private set; }

    public Piece this[Square sq]
    {
        get => sq != Square.None ? Squares[(byte)sq] : Piece.None;
        set
        {
            if (sq != Square.None)
                Squares[(byte)sq] = value;
        }
    }

    public Piece this[int file, int rank]
    {
        get => this[SquareExtensions.FromCoords(file, rank)];
        set => this[SquareExtensions.FromCoords(file, rank)] = value;
    }

    public BoardPosition Clone()
    {
        var copy = new BoardPosition
        {
            ActiveColor = ActiveColor,
            Castling = Castling,
            EnPassantSquare = EnPassantSquare,
            HalfmoveClock = HalfmoveClock,
            FullmoveNumber = FullmoveNumber,
            ZobristKey = ZobristKey
        };
        Array.Copy(Squares, copy.Squares, 64);
        return copy;
    }

    public Square FindKing(PieceColor color)
    {
        for (int i = 0; i < 64; i++)
        {
            var p = Squares[i];
            if (p.Color == color && p.Type == PieceType.King)
                return (Square)i;
        }
        return Square.None;
    }

    public void RecalculateZobristKey()
    {
        ZobristKey = Zobrist.Compute(this);
    }
}

public static class Zobrist
{
    private static readonly ulong[,,] PieceKeys = new ulong[64, 2, 7];
    private static readonly ulong[] CastlingKeys = new ulong[16];
    private static readonly ulong[] EnPassantKeys = new ulong[65];
    private static readonly ulong SideKey;

    static Zobrist()
    {
        var rng = new Random(20260907);
        for (int sq = 0; sq < 64; sq++)
            for (int col = 0; col < 2; col++)
                for (int pt = 1; pt <= 6; pt++)
                    PieceKeys[sq, col, pt] = NextUlong(rng);

        for (int i = 0; i < 16; i++)
            CastlingKeys[i] = NextUlong(rng);

        for (int i = 0; i < 65; i++)
            EnPassantKeys[i] = NextUlong(rng);

        SideKey = NextUlong(rng);
    }

    private static ulong NextUlong(Random rng)
    {
        var buf = new byte[8];
        rng.NextBytes(buf);
        return BitConverter.ToUInt64(buf, 0);
    }

    public static ulong Compute(BoardPosition pos)
    {
        ulong h = 0;
        for (int sq = 0; sq < 64; sq++)
        {
            var p = pos.Squares[sq];
            if (!p.IsEmpty)
                h ^= PieceKeys[sq, (byte)p.Color, (byte)p.Type];
        }

        h ^= CastlingKeys[(byte)pos.Castling];

        if (pos.EnPassantSquare.HasValue)
            h ^= EnPassantKeys[(byte)pos.EnPassantSquare.Value];
        else
            h ^= EnPassantKeys[64];

        if (pos.ActiveColor == PieceColor.Black)
            h ^= SideKey;

        return h;
    }
}
