using CaissaDesk.Core.PairingEngine;
using CaissaDesk.Core.Standings;

namespace CaissaDesk.Tests;

public class StandingsCacheTests
{
    /// <summary>
    /// Verbatim copy of the score loop the tournament workbench used to run on every render of the cross table
    /// (TournamentWorkbench.CalculateScores before the U5 refactor). Kept as the reference the cached
    /// <see cref="TiebreakService"/> output must reproduce.
    /// </summary>
    private static Dictionary<string, (double Points, int Games)> LegacyCalculateScores(Tournament tournament)
    {
        var dict = new Dictionary<string, (double Points, int Games)>();

        foreach (var p in tournament.Players)
        {
            dict[p.Id] = (0.0, 0);
        }

        foreach (var round in tournament.Rounds)
        {
            foreach (var pairing in round.Pairings)
            {
                if (pairing.Result == MatchResult.NotPlayed) continue;

                var (wPts, bPts) = pairing.Result.ToPoints();

                if (!string.IsNullOrEmpty(pairing.WhitePlayerId) && dict.ContainsKey(pairing.WhitePlayerId))
                {
                    var curr = dict[pairing.WhitePlayerId];
                    dict[pairing.WhitePlayerId] = (curr.Points + wPts, curr.Games + 1);
                }

                if (!string.IsNullOrEmpty(pairing.BlackPlayerId) && dict.ContainsKey(pairing.BlackPlayerId))
                {
                    var curr = dict[pairing.BlackPlayerId];
                    dict[pairing.BlackPlayerId] = (curr.Points + bPts, curr.Games + 1);
                }
            }
        }

        return dict;
    }

    private static readonly MatchResult[] ResultCycle =
    [
        MatchResult.WhiteWin, MatchResult.Draw, MatchResult.BlackWin, MatchResult.WhiteWin,
        MatchResult.WhiteForfeit, MatchResult.Draw, MatchResult.BlackForfeit, MatchResult.BlackWin,
        MatchResult.DoubleForfeit, MatchResult.Draw
    ];

    private static List<Player> SamplePlayers(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new Player
            {
                Id = $"p{i}",
                StartRank = i,
                FullName = $"Player {i}",
                FideRating = 2600 - i * 25,
                NationalRating = 2550 - i * 20,
                Federation = "SVK"
            })
            .ToList();

    /// <summary>Round robin with an odd field (free rounds) and every kind of result, last round partly unplayed.</summary>
    private static Tournament SampleRoundRobin(int players = 7)
    {
        var t = new Tournament { System = TournamentSystem.RoundRobin, Players = SamplePlayers(players) };
        t.TiebreakOrder = TiebreakPreset.RoundRobin.GetDefaultList();
        t.Rounds = new BergerPairingEngine().GenerateAllRounds(t);
        t.TotalRounds = t.Rounds.Count;

        int k = 0;
        foreach (var round in t.Rounds)
        {
            foreach (var pairing in round.Pairings.Where(p => !p.IsBye))
            {
                // Leave the last round half-finished so NotPlayed games are covered too
                if (round.RoundNumber == t.Rounds.Count && pairing.BoardNumber % 2 == 0) continue;
                pairing.Result = ResultCycle[k++ % ResultCycle.Length];
            }
        }

        return t;
    }

    /// <summary>Swiss tournament paired by the native engine round by round, including a pairing-allocated bye.</summary>
    private static async Task<Tournament> SampleSwissAsync()
    {
        var t = new Tournament
        {
            System = TournamentSystem.SwissDutch,
            TotalRounds = 5,
            Players = SamplePlayers(9),
            TiebreakOrder = TiebreakPreset.FideStandardSwiss.GetDefaultList()
        };

        var engine = new NativeDutchPairingEngine();
        int k = 3;
        for (int r = 1; r <= 4; r++)
        {
            var result = await engine.GenerateRoundPairingsAsync(t, r);
            Assert.True(result.Success, result.ErrorMessage);
            foreach (var pairing in result.Pairings.Where(p => !p.IsBye))
            {
                pairing.Result = ResultCycle[k++ % ResultCycle.Length];
            }
            t.Rounds.Add(new Round { RoundNumber = r, Pairings = result.Pairings });
        }

        // Requested half-point bye and a zero-point bye entered by the arbiter
        t.Rounds[^1].Pairings.Add(new Pairing { RoundNumber = 4, BoardNumber = 90, WhitePlayerId = "p9", Result = MatchResult.ByeHalf });
        return t;
    }

    private static void AssertPointsMatchLegacy(Tournament t)
    {
        var legacy = LegacyCalculateScores(t);
        var cache = new StandingsCache(new TiebreakService());

        foreach (var p in t.Players)
        {
            Assert.Equal(legacy[p.Id].Points, cache.GetPoints(t, p.Id), precision: 6);
        }
    }

    [Fact]
    public void RoundRobin_CachedPoints_MatchLegacyCalculateScores()
    {
        AssertPointsMatchLegacy(SampleRoundRobin(7));
        AssertPointsMatchLegacy(SampleRoundRobin(6));
    }

    [Fact]
    public async Task Swiss_CachedPoints_MatchLegacyCalculateScores()
    {
        AssertPointsMatchLegacy(await SampleSwissAsync());
    }

    [Fact]
    public async Task CachedStandings_AreIdenticalToDirectTiebreakServiceOutput()
    {
        foreach (var t in new[] { SampleRoundRobin(), await SampleSwissAsync() })
        {
            var direct = new TiebreakService().CalculateStandings(t);
            var cached = new StandingsCache(new TiebreakService()).GetStandings(t);

            Assert.Equal(direct.Count, cached.Count);
            for (int i = 0; i < direct.Count; i++)
            {
                Assert.Equal(direct[i].PlayerId, cached[i].PlayerId);
                Assert.Equal(direct[i].Rank, cached[i].Rank);
                Assert.Equal(direct[i].Points, cached[i].Points);
                Assert.Equal(direct[i].GamesPlayed, cached[i].GamesPlayed);
                foreach (var tb in t.TiebreakOrder)
                {
                    Assert.Equal(direct[i].FormatTiebreak(tb), cached[i].FormatTiebreak(tb));
                }
            }
        }
    }

    private sealed class CountingTiebreakService : ITiebreakService
    {
        private readonly TiebreakService _inner = new();
        public int Calls { get; private set; }

        public List<PlayerStanding> CalculateStandings(Tournament tournament, List<TiebreakType>? tiebreakOrder = null)
        {
            Calls++;
            return _inner.CalculateStandings(tournament, tiebreakOrder);
        }
    }

    [Fact]
    public void RepeatedReads_WithoutChanges_ComputeOnce()
    {
        var svc = new CountingTiebreakService();
        var cache = new StandingsCache(svc);
        var t = SampleRoundRobin();

        for (int i = 0; i < 5; i++)
        {
            cache.GetStandings(t);
            cache.GetPoints(t, "p1");
        }

        Assert.Equal(1, svc.Calls);
        Assert.Equal(1, cache.ComputeCount);
    }

    [Fact]
    public void ResultChange_TriggersRecompute()
    {
        var svc = new CountingTiebreakService();
        var cache = new StandingsCache(svc);
        var t = SampleRoundRobin();
        var game = t.Rounds[^1].Pairings.First(p => !p.IsBye && p.Result == MatchResult.NotPlayed);

        double before = cache.GetPoints(t, game.WhitePlayerId!);
        game.Result = MatchResult.WhiteWin;
        double after = cache.GetPoints(t, game.WhitePlayerId!);

        Assert.Equal(2, svc.Calls);
        Assert.Equal(before + 1.0, after);
    }

    [Fact]
    public void TiebreakOrderRatingAndRoundChanges_TriggerRecompute()
    {
        var svc = new CountingTiebreakService();
        var cache = new StandingsCache(svc);
        var t = SampleRoundRobin();
        cache.GetStandings(t);

        (t.TiebreakOrder[0], t.TiebreakOrder[1]) = (t.TiebreakOrder[1], t.TiebreakOrder[0]);
        cache.GetStandings(t);
        Assert.Equal(2, svc.Calls);

        t.Players[0].FideRating += 10;
        cache.GetStandings(t);
        Assert.Equal(3, svc.Calls);

        t.Rounds.RemoveAt(t.Rounds.Count - 1);
        cache.GetStandings(t);
        Assert.Equal(4, svc.Calls);
    }

    [Fact]
    public void ReplacedTournamentInstance_TriggersRecompute()
    {
        var svc = new CountingTiebreakService();
        var cache = new StandingsCache(svc);

        cache.GetStandings(SampleRoundRobin());
        cache.GetStandings(SampleRoundRobin());

        Assert.Equal(2, svc.Calls);
    }

    [Fact]
    public void Invalidate_ForcesRecompute()
    {
        var svc = new CountingTiebreakService();
        var cache = new StandingsCache(svc);
        var t = SampleRoundRobin();

        cache.GetStandings(t);
        cache.Invalidate();
        cache.GetStandings(t);

        Assert.Equal(2, svc.Calls);
    }

    [Fact]
    public void GetPoints_UnknownPlayer_IsZero()
    {
        var cache = new StandingsCache(new TiebreakService());
        Assert.Equal(0.0, cache.GetPoints(SampleRoundRobin(), "nobody"));
    }

    [Fact]
    public void CrossTable_HeadToHead_ReportsScoreFromEachSide()
    {
        var t = new Tournament { Players = SamplePlayers(3) };
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.WhiteWin },
                new Pairing { WhitePlayerId = "p3", BlackPlayerId = null, Result = MatchResult.ByeFull }
            ]
        });
        t.Rounds.Add(new Round
        {
            RoundNumber = 2,
            Pairings = [new Pairing { WhitePlayerId = "p3", BlackPlayerId = "p1", Result = MatchResult.Draw }]
        });
        t.Rounds.Add(new Round
        {
            RoundNumber = 3,
            Pairings = [new Pairing { WhitePlayerId = "p2", BlackPlayerId = "p3", Result = MatchResult.NotPlayed }]
        });

        Assert.Equal("1", CrossTable.GetHeadToHeadResult(t, "p1", "p2"));
        Assert.Equal("0", CrossTable.GetHeadToHeadResult(t, "p2", "p1"));
        Assert.Equal("½", CrossTable.GetHeadToHeadResult(t, "p1", "p3"));
        Assert.Equal("½", CrossTable.GetHeadToHeadResult(t, "p3", "p1"));
        Assert.Equal("-", CrossTable.GetHeadToHeadResult(t, "p2", "p3"));
        Assert.Equal("-", CrossTable.GetHeadToHeadResult(null, "p2", "p3"));
    }
}
