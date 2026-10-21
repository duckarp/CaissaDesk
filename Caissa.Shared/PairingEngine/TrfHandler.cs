using System.Globalization;
using System.Text;
using Caissa.Shared.Models;

namespace Caissa.Shared.PairingEngine;

public static class TrfHandler
{
    /// <summary>
    /// Generates compliant FIDE TRF (Tournament Report File) 132-column text.
    /// Used by FIDE rating servers and external pairing engines (JaVaFo & bbpPairings).
    /// </summary>
    public static string GenerateTrf(Tournament tournament)
    {
        var sb = new StringBuilder();

        // 1. Header lines
        sb.AppendLine($"012 {tournament.Name}");
        if (!string.IsNullOrEmpty(tournament.Venue))
            sb.AppendLine($"022 {tournament.Venue}");
        sb.AppendLine($"032 {tournament.Federation}");
        sb.AppendLine($"042 {tournament.StartDate:yyyy/MM/dd}");
        sb.AppendLine($"052 {tournament.EndDate:yyyy/MM/dd}");
        sb.AppendLine($"062 {tournament.Players.Count}");
        sb.AppendLine($"072 {tournament.Players.Count(p => p.FideRating > 0)}");
        sb.AppendLine("082 0"); // Teams
        sb.AppendLine($"092 {(tournament.System == TournamentSystem.SwissDutch ? "Individual Swiss" : "Round Robin")}");
        if (!string.IsNullOrEmpty(tournament.ChiefArbiter))
            sb.AppendLine($"102 {tournament.ChiefArbiter}");
        if (!string.IsNullOrEmpty(tournament.DeputyArbiter))
            sb.AppendLine($"112 {tournament.DeputyArbiter}");
        sb.AppendLine($"122 {tournament.TimeControlDetails}");
        sb.AppendLine($"XXR {Math.Max(1, tournament.TotalRounds)}");
        sb.AppendLine("XXC white1");

        // 2. Pre-calculate points and ranks for players
        var sortedPlayers = tournament.Players.OrderBy(p => p.StartRank).ToList();

        // Build mapping: playerId -> list of round entries (opponentStartRank, color, resultChar)
        var playerRounds = new Dictionary<string, List<(int OpponentRank, char Color, char ResultChar)>>();
        var playerPoints = new Dictionary<string, double>();

        foreach (var p in tournament.Players)
        {
            playerRounds[p.Id] = [];
            playerPoints[p.Id] = 0.0;
        }

        var completedRounds = tournament.Rounds.OrderBy(r => r.RoundNumber).ToList();

        foreach (var round in completedRounds)
        {
            foreach (var pairing in round.Pairings)
            {
                if (pairing.Result == MatchResult.NotPlayed) continue;

                var (wPts, bPts) = pairing.Result.ToPoints();

                // Normal two-player game
                if (!string.IsNullOrEmpty(pairing.WhitePlayerId) && !string.IsNullOrEmpty(pairing.BlackPlayerId))
                {
                    var white = tournament.GetPlayer(pairing.WhitePlayerId);
                    var black = tournament.GetPlayer(pairing.BlackPlayerId);

                    if (white != null && black != null)
                    {
                        playerPoints[white.Id] += wPts;
                        playerPoints[black.Id] += bPts;

                        char wRes = GetTrfResultChar(pairing.Result, isWhite: true);
                        char bRes = GetTrfResultChar(pairing.Result, isWhite: false);

                        playerRounds[white.Id].Add((black.StartRank, 'w', wRes));
                        playerRounds[black.Id].Add((white.StartRank, 'b', bRes));
                    }
                }
                // Bye
                else if (pairing.IsBye)
                {
                    string? byePlayerId = pairing.WhitePlayerId ?? pairing.BlackPlayerId;
                    if (!string.IsNullOrEmpty(byePlayerId) && playerPoints.ContainsKey(byePlayerId))
                    {
                        double pts = (pairing.Result == MatchResult.ByeFull) ? 1.0 :
                                     (pairing.Result == MatchResult.ByeHalf) ? 0.5 : 0.0;
                        playerPoints[byePlayerId] += pts;

                        char byeChar = (pts == 1.0) ? '1' : (pts == 0.5) ? 'H' : 'Z';
                        playerRounds[byePlayerId].Add((0, '-', byeChar));
                    }
                }
            }
        }

        // Rank by points descending, then seed
        var rankMap = sortedPlayers
            .OrderByDescending(p => playerPoints[p.Id])
            .ThenBy(p => p.StartRank)
            .Select((p, idx) => (p.Id, Rank: idx + 1))
            .ToDictionary(x => x.Id, x => x.Rank);

        // 3. Emit 001 player lines
        foreach (var p in sortedPlayers)
        {
            var line = new StringBuilder();

            // ID code (1-3)
            line.Append("001 ");

            // Starting rank (5-8, right-aligned)
            line.Append(p.StartRank.ToString().PadLeft(4));
            line.Append(' ');

            // Sex (10)
            char sex = (p.Gender?.ToUpperInvariant() == "F" || p.Gender?.ToUpperInvariant() == "W") ? 'w' : 'm';
            line.Append(sex);

            // Title (11-13)
            string title = (p.Title ?? "").PadRight(3);
            if (title.Length > 3) title = title[..3];
            line.Append(title);
            line.Append(' ');

            // Name (15-47, 33 chars, left-aligned)
            string name = p.FullName.PadRight(33);
            if (name.Length > 33) name = name[..33];
            line.Append(name);
            line.Append(' ');

            // FIDE Rating (49-52, 4 chars, right-aligned)
            string fideRating = p.FideRating > 0 ? p.FideRating.ToString().PadLeft(4) : "   0";
            line.Append(fideRating);
            line.Append(' ');

            // Federation (54-56, 3 chars)
            string fed = (p.Federation ?? "FID").PadRight(3);
            if (fed.Length > 3) fed = fed[..3];
            line.Append(fed);
            line.Append(' ');

            // FIDE ID (58-68, 11 chars, right-aligned)
            string fideId = (p.FideId ?? "0").PadLeft(11);
            if (fideId.Length > 11) fideId = fideId[..11];
            line.Append(fideId);
            line.Append(' ');

            // Birth date (70-79, 10 chars: YYYY/MM/DD or YYYY/00/00)
            string bdate = p.BirthYear.HasValue ? $"{p.BirthYear.Value}/00/00" : "0000/00/00";
            line.Append(bdate.PadRight(10));
            line.Append(' ');

            // Points (81-84, 4 chars: e.g. " 4.5")
            double pts = playerPoints.GetValueOrDefault(p.Id, 0.0);
            string ptsStr = pts.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4);
            line.Append(ptsStr);
            line.Append(' ');

            // Rank (86-89, 4 chars, right-aligned)
            int rank = rankMap.GetValueOrDefault(p.Id, p.StartRank);
            line.Append(rank.ToString().PadLeft(4));
            line.Append("  ");

            // Round results (Column 92 onward, 10 chars per round)
            var rounds = playerRounds.GetValueOrDefault(p.Id, []);
            foreach (var (oppRank, color, resChar) in rounds)
            {
                // Format: "  12 w 1  "
                string oppStr = oppRank > 0 ? oppRank.ToString().PadLeft(4) : "   0";
                line.Append($"{oppStr} {color} {resChar}  ");
            }

            sb.AppendLine(line.ToString());
        }

        return sb.ToString();
    }

    private static char GetTrfResultChar(MatchResult result, bool isWhite)
    {
        return result switch
        {
            MatchResult.WhiteWin => isWhite ? '1' : '0',
            MatchResult.BlackWin => isWhite ? '0' : '1',
            MatchResult.Draw => '=',
            MatchResult.WhiteForfeit => isWhite ? '+' : '-',
            MatchResult.BlackForfeit => isWhite ? '-' : '+',
            MatchResult.DoubleForfeit => '-',
            MatchResult.ByeFull => '1',
            MatchResult.ByeHalf => 'H',
            MatchResult.ByeZero => 'Z',
            _ => ' '
        };
    }
}
