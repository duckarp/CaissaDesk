using CaissaDesk.Core.PairingEngine;
using Caissa.Shared.Models;
using Xunit;

namespace CaissaDesk.Tests;

public class BbpPairingEngineTests
{
    [Fact]
    public void TestEngine_BbpPairings_SucceedsIfInstalled()
    {
        var registry = new PairingEngineRegistry();
        var bbp = registry.AllEngines.FirstOrDefault(e => e.Id == "bbp");

        if (bbp == null || !bbp.IsAvailable)
        {
            // If binary not found in current environment, skip
            return;
        }

        var result = registry.TestEngine("bbp");
        Assert.True(result.Success, $"bbpPairings TestEngine failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.Pairings);
        Assert.Equal(2, result.Pairings.Count);
    }

    // ---- bbpPairings as an oracle (skipped when the binary is not installed) ----

    private static BbpPairingEngine? InstalledBbp()
    {
        var bbp = new BbpPairingEngine();
        return bbp.IsAvailable ? bbp : null;
    }

    private static Tournament Field(int n) => new()
    {
        System = TournamentSystem.SwissDutch,
        TotalRounds = 7,
        Players = Enumerable.Range(1, n)
            .Select(r => new Player { Id = $"p{r}", StartRank = r, FullName = $"Player {r}", FideRating = 2500 - 10 * r })
            .ToList()
    };

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(11)]
    public void Round1_NativeMatchesBbp(int players)
    {
        var bbp = InstalledBbp();
        if (bbp == null) return;

        var t = Field(players);
        var expected = bbp.GenerateRoundPairings(t, 1);
        var actual = new NativeDutchPairingEngine().GenerateRoundPairings(t, 1);

        Assert.True(expected.Success, expected.ErrorMessage);
        Assert.True(actual.Success, actual.ErrorMessage);
        Assert.Equal(
            expected.Pairings.Select(p => (p.WhitePlayerId, p.BlackPlayerId, p.Result)).OrderBy(x => x.WhitePlayerId),
            actual.Pairings.Select(p => (p.WhitePlayerId, p.BlackPlayerId, p.Result)).OrderBy(x => x.WhitePlayerId));
    }

    [Fact]
    public void Bbp_SkipsWithdrawnPlayersAndHonoursRequestedByes()
    {
        var bbp = InstalledBbp();
        if (bbp == null) return;

        var t = Field(6);
        var r1 = bbp.GenerateRoundPairings(t, 1);
        Assert.True(r1.Success, r1.ErrorMessage);
        foreach (var p in r1.Pairings) p.Result = MatchResult.WhiteWin;
        t.Rounds.Add(new Round { RoundNumber = 1, Pairings = r1.Pairings });

        t.Players[5].Status = PlayerStatus.Withdrawn;    // p6
        t.Players[0].ByeRequests[2] = ByeType.HalfPoint; // p1

        var r2 = bbp.GenerateRoundPairings(t, 2);

        Assert.True(r2.Success, r2.ErrorMessage);
        Assert.DoesNotContain(r2.Pairings, p => p.WhitePlayerId == "p6" || p.BlackPlayerId == "p6");
        var p1 = r2.Pairings.Single(p => p.WhitePlayerId == "p1" || p.BlackPlayerId == "p1");
        Assert.Equal(MatchResult.ByeHalf, p1.Result);
        // p2..p5 paired, nobody needs a pairing-allocated bye
        Assert.Equal(2, r2.Pairings.Count(p => !p.IsBye));
    }

    [Fact]
    public void PairingByeChoice_NativeAgreesWithBbp_LowestScoreGroup()
    {
        var bbp = InstalledBbp();
        if (bbp == null) return;

        var t = Field(5);
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.Draw },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p3", BlackPlayerId = "p4", Result = MatchResult.BlackWin },
                new Pairing { BoardNumber = 3, WhitePlayerId = "p5", Result = MatchResult.ByeFull }
            ]
        });

        var expected = bbp.GenerateRoundPairings(t, 2);
        var actual = new NativeDutchPairingEngine().GenerateRoundPairings(t, 2);

        Assert.True(expected.Success, expected.ErrorMessage);
        Assert.True(actual.Success, actual.ErrorMessage);
        Assert.Equal(
            expected.Pairings.Single(p => p.IsBye).WhitePlayerId,
            actual.Pairings.Single(p => p.IsBye).WhitePlayerId);
    }

    /// <summary>
    /// Plays a whole event through bbpPairings with withdrawals, late byes and forfeits. bbpPairings parses
    /// every TRF we generate (it rejects malformed files) and every round must satisfy the absolute criteria.
    /// </summary>
    [Fact]
    public void FullEvent_ThroughBbp_ProducesValidPairingsEveryRound()
    {
        var bbp = InstalledBbp();
        if (bbp == null) return;

        var t = Field(13);
        t.TotalRounds = 7;
        var rng = new Random(20261021);
        var met = new HashSet<(string, string)>();
        var hadPab = new HashSet<string>();

        for (int round = 1; round <= t.TotalRounds; round++)
        {
            if (round == 3) t.Players[12].Status = PlayerStatus.Withdrawn;
            if (round == 4) t.Players[3].ByeRequests[4] = ByeType.HalfPoint;
            if (round == 5) t.Players[7].ByeRequests[5] = ByeType.ZeroPoint;
            if (round == 6) t.Players[12].Status = PlayerStatus.Active; // returns after missing rounds

            var result = bbp.GenerateRoundPairings(t, round);
            Assert.True(result.Success, $"Round {round}: {result.ErrorMessage}");

            var available = t.Players
                .Where(p => p.Status == PlayerStatus.Active)
                .Select(p => p.Id)
                .ToHashSet();
            var seen = result.Pairings.SelectMany(p => new[] { p.WhitePlayerId, p.BlackPlayerId }).OfType<string>().ToList();
            Assert.Equal(seen.Count, seen.Distinct().Count());
            Assert.True(available.SetEquals(seen), $"Round {round}: every active player must appear exactly once");

            foreach (var p in result.Pairings)
            {
                if (p.IsBye)
                {
                    if (p.Result == MatchResult.ByeFull)
                    {
                        Assert.True(hadPab.Add(p.WhitePlayerId!), $"Round {round}: second pairing bye for {p.WhitePlayerId}");
                    }
                    continue;
                }

                var key = string.CompareOrdinal(p.WhitePlayerId, p.BlackPlayerId) < 0
                    ? (p.WhitePlayerId!, p.BlackPlayerId!)
                    : (p.BlackPlayerId!, p.WhitePlayerId!);
                Assert.True(met.Add(key), $"Round {round}: rematch {key}");

                int roll = rng.Next(100);
                p.Result = roll switch
                {
                    < 40 => MatchResult.WhiteWin,
                    < 70 => MatchResult.BlackWin,
                    < 95 => MatchResult.Draw,
                    _ => MatchResult.WhiteForfeit
                };
                if (p.Result == MatchResult.WhiteForfeit)
                {
                    // Forfeited game: the pair may meet again, and the winner can no longer get the bye
                    met.Remove(key);
                    hadPab.Add(p.WhitePlayerId!);
                }
            }

            t.Rounds.Add(new Round { RoundNumber = round, Pairings = result.Pairings });
        }
    }
}
