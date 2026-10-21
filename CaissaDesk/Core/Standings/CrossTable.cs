namespace CaissaDesk.Core.Standings;

/// <summary>Cross-table (Berger matrix) cell helpers shared by the standings view and the print sheets.</summary>
public static class CrossTable
{
    /// <summary>
    /// The score <paramref name="playerId"/> made against <paramref name="opponentId"/> in their first decided
    /// game ("1", "½", "0"), or "-" when they have not met or the game has no result yet.
    /// </summary>
    public static string GetHeadToHeadResult(Tournament? tournament, string playerId, string opponentId)
    {
        if (tournament == null) return "-";

        foreach (var round in tournament.Rounds)
        {
            var match = round.Pairings.FirstOrDefault(p =>
                (p.WhitePlayerId == playerId && p.BlackPlayerId == opponentId) ||
                (p.WhitePlayerId == opponentId && p.BlackPlayerId == playerId));

            if (match == null || match.Result == MatchResult.NotPlayed) continue;

            var (wPts, bPts) = match.Result.ToPoints();
            return FormatScore(match.WhitePlayerId == playerId ? wPts : bPts);
        }

        return "-";
    }

    public static string FormatScore(double points) => points switch
    {
        1.0 => "1",
        0.5 => "½",
        0.0 => "0",
        _ => "-"
    };
}
