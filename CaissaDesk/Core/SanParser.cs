using System.Text;

namespace CaissaDesk.Core;

public static class SanParser
{
    public static string ToSan(BoardPosition pos, Move move)
    {
        if (move.IsEmpty) return "";

        var p = pos[move.From];
        var legalMoves = MoveGenerator.GenerateLegalMoves(pos);
        var nextPos = MoveGenerator.ApplyMove(pos, move);
        bool isCheck = MoveGenerator.IsInCheck(nextPos, nextPos.ActiveColor);
        bool isCheckmate = isCheck && MoveGenerator.GenerateLegalMoves(nextPos).Count == 0;

        string checkSuffix = isCheckmate ? "#" : (isCheck ? "+" : "");

        // Castling
        if (move.IsCastling)
        {
            return (move.To.File() > move.From.File() ? "O-O" : "O-O-O") + checkSuffix;
        }

        var sb = new StringBuilder();

        if (p.Type == PieceType.Pawn)
        {
            if (move.IsCapture || move.IsEnPassant)
            {
                sb.Append((char)('a' + move.From.File()));
                sb.Append('x');
            }
            sb.Append(move.To.Name());

            if (move.IsPromotion)
            {
                sb.Append('=');
                sb.Append(move.Promotion switch
                {
                    PieceType.Queen => 'Q',
                    PieceType.Rook => 'R',
                    PieceType.Bishop => 'B',
                    PieceType.Knight => 'N',
                    _ => 'Q'
                });
            }
        }
        else
        {
            // Piece prefix (N, B, R, Q, K)
            sb.Append(char.ToUpperInvariant(p.ToChar()));

            // Disambiguation
            var candidates = legalMoves.Where(m =>
                m.To == move.To &&
                m.From != move.From &&
                pos[m.From].Type == p.Type &&
                pos[m.From].Color == p.Color).ToList();

            if (candidates.Count > 0)
            {
                bool needFile = false;
                bool needRank = false;

                bool sameFile = candidates.Any(c => c.From.File() == move.From.File());
                bool sameRank = candidates.Any(c => c.From.Rank() == move.From.Rank());

                if (!sameFile)
                {
                    needFile = true;
                }
                else if (!sameRank)
                {
                    needRank = true;
                }
                else
                {
                    needFile = true;
                    needRank = true;
                }

                if (needFile) sb.Append((char)('a' + move.From.File()));
                if (needRank) sb.Append((char)('1' + move.From.Rank()));
            }

            if (move.IsCapture)
            {
                sb.Append('x');
            }

            sb.Append(move.To.Name());
        }

        sb.Append(checkSuffix);
        return sb.ToString();
    }

    public static Move ParseSan(BoardPosition pos, string san)
    {
        if (string.IsNullOrWhiteSpace(san))
            return Move.Empty;

        san = san.Trim().TrimEnd('+', '#', '!', '?');
        var legalMoves = MoveGenerator.GenerateLegalMoves(pos);

        // Quick exact match against generated SANs
        foreach (var m in legalMoves)
        {
            string s = ToSan(pos, m).TrimEnd('+', '#', '!', '?');
            if (s.Equals(san, StringComparison.OrdinalIgnoreCase))
                return m;
        }

        // Castling fallback
        if (san.Equals("O-O", StringComparison.OrdinalIgnoreCase) || san.Equals("0-0", StringComparison.OrdinalIgnoreCase))
            return legalMoves.FirstOrDefault(m => m.IsCastling && m.To.File() == 6);
        if (san.Equals("O-O-O", StringComparison.OrdinalIgnoreCase) || san.Equals("0-0-0", StringComparison.OrdinalIgnoreCase))
            return legalMoves.FirstOrDefault(m => m.IsCastling && m.To.File() == 2);

        // Some PGN sources include disambiguation even when only one piece of the
        // type can reach the destination (e.g. "Nge2" where "Ne2" suffices).
        if (san.Length >= 3 && char.IsUpper(san[0]))
        {
            PieceType? type = char.ToUpperInvariant(san[0]) switch
            {
                'N' => PieceType.Knight,
                'B' => PieceType.Bishop,
                'R' => PieceType.Rook,
                'Q' => PieceType.Queen,
                'K' => PieceType.King,
                _ => null
            };

            if (type is not null &&
                SquareExtensions.Parse(san[^2..]) is Square target &&
                target != Square.None)
            {
                string mid = san[1..^2];
                string disamb = mid.EndsWith("x") ? mid[..^1] : mid;

                byte? file = null;
                byte? rank = null;
                foreach (var c in disamb)
                {
                    if (c >= 'a' && c <= 'h') file = (byte)(c - 'a');
                    else if (c >= '1' && c <= '8') rank = (byte)(c - '1');
                }

                if (file is not null || rank is not null)
                {
                    foreach (var m in legalMoves)
                    {
                        if (pos[m.From].Type != type.Value || m.To != target)
                            continue;
                        if (file is not null && m.From.File() != file)
                            continue;
                        if (rank is not null && m.From.Rank() != rank)
                            continue;
                        return m;
                    }
                }
            }
        }

        return Move.Empty;
    }
}
