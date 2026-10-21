using Caissa.Shared.Models;

namespace Caissa.Shared.Tiebreak;

/// <summary>
/// Standings and tie-breaks following FIDE C.07 (2023): unplayed rounds per Article 16 (Swiss),
/// Direct Encounter per Article 6, ARO / games with Black over the board only.
/// </summary>
public class TiebreakService : ITiebreakService
{
    private enum EntryKind
    {
        Played,          // game played over the board
        ForfeitWin,
        ForfeitLoss,     // includes double forfeit
        PairingBye,      // pairing-allocated bye (16.2.1)
        FullBye,         // requested full-point bye (16.2.1)
        RequestedBye,    // requested ½ / 0 point bye (16.2.3 / 16.2.5)
        Absent           // not on any board in a round that took place; treated like a 0-point requested bye
    }

    private sealed class RoundEntry
    {
        public int RoundNumber { get; init; }
        public EntryKind Kind { get; init; }
        public double Points { get; init; }
        public string? OpponentId { get; init; }
        public bool IsBlack { get; init; }

        /// <summary>Forfeit losses and requested byes: the "voluntarily unplayed" rounds of 16.5.</summary>
        public bool IsVoluntaryUnplayed => Kind is EntryKind.ForfeitLoss or EntryKind.RequestedBye or EntryKind.Absent;
    }

    private sealed class PlayerStats
    {
        public Player Player { get; set; } = default!;
        public double Points { get; set; }
        public List<RoundEntry> Entries { get; } = [];

        /// <summary>Score used for this player when it appears as someone else's opponent (16.3).</summary>
        public double AdjustedScore { get; set; }
    }

    private sealed record Contribution(double Value, bool Voluntary);

    public List<PlayerStanding> CalculateStandings(Tournament tournament, List<TiebreakType>? tiebreakOrder = null)
    {
        var order = tiebreakOrder ?? tournament.TiebreakOrder ?? TiebreakPreset.FideStandardSwiss.GetDefaultList();

        // Round robin pairings are predetermined: forfeits there count as regular games (C.07 15.2)
        bool predetermined = tournament.System == TournamentSystem.RoundRobin;
        bool isSwiss = !predetermined;

        var stats = BuildStats(tournament);

        // Maximum possible score in the tournament (for Koya 50% threshold)
        double maxTournamentScore = tournament.TotalRounds > 0
            ? tournament.TotalRounds
            : (tournament.Rounds.Count > 0 ? tournament.Rounds.Count : Math.Max(1, tournament.Players.Count - 1));
        double koya50Threshold = maxTournamentScore * 0.5;

        bool CountsAsGame(RoundEntry e) =>
            e.OpponentId != null && stats.ContainsKey(e.OpponentId) &&
            (e.Kind == EntryKind.Played || (predetermined && e.Kind is EntryKind.ForfeitWin or EntryKind.ForfeitLoss));

        // 16.3: an opponent's score, for my tie-breaks, counts trailing requested byes (16.2.5) as draws
        foreach (var s in stats.Values)
        {
            s.AdjustedScore = isSwiss ? ComputeAdjustedScore(s) : s.Points;
        }

        // 2. Compute individual tiebreak values for each player
        var standings = new List<PlayerStanding>();

        foreach (var (playerId, pStat) in stats)
        {
            var standing = new PlayerStanding
            {
                PlayerId = playerId,
                Player = pStat.Player,
                Points = pStat.Points,
                GamesPlayed = pStat.Entries.Count(e => e.Kind != EntryKind.Absent),
                // WIN (C.07 7.1): rounds scored as a win, with or without playing
                Wins = pStat.Entries.Count(e => e.Points == 1.0),
                // BPG (C.07 7.3): games played over the board with Black
                BlackGames = pStat.Entries.Count(e => e.Kind == EntryKind.Played && e.IsBlack),
                TiebreakValues = []
            };

            var contributions = new List<Contribution>();
            var opponentRatings = new List<int>();
            double sbScore = 0.0;
            double koyaScore = 0.0;

            foreach (var entry in pStat.Entries)
            {
                if (CountsAsGame(entry))
                {
                    var opp = stats[entry.OpponentId!];
                    contributions.Add(new Contribution(opp.AdjustedScore, entry.IsVoluntaryUnplayed));
                    sbScore += entry.Points * opp.AdjustedScore;

                    if (opp.Points >= koya50Threshold)
                    {
                        koyaScore += entry.Points;
                    }
                }
                else if (isSwiss)
                {
                    // 16.4: an unplayed round is a game against a dummy opponent with the participant's own score
                    contributions.Add(new Contribution(pStat.Points, entry.IsVoluntaryUnplayed));
                    sbScore += entry.Points * pStat.Points;
                }

                // ARO (C.07 10.1): opponents played over the board only
                if (entry.Kind == EntryKind.Played && entry.OpponentId != null && stats.TryGetValue(entry.OpponentId, out var ratedOpp))
                {
                    opponentRatings.Add(ratedOpp.Player.EffectiveRating(tournament.SeedingPrecedence));
                }
            }

            double bhFull = contributions.Sum(c => c.Value);
            standing.TiebreakValues[TiebreakType.Buchholz] = bhFull;
            standing.TiebreakValues[TiebreakType.BuchholzCut1] = contributions.Count > 1 ? SumAfterCuts(contributions, 1, 0) : bhFull;
            standing.TiebreakValues[TiebreakType.BuchholzCut2] = contributions.Count > 2 ? SumAfterCuts(contributions, 2, 0) : bhFull;
            standing.TiebreakValues[TiebreakType.MedianBuchholz] = contributions.Count > 2 ? SumAfterCuts(contributions, 1, 1) : bhFull;

            standing.TiebreakValues[TiebreakType.SonnebornBerger] = sbScore;
            standing.TiebreakValues[TiebreakType.Koya] = koyaScore;
            // Extended Koya (baseline is Koya; step-down resolution evaluated during sorting)
            standing.TiebreakValues[TiebreakType.ExtendedKoya] = koyaScore;
            standing.TiebreakValues[TiebreakType.MostWins] = standing.Wins;
            standing.TiebreakValues[TiebreakType.MostBlack] = standing.BlackGames;

            standing.TiebreakValues[TiebreakType.Aroc] = opponentRatings.Count > 0 ? opponentRatings.Average() : 0.0;
            if (opponentRatings.Count > 1)
            {
                opponentRatings.Sort();
                standing.TiebreakValues[TiebreakType.Aroc1] = opponentRatings.Skip(1).Average();
            }
            else
            {
                standing.TiebreakValues[TiebreakType.Aroc1] = opponentRatings.Count == 1 ? opponentRatings[0] : 0.0;
            }

            // Direct Encounter display value: score against players on the same points
            double deAgainstTied = pStat.Entries
                .Where(e => CountsAsGame(e) && Math.Abs(stats[e.OpponentId!].Points - pStat.Points) < 0.001)
                .Sum(e => e.Points);
            standing.TiebreakValues[TiebreakType.DirectEncounter] = deAgainstTied;

            standings.Add(standing);
        }

        // 3. Rank: points, then each tie-break in turn applied to the groups still tied (C.07 4.2).
        // Tie-breaks are evaluated per group rather than pairwise, so Direct Encounter is a proper
        // mini-tournament among all tied players and the ordering is always consistent.
        var ranked = new List<PlayerStanding>();
        foreach (var group in PartitionByValue(standings, s => s.Points))
        {
            ranked.AddRange(OrderGroup(group, 0));
        }

        List<PlayerStanding> OrderGroup(List<PlayerStanding> group, int tbIndex)
        {
            if (group.Count <= 1) return group;
            if (tbIndex >= order.Count)
            {
                // Fallback: Starting rank ascending (better seeded player wins tiebreak)
                return group.OrderBy(s => s.Player.StartRank).ToList();
            }

            var tb = order[tbIndex];
            List<List<PlayerStanding>> parts = tb switch
            {
                TiebreakType.DirectEncounter => PartitionByDirectEncounter(group, stats, CountsAsGame),
                TiebreakType.ExtendedKoya => PartitionByComparer(group, (a, b) => CompareExtendedKoya(a.PlayerId, b.PlayerId, stats, maxTournamentScore)),
                _ => PartitionByValue(group, s => s.TiebreakValues.GetValueOrDefault(tb, 0.0))
            };

            return parts.SelectMany(p => OrderGroup(p, tbIndex + 1)).ToList();
        }

        for (int i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }

        return ranked;
    }

    private static Dictionary<string, PlayerStats> BuildStats(Tournament tournament)
    {
        var stats = tournament.Players.ToDictionary(p => p.Id, p => new PlayerStats { Player = p });

        foreach (var round in tournament.Rounds.OrderBy(r => r.RoundNumber))
        {
            bool roundTookPlace = round.Pairings.Any(p => p.Result != MatchResult.NotPlayed);
            var onBoard = new HashSet<string>();

            foreach (var pairing in round.Pairings)
            {
                if (!string.IsNullOrEmpty(pairing.WhitePlayerId)) onBoard.Add(pairing.WhitePlayerId);
                if (!string.IsNullOrEmpty(pairing.BlackPlayerId)) onBoard.Add(pairing.BlackPlayerId);

                if (pairing.Result == MatchResult.NotPlayed) continue;

                var (wPts, bPts) = pairing.Result.ToPoints();

                if (!string.IsNullOrEmpty(pairing.WhitePlayerId) && !string.IsNullOrEmpty(pairing.BlackPlayerId))
                {
                    if (stats.TryGetValue(pairing.WhitePlayerId, out var wStats))
                    {
                        AddEntry(wStats, new RoundEntry
                        {
                            RoundNumber = round.RoundNumber,
                            Kind = GameKind(pairing.Result, isWhite: true),
                            Points = wPts,
                            OpponentId = pairing.BlackPlayerId,
                            IsBlack = false
                        });
                    }

                    if (stats.TryGetValue(pairing.BlackPlayerId, out var bStats))
                    {
                        AddEntry(bStats, new RoundEntry
                        {
                            RoundNumber = round.RoundNumber,
                            Kind = GameKind(pairing.Result, isWhite: false),
                            Points = bPts,
                            OpponentId = pairing.WhitePlayerId,
                            IsBlack = true
                        });
                    }
                }
                else if (pairing.IsBye)
                {
                    string? byePlayerId = pairing.WhitePlayerId ?? pairing.BlackPlayerId;
                    if (!string.IsNullOrEmpty(byePlayerId) && stats.TryGetValue(byePlayerId, out var byeStats))
                    {
                        AddEntry(byeStats, new RoundEntry
                        {
                            RoundNumber = round.RoundNumber,
                            Kind = pairing.Result switch
                            {
                                MatchResult.ByeFull => EntryKind.PairingBye,
                                MatchResult.ByeFullRequested => EntryKind.FullBye,
                                _ => EntryKind.RequestedBye
                            },
                            Points = pairing.Result.ByePoints()
                        });
                    }
                }
            }

            if (!roundTookPlace) continue;

            foreach (var s in stats.Values.Where(s => !onBoard.Contains(s.Player.Id)))
            {
                AddEntry(s, new RoundEntry { RoundNumber = round.RoundNumber, Kind = EntryKind.Absent, Points = 0.0 });
            }
        }

        return stats;
    }

    private static void AddEntry(PlayerStats stats, RoundEntry entry)
    {
        stats.Entries.Add(entry);
        stats.Points += entry.Points;
    }

    private static EntryKind GameKind(MatchResult result, bool isWhite) => result switch
    {
        MatchResult.WhiteForfeit => isWhite ? EntryKind.ForfeitWin : EntryKind.ForfeitLoss,
        MatchResult.BlackForfeit => isWhite ? EntryKind.ForfeitLoss : EntryKind.ForfeitWin,
        MatchResult.DoubleForfeit => EntryKind.ForfeitLoss,
        _ => EntryKind.Played
    };

    /// <summary>
    /// C.07 16.3: requested byes (and absences) not followed by any round in which the player was
    /// available to play (16.2.5) are evaluated as draws; all other rounds keep their awarded result.
    /// </summary>
    private static double ComputeAdjustedScore(PlayerStats s)
    {
        double adjusted = s.Points;
        foreach (var entry in s.Entries.OrderByDescending(e => e.RoundNumber))
        {
            if (entry.Kind is not (EntryKind.RequestedBye or EntryKind.Absent)) break;
            adjusted += 0.5 - entry.Points;
        }
        return adjusted;
    }

    /// <summary>
    /// Sum after cutting the <paramref name="cutLowest"/> least significant and <paramref name="cutHighest"/>
    /// most significant contributions. Per C.07 16.5 the lowest-cut slots are taken first by contributions of
    /// voluntarily unplayed rounds (forfeit losses, requested byes), lowest first.
    /// </summary>
    private static double SumAfterCuts(List<Contribution> contributions, int cutLowest, int cutHighest)
    {
        var remaining = contributions.ToList();

        for (int i = 0; i < cutLowest && remaining.Count > 0; i++)
        {
            var voluntary = remaining.Where(c => c.Voluntary).OrderBy(c => c.Value).FirstOrDefault();
            remaining.Remove(voluntary ?? remaining.OrderBy(c => c.Value).First());
        }

        for (int i = 0; i < cutHighest && remaining.Count > 0; i++)
        {
            remaining.Remove(remaining.OrderByDescending(c => c.Value).First());
        }

        return remaining.Sum(c => c.Value);
    }

    private static List<List<PlayerStanding>> PartitionByValue(List<PlayerStanding> group, Func<PlayerStanding, double> key) =>
        group
            .GroupBy(s => Math.Round(key(s), 6))
            .OrderByDescending(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

    private static List<List<PlayerStanding>> PartitionByComparer(List<PlayerStanding> group, Comparison<PlayerStanding> comparison)
    {
        var sorted = group.ToList();
        sorted.Sort(comparison);

        var parts = new List<List<PlayerStanding>>();
        foreach (var s in sorted)
        {
            if (parts.Count > 0 && comparison(parts[^1][0], s) == 0) parts[^1].Add(s);
            else parts.Add([s]);
        }
        return parts;
    }

    /// <summary>
    /// C.07 Article 6. Scores in the mini-tournament among the tied group (repeated encounters averaged).
    /// 6.2: if everyone met everyone, rank by that score and re-apply to any sub-group still tied.
    /// 6.3: otherwise a player who leads whatever the missing results is ranked first and Article 6 is
    /// re-applied to the rest; if nobody is guaranteed to lead, the group stays tied for the next tie-break.
    /// </summary>
    private static List<List<PlayerStanding>> PartitionByDirectEncounter(
        List<PlayerStanding> group,
        Dictionary<string, PlayerStats> stats,
        Func<RoundEntry, bool> countsAsGame)
    {
        if (group.Count <= 1) return [group];

        var ids = group.Select(s => s.PlayerId).ToHashSet();
        var score = new Dictionary<string, double>();
        var unmet = new Dictionary<string, int>();

        foreach (var s in group)
        {
            var vsGroup = stats[s.PlayerId].Entries
                .Where(e => countsAsGame(e) && ids.Contains(e.OpponentId!))
                .GroupBy(e => e.OpponentId!)
                .ToList();

            score[s.PlayerId] = vsGroup.Sum(g => g.Average(e => e.Points));
            unmet[s.PlayerId] = group.Count - 1 - vsGroup.Count;
        }

        if (unmet.Values.All(u => u == 0))
        {
            var parts = PartitionByValue(group, s => score[s.PlayerId]);
            if (parts.Count == 1) return parts; // DE cannot separate them

            return parts
                .SelectMany(p => p.Count > 1 ? PartitionByDirectEncounter(p, stats, countsAsGame) : [p])
                .ToList();
        }

        var leader = group
            .Where(a => group.All(b => b == a || score[a.PlayerId] > score[b.PlayerId] + unmet[b.PlayerId]))
            .FirstOrDefault();

        if (leader == null) return [group];

        var rest = group.Where(s => s != leader).ToList();
        return [[leader], .. PartitionByDirectEncounter(rest, stats, countsAsGame)];
    }

    private static int CompareExtendedKoya(string playerAId, string playerBId, Dictionary<string, PlayerStats> stats, double maxScore)
    {
        if (!stats.TryGetValue(playerAId, out var statsA) || !stats.TryGetValue(playerBId, out var statsB))
            return 0;

        // Step down from 50% threshold in steps of 0.5 points
        for (double t = maxScore * 0.5; t >= 0; t -= 0.5)
        {
            double scoreA = statsA.Entries
                .Where(e => e.Kind == EntryKind.Played && stats.TryGetValue(e.OpponentId!, out var opp) && opp.Points >= t)
                .Sum(e => e.Points);

            double scoreB = statsB.Entries
                .Where(e => e.Kind == EntryKind.Played && stats.TryGetValue(e.OpponentId!, out var opp) && opp.Points >= t)
                .Sum(e => e.Points);

            int comp = scoreB.CompareTo(scoreA); // descending
            if (comp != 0) return comp;
        }

        return 0;
    }
}
