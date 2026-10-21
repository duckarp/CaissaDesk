using CaissaDesk.Core.PairingEngine;
using Caissa.Shared.Models;

namespace CaissaDesk.Tests;

public class BergerPairingEngineTests
{
    [Fact]
    public void Berger_6Players_MatchesOfficialFideTablesExactly()
    {
        // Round 1
        var r1 = BergerPairingEngine.GenerateFideBergerRound(6, 1);
        Assert.Equal([(1, 6), (2, 5), (3, 4)], r1);

        // Round 2
        var r2 = BergerPairingEngine.GenerateFideBergerRound(6, 2);
        Assert.Equal([(6, 4), (1, 2), (5, 3)], r2);

        // Round 3
        var r3 = BergerPairingEngine.GenerateFideBergerRound(6, 3);
        Assert.Equal([(2, 6), (3, 1), (4, 5)], r3);

        // Round 4
        var r4 = BergerPairingEngine.GenerateFideBergerRound(6, 4);
        Assert.Equal([(6, 5), (1, 4), (2, 3)], r4);

        // Round 5
        var r5 = BergerPairingEngine.GenerateFideBergerRound(6, 5);
        Assert.Equal([(3, 6), (5, 1), (4, 2)], r5);
    }

    [Fact]
    public void Berger_GeneratesAllRoundsForTournament()
    {
        var engine = new BergerPairingEngine();
        var tournament = new Tournament
        {
            Name = "Round Robin Test",
            System = TournamentSystem.RoundRobin,
            Players =
            [
                new Player { StartRank = 1, FullName = "P1" },
                new Player { StartRank = 2, FullName = "P2" },
                new Player { StartRank = 3, FullName = "P3" },
                new Player { StartRank = 4, FullName = "P4" }
            ]
        };

        var rounds = engine.GenerateAllRounds(tournament);
        Assert.Equal(3, rounds.Count); // 4 players = 3 rounds

        foreach (var r in rounds)
        {
            Assert.Equal(2, r.Pairings.Count); // 2 boards per round
        }

        // Verify each pair of players plays exactly once
        var allPlayedPairs = new HashSet<string>();
        foreach (var r in rounds)
        {
            foreach (var p in r.Pairings)
            {
                var p1 = tournament.GetPlayer(p.WhitePlayerId)!.StartRank;
                var p2 = tournament.GetPlayer(p.BlackPlayerId)!.StartRank;
                string key = $"{Math.Min(p1, p2)}-{Math.Max(p1, p2)}";
                Assert.DoesNotContain(key, allPlayedPairs);
                allPlayedPairs.Add(key);
            }
        }

        // With 4 players, there are 4*3/2 = 6 unique games
        Assert.Equal(6, allPlayedPairs.Count);
    }

    [Fact]
    public void Berger_OddPlayers_AssignsOneByePerPlayer()
    {
        var engine = new BergerPairingEngine();
        var tournament = new Tournament
        {
            Name = "5 Players Round Robin",
            System = TournamentSystem.RoundRobin,
            Players =
            [
                new Player { StartRank = 1, FullName = "P1" },
                new Player { StartRank = 2, FullName = "P2" },
                new Player { StartRank = 3, FullName = "P3" },
                new Player { StartRank = 4, FullName = "P4" },
                new Player { StartRank = 5, FullName = "P5" }
            ]
        };

        var rounds = engine.GenerateAllRounds(tournament);
        Assert.Equal(5, rounds.Count); // 5 players = 5 rounds

        var byePlayers = new HashSet<string>();
        foreach (var r in rounds)
        {
            var bye = r.Pairings.FirstOrDefault(p => p.IsBye);
            Assert.NotNull(bye);
            Assert.Equal(MatchResult.ByeFull, bye.Result);

            string? byePlayerId = bye.WhitePlayerId ?? bye.BlackPlayerId;
            Assert.NotNull(byePlayerId);
            byePlayers.Add(byePlayerId);
        }

        // All 5 players must have received exactly one bye
        Assert.Equal(5, byePlayers.Count);
    }
}
