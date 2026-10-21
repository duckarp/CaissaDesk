using System.Globalization;
using System.Text;
using Caissa.Shared.Models;

namespace Caissa.Shared.PairingEngine;

public static class TrfHandler
{
    public static string GenerateTrf(Tournament tournament) => GenerateTrf(tournament, pairingRound: null);

    /// <summary>
    /// Generates compliant FIDE TRF (Tournament Report File, TRF-16) 132-column text.
    /// Used by FIDE rating servers and external pairing engines (JaVaFo &amp; bbpPairings).
    /// </summary>
    /// <param name="pairingRound">
    /// When set, the round about to be paired: players who must not be paired in it (withdrawn, paused,
    /// or with an advance bye request) get a pre-filled entry for that round so external engines skip them.
    /// </param>
    public static string GenerateTrf(Tournament tournament, int? pairingRound)
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
        sb.AppendLine($"XXR {Math.Max(Math.Max(1, tournament.TotalRounds), pairingRound ?? 0)}");
        sb.AppendLine("XXC white1");

        // 2. Pre-calculate points and per-round entries for players
        var sortedPlayers = tournament.Players.OrderBy(p => p.StartRank).ToList();

        // playerId -> (round number -> entry). Rounds are indexed by number so that a round in which a
        // player has no entry still occupies its own column (padded below) instead of shifting later rounds.
        var playerRounds = new Dictionary<string, Dictionary<int, TrfRoundEntry>>();
        var playerPoints = new Dictionary<string, double>();

        foreach (var p in tournament.Players)
        {
            playerRounds[p.Id] = [];
            playerPoints[p.Id] = 0.0;
        }

        var rounds = tournament.Rounds
            .Where(r => pairingRound == null || r.RoundNumber < pairingRound)
            .OrderBy(r => r.RoundNumber)
            .ToList();

        foreach (var round in rounds)
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

                        playerRounds[white.Id][round.RoundNumber] = new(black.StartRank, 'w', wRes);
                        playerRounds[black.Id][round.RoundNumber] = new(white.StartRank, 'b', bRes);
                    }
                }
                // Bye
                else if (pairing.IsBye)
                {
                    string? byePlayerId = pairing.WhitePlayerId ?? pairing.BlackPlayerId;
                    if (!string.IsNullOrEmpty(byePlayerId) && playerPoints.ContainsKey(byePlayerId))
                    {
                        playerPoints[byePlayerId] += pairing.Result.ByePoints();
                        playerRounds[byePlayerId][round.RoundNumber] = new(0, '-', GetTrfResultChar(pairing.Result, isWhite: true));
                    }
                }
            }
        }

        // Players not on any board of a round that took place were absent: unpaired, zero points.
        foreach (var round in rounds.Where(r => r.Pairings.Any(pp => pp.Result != MatchResult.NotPlayed)))
        {
            foreach (var p in tournament.Players)
            {
                bool onBoard = round.Pairings.Any(pp => pp.WhitePlayerId == p.Id || pp.BlackPlayerId == p.Id);
                if (!onBoard)
                {
                    playerRounds[p.Id][round.RoundNumber] = new(0, '-', 'Z');
                }
            }
        }

        // Round being paired: tell the engine who must not be paired.
        if (pairingRound is int nextRound)
        {
            foreach (var p in tournament.Players)
            {
                if (p.Status != PlayerStatus.Active)
                {
                    playerRounds[p.Id][nextRound] = new(0, '-', 'Z');
                }
                else if (p.ByeRequests.TryGetValue(nextRound, out var byeType) && byeType != ByeType.None)
                {
                    var byeResult = byeType.ToMatchResult();
                    playerPoints[p.Id] += byeResult.ByePoints();
                    playerRounds[p.Id][nextRound] = new(0, '-', GetTrfResultChar(byeResult, isWhite: true));
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

            // Round results (Column 92 onward, 10 chars per round, indexed by round number)
            var entries = playerRounds.GetValueOrDefault(p.Id, []);
            int maxRound = entries.Count > 0 ? entries.Keys.Max() : 0;
            for (int r = 1; r <= maxRound; r++)
            {
                if (entries.TryGetValue(r, out var e))
                {
                    // Format: "  12 w 1  " / "0000 - U  "
                    string oppStr = e.OpponentRank > 0 ? e.OpponentRank.ToString().PadLeft(4) : "0000";
                    line.Append($"{oppStr} {e.Color} {e.ResultChar}  ");
                }
                else
                {
                    // Not yet played / unknown: keep the column so later rounds stay aligned
                    line.Append(new string(' ', 10));
                }
            }

            sb.AppendLine(line.ToString().TrimEnd());
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
            MatchResult.ByeFull => 'U',          // pairing-allocated bye
            MatchResult.ByeFullRequested => 'F', // requested full-point bye
            MatchResult.ByeHalf => 'H',
            MatchResult.ByeZero => 'Z',
            _ => ' '
        };
    }

    private readonly record struct TrfRoundEntry(int OpponentRank, char Color, char ResultChar);
}
