using Caissa.Shared.Models;

namespace Caissa.Shared.Tiebreak;

public class TiebreakService : ITiebreakService
{
    private sealed class PlayerStats
    {
        public Player Player { get; set; } = default!;
        public double Points { get; set; }
        public int GamesPlayed { get; set; }
        public int Wins { get; set; }
        public int BlackGames { get; set; }
        public List<OpponentGame> Opponents { get; set; } = [];
    }

    private sealed class OpponentGame
    {
        public string OpponentId { get; set; } = string.Empty;
        public double PlayerScoreInGame { get; set; }
        public bool IsBlack { get; set; }
        public bool IsBye { get; set; }
        public int OpponentRating { get; set; }
    }

    public List<PlayerStanding> CalculateStandings(Tournament tournament, List<TiebreakType>? tiebreakOrder = null)
    {
        var order = tiebreakOrder ?? tournament.TiebreakOrder ?? TiebreakPreset.FideStandardSwiss.GetDefaultList();
        var players = tournament.Players.ToDictionary(p => p.Id, p => p);

        // 1. Gather baseline stats & games for each player
        var stats = new Dictionary<string, PlayerStats>();
        foreach (var p in tournament.Players)
        {
            stats[p.Id] = new PlayerStats
            {
                Player = p,
                Points = 0.0,
                GamesPlayed = 0,
                Wins = 0,
                BlackGames = 0,
                Opponents = []
            };
        }

        foreach (var round in tournament.Rounds)
        {
            foreach (var pairing in round.Pairings)
            {
                if (pairing.Result == MatchResult.NotPlayed) continue;

                var (wPts, bPts) = pairing.Result.ToPoints();

                // Normal game
                if (!string.IsNullOrEmpty(pairing.WhitePlayerId) && !string.IsNullOrEmpty(pairing.BlackPlayerId))
                {
                    if (stats.TryGetValue(pairing.WhitePlayerId, out var wStats) &&
                        stats.TryGetValue(pairing.BlackPlayerId, out var bStats))
                    {
                        wStats.Points += wPts;
                        wStats.GamesPlayed++;
                        if (wPts == 1.0) wStats.Wins++;
                        wStats.Opponents.Add(new OpponentGame
                        {
                            OpponentId = pairing.BlackPlayerId,
                            PlayerScoreInGame = wPts,
                            IsBlack = false,
                            IsBye = false,
                            OpponentRating = bStats.Player.EffectiveRating(tournament.SeedingPrecedence)
                        });

                        bStats.Points += bPts;
                        bStats.GamesPlayed++;
                        bStats.BlackGames++;
                        if (bPts == 1.0) bStats.Wins++;
                        bStats.Opponents.Add(new OpponentGame
                        {
                            OpponentId = pairing.WhitePlayerId,
                            PlayerScoreInGame = bPts,
                            IsBlack = true,
                            IsBye = false,
                            OpponentRating = wStats.Player.EffectiveRating(tournament.SeedingPrecedence)
                        });
                    }
                }
                // Bye game
                else if (pairing.IsBye)
                {
                    string? byePlayerId = pairing.WhitePlayerId ?? pairing.BlackPlayerId;
                    if (!string.IsNullOrEmpty(byePlayerId) && stats.TryGetValue(byePlayerId, out var byeStats))
                    {
                        double byePoints = (pairing.Result == MatchResult.ByeFull) ? 1.0 :
                                          (pairing.Result == MatchResult.ByeHalf) ? 0.5 : 0.0;
                        byeStats.Points += byePoints;
                        byeStats.GamesPlayed++;
                        if (byePoints == 1.0) byeStats.Wins++;
                        byeStats.Opponents.Add(new OpponentGame
                        {
                            OpponentId = string.Empty,
                            PlayerScoreInGame = byePoints,
                            IsBlack = false,
                            IsBye = true,
                            OpponentRating = byeStats.Player.EffectiveRating(tournament.SeedingPrecedence)
                        });
                    }
                }
            }
        }

        // Maximum possible score in the tournament (for Koya 50% threshold)
        double maxTournamentScore = tournament.TotalRounds > 0
            ? tournament.TotalRounds
            : (tournament.Rounds.Count > 0 ? tournament.Rounds.Count : Math.Max(1, tournament.Players.Count - 1));
        double koya50Threshold = maxTournamentScore * 0.5;

        // 2. Compute individual tiebreak values for each player
        var standings = new List<PlayerStanding>();

        foreach (var (playerId, pStat) in stats)
        {
            var standing = new PlayerStanding
            {
                PlayerId = playerId,
                Player = pStat.Player,
                Points = pStat.Points,
                GamesPlayed = pStat.GamesPlayed,
                Wins = pStat.Wins,
                BlackGames = pStat.BlackGames,
                TiebreakValues = []
            };

            // Pre-calculate opponent total scores
            var opponentScores = new List<double>();
            var opponentRatings = new List<int>();
            double sbScore = 0.0;
            double koyaScore = 0.0;

            foreach (var game in pStat.Opponents)
            {
                double oppScore;
                if (!game.IsBye && stats.TryGetValue(game.OpponentId, out var oppStat))
                {
                    oppScore = oppStat.Points;
                    opponentRatings.Add(game.OpponentRating);

                    // Koya: Points scored against opponents who scored at least 50% of max possible score
                    if (oppStat.Points >= koya50Threshold)
                    {
                        koyaScore += game.PlayerScoreInGame;
                    }
                }
                else
                {
                    // FIDE virtual opponent score for unplayed bye
                    oppScore = Math.Max(0.0, pStat.Points - game.PlayerScoreInGame);
                }

                opponentScores.Add(oppScore);

                if (game.PlayerScoreInGame == 1.0)
                {
                    sbScore += oppScore;
                }
                else if (game.PlayerScoreInGame == 0.5)
                {
                    sbScore += 0.5 * oppScore;
                }
            }

            opponentScores.Sort();

            // Buchholz Full
            double bhFull = opponentScores.Sum();
            standing.TiebreakValues[TiebreakType.Buchholz] = bhFull;

            // Buchholz Cut 1 (exclude lowest score)
            standing.TiebreakValues[TiebreakType.BuchholzCut1] =
                opponentScores.Count > 1 ? opponentScores.Skip(1).Sum() : bhFull;

            // Buchholz Cut 2 (exclude 2 lowest scores)
            standing.TiebreakValues[TiebreakType.BuchholzCut2] =
                opponentScores.Count > 2 ? opponentScores.Skip(2).Sum() : bhFull;

            // Median Buchholz (exclude highest and lowest)
            standing.TiebreakValues[TiebreakType.MedianBuchholz] =
                opponentScores.Count > 2 ? opponentScores.Skip(1).Take(opponentScores.Count - 2).Sum() : bhFull;

            // Sonneborn-Berger
            standing.TiebreakValues[TiebreakType.SonnebornBerger] = sbScore;

            // Koya System
            standing.TiebreakValues[TiebreakType.Koya] = koyaScore;

            // Extended Koya (baseline is Koya; step-down resolution evaluated during sorting)
            standing.TiebreakValues[TiebreakType.ExtendedKoya] = koyaScore;

            // Wins
            standing.TiebreakValues[TiebreakType.MostWins] = pStat.Wins;

            // Black Games
            standing.TiebreakValues[TiebreakType.MostBlack] = pStat.BlackGames;

            // AROC Full (uncut)
            standing.TiebreakValues[TiebreakType.Aroc] = opponentRatings.Count > 0 ? opponentRatings.Average() : 0.0;

            // AROC Cut 1
            if (opponentRatings.Count > 1)
            {
                opponentRatings.Sort();
                standing.TiebreakValues[TiebreakType.Aroc1] = opponentRatings.Skip(1).Average();
            }
            else if (opponentRatings.Count == 1)
            {
                standing.TiebreakValues[TiebreakType.Aroc1] = opponentRatings[0];
            }
            else
            {
                standing.TiebreakValues[TiebreakType.Aroc1] = 0.0;
            }

            // Direct Encounter placeholder (evaluated against tied players below)
            standing.TiebreakValues[TiebreakType.DirectEncounter] = 0.0;

            standings.Add(standing);
        }

        // 3. Sort standings using points and the ordered tiebreak hierarchy
        standings.Sort((a, b) =>
        {
            // Primary: Points descending
            int ptsComp = b.Points.CompareTo(a.Points);
            if (ptsComp != 0) return ptsComp;

            // Secondary: Configured tiebreak stack
            foreach (var tb in order)
            {
                if (tb == TiebreakType.DirectEncounter)
                {
                    // Compute pairwise head-to-head between a and b
                    double deA = GetHeadToHeadScore(tournament, a.PlayerId, b.PlayerId);
                    double deB = GetHeadToHeadScore(tournament, b.PlayerId, a.PlayerId);
                    int deComp = deB.CompareTo(deA);
                    if (deComp != 0) return deComp;
                }
                else if (tb == TiebreakType.ExtendedKoya)
                {
                    int ekComp = CompareExtendedKoya(a.PlayerId, b.PlayerId, stats, maxTournamentScore);
                    if (ekComp != 0) return ekComp;
                }
                else
                {
                    double valA = a.TiebreakValues.GetValueOrDefault(tb, 0.0);
                    double valB = b.TiebreakValues.GetValueOrDefault(tb, 0.0);
                    int tbComp = valB.CompareTo(valA);
                    if (tbComp != 0) return tbComp;
                }
            }

            // Fallback: Starting rank ascending (better seeded player wins tiebreak)
            return a.Player.StartRank.CompareTo(b.Player.StartRank);
        });

        // 4. Update Direct Encounter values for display among tied groups and set final ranks
        for (int i = 0; i < standings.Count; i++)
        {
            standings[i].Rank = i + 1;

            // Direct encounter points against players with the exact same tournament score
            var tiedPlayerIds = standings
                .Where(s => s.PlayerId != standings[i].PlayerId && Math.Abs(s.Points - standings[i].Points) < 0.001)
                .Select(s => s.PlayerId)
                .ToHashSet();

            if (tiedPlayerIds.Count > 0 && stats.TryGetValue(standings[i].PlayerId, out var pStat))
            {
                double deAgainstTied = pStat.Opponents
                    .Where(g => !g.IsBye && tiedPlayerIds.Contains(g.OpponentId))
                    .Sum(g => g.PlayerScoreInGame);
                standings[i].TiebreakValues[TiebreakType.DirectEncounter] = deAgainstTied;
            }
            else
            {
                standings[i].TiebreakValues[TiebreakType.DirectEncounter] = 0.0;
            }
        }

        return standings;
    }

    private static double GetHeadToHeadScore(Tournament tournament, string playerAId, string playerBId)
    {
        double score = 0.0;
        foreach (var round in tournament.Rounds)
        {
            foreach (var pairing in round.Pairings)
            {
                if (pairing.Result == MatchResult.NotPlayed) continue;

                var (wPts, bPts) = pairing.Result.ToPoints();
                if (pairing.WhitePlayerId == playerAId && pairing.BlackPlayerId == playerBId)
                {
                    score += wPts;
                }
                else if (pairing.BlackPlayerId == playerAId && pairing.WhitePlayerId == playerBId)
                {
                    score += bPts;
                }
            }
        }
        return score;
    }

    private static int CompareExtendedKoya(string playerAId, string playerBId, Dictionary<string, PlayerStats> stats, double maxScore)
    {
        if (!stats.TryGetValue(playerAId, out var statsA) || !stats.TryGetValue(playerBId, out var statsB))
            return 0;

        // Step down from 50% threshold in steps of 0.5 points
        for (double t = maxScore * 0.5; t >= 0; t -= 0.5)
        {
            double scoreA = statsA.Opponents
                .Where(g => !g.IsBye && stats.TryGetValue(g.OpponentId, out var opp) && opp.Points >= t)
                .Sum(g => g.PlayerScoreInGame);

            double scoreB = statsB.Opponents
                .Where(g => !g.IsBye && stats.TryGetValue(g.OpponentId, out var opp) && opp.Points >= t)
                .Sum(g => g.PlayerScoreInGame);

            int comp = scoreB.CompareTo(scoreA); // descending
            if (comp != 0) return comp;
        }

        return 0;
    }
}
