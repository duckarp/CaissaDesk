namespace CaissaDesk.Core;

public record GameResultInfo(
    bool HasResult,
    string Score,          // e.g. "1-0", "0-1", "1/2-1/2"
    string DisplayScore,   // e.g. "1-0", "0-1", "½-½"
    string? Description,   // e.g. "White won by resignation", "Black won by checkmate", "Draw"
    string ResultClass     // "result-white-win", "result-black-win", "result-draw"
)
{
    public string Tooltip => string.IsNullOrEmpty(Description)
        ? $"Result: {DisplayScore}"
        : $"{DisplayScore} • {Description}";
}

public static class GameResultHelper
{
    public static GameResultInfo GetResult(GameTree? tree, BoardPosition? currentPosition = null)
    {
        if (tree?.Root == null)
        {
            return new GameResultInfo(false, "", "", null, "");
        }

        // 1. Check explicit Result header if present and not unknown '*'
        if (tree.Headers.TryGetValue("Result", out var rawRes) &&
            !string.IsNullOrWhiteSpace(rawRes) && rawRes.Trim() != "*")
        {
            return CreateFromHeader(rawRes, tree);
        }

        // 2. Check current node position for checkmate / stalemate
        var pos = currentPosition ?? tree.CurrentNode?.Position;
        if (pos != null)
        {
            if (MoveGenerator.IsCheckmate(pos))
            {
                bool whiteWon = pos.ActiveColor == PieceColor.Black;
                return new GameResultInfo(
                    HasResult: true,
                    Score: whiteWon ? "1-0" : "0-1",
                    DisplayScore: whiteWon ? "1-0" : "0-1",
                    Description: whiteWon ? "White won by checkmate" : "Black won by checkmate",
                    ResultClass: whiteWon ? "result-white-win" : "result-black-win"
                );
            }

            if (MoveGenerator.IsStalemate(pos))
            {
                return new GameResultInfo(
                    HasResult: true,
                    Score: "1/2-1/2",
                    DisplayScore: "½-½",
                    Description: "Draw by stalemate",
                    ResultClass: "result-draw"
                );
            }
        }

        // 3. Check mainline leaf position (end of game in tree)
        var leaf = tree.Root;
        while (leaf.Children.Count > 0)
        {
            leaf = leaf.Children[0];
        }

        if (leaf != tree.Root)
        {
            if (MoveGenerator.IsCheckmate(leaf.Position))
            {
                bool whiteWon = leaf.Position.ActiveColor == PieceColor.Black;
                return new GameResultInfo(
                    HasResult: true,
                    Score: whiteWon ? "1-0" : "0-1",
                    DisplayScore: whiteWon ? "1-0" : "0-1",
                    Description: whiteWon ? "White won by checkmate" : "Black won by checkmate",
                    ResultClass: whiteWon ? "result-white-win" : "result-black-win"
                );
            }

            if (MoveGenerator.IsStalemate(leaf.Position))
            {
                return new GameResultInfo(
                    HasResult: true,
                    Score: "1/2-1/2",
                    DisplayScore: "½-½",
                    Description: "Draw by stalemate",
                    ResultClass: "result-draw"
                );
            }
        }

        return new GameResultInfo(false, "", "", null, "");
    }

    private static GameResultInfo CreateFromHeader(string rawRes, GameTree tree)
    {
        string score = rawRes.Trim().Replace(" ", "");
        string displayScore = score == "1/2-1/2" ? "½-½" : score;
        string resultClass = score switch
        {
            "1-0" => "result-white-win",
            "0-1" => "result-black-win",
            _ => "result-draw"
        };

        string? desc = null;
        if (tree.Headers.TryGetValue("Termination", out var term) &&
            !string.IsNullOrWhiteSpace(term) && term != "Normal" && term != "unterminated")
        {
            if (term.Equals("Checkmate", StringComparison.OrdinalIgnoreCase))
            {
                desc = score == "1-0" ? "White won by checkmate" : (score == "0-1" ? "Black won by checkmate" : "Checkmate");
            }
            else if (term.Equals("Stalemate", StringComparison.OrdinalIgnoreCase))
            {
                desc = "Draw by stalemate";
            }
            else
            {
                desc = term;
            }
        }

        if (string.IsNullOrEmpty(desc))
        {
            // Check if end of mainline was checkmate or stalemate
            var leaf = tree.Root;
            while (leaf.Children.Count > 0)
            {
                leaf = leaf.Children[0];
            }

            if (leaf != tree.Root && MoveGenerator.IsCheckmate(leaf.Position))
            {
                desc = score == "1-0" ? "White won by checkmate" : "Black won by checkmate";
            }
            else if (leaf != tree.Root && MoveGenerator.IsStalemate(leaf.Position))
            {
                desc = "Draw by stalemate";
            }
            else
            {
                desc = score switch
                {
                    "1-0" => "White is victorious",
                    "0-1" => "Black is victorious",
                    "1/2-1/2" or "½-½" => "Draw",
                    _ => null
                };
            }
        }

        return new GameResultInfo(true, score, displayScore, desc, resultClass);
    }
}
