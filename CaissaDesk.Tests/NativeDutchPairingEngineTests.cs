using CaissaDesk.Core.PairingEngine;
using Caissa.Shared.Models;

namespace CaissaDesk.Tests;

public class NativeDutchPairingEngineTests
{
    [Fact]
    public void Round1_PairsTopHalfAgainstBottomHalf_WithAlternatingColors()
    {
        var engine = new NativeDutchPairingEngine();
        var tournament = new Tournament
        {
            Name = "Swiss Round 1 Test",
            System = TournamentSystem.SwissDutch,
            Players =
            [
                new Player { StartRank = 1, FullName = "P1" },
                new Player { StartRank = 2, FullName = "P2" },
                new Player { StartRank = 3, FullName = "P3" },
                new Player { StartRank = 4, FullName = "P4" },
                new Player { StartRank = 5, FullName = "P5" },
                new Player { StartRank = 6, FullName = "P6" }
            ]
        };

        var result = engine.GenerateRoundPairings(tournament, 1);
        Assert.True(result.Success);
        Assert.Equal(3, result.Pairings.Count);

        // Board 1: P1 White vs P4 Black
        var b1 = result.Pairings[0];
        Assert.Equal(1, tournament.GetPlayer(b1.WhitePlayerId)!.StartRank);
        Assert.Equal(4, tournament.GetPlayer(b1.BlackPlayerId)!.StartRank);

        // Board 2: P5 White vs P2 Black (color alternation for top half!)
        var b2 = result.Pairings[1];
        Assert.Equal(5, tournament.GetPlayer(b2.WhitePlayerId)!.StartRank);
        Assert.Equal(2, tournament.GetPlayer(b2.BlackPlayerId)!.StartRank);

        // Board 3: P3 White vs P6 Black
        var b3 = result.Pairings[2];
        Assert.Equal(3, tournament.GetPlayer(b3.WhitePlayerId)!.StartRank);
        Assert.Equal(6, tournament.GetPlayer(b3.BlackPlayerId)!.StartRank);
    }

    [Fact]
    public void Round1_OddPlayerCount_GivesLowestSeedTheBye()
    {
        var engine = new NativeDutchPairingEngine();
        var tournament = new Tournament
        {
            Name = "Odd Swiss Test",
            System = TournamentSystem.SwissDutch,
            Players =
            [
                new Player { StartRank = 1, FullName = "P1" },
                new Player { StartRank = 2, FullName = "P2" },
                new Player { StartRank = 3, FullName = "P3" },
                new Player { StartRank = 4, FullName = "P4" },
                new Player { StartRank = 5, FullName = "P5" }
            ]
        };

        var result = engine.GenerateRoundPairings(tournament, 1);
        Assert.True(result.Success);
        Assert.Equal(3, result.Pairings.Count); // 2 normal boards + 1 bye board

        var byeBoard = result.Pairings.FirstOrDefault(p => p.IsBye);
        Assert.NotNull(byeBoard);
        Assert.Equal(MatchResult.ByeFull, byeBoard.Result);

        var byePlayer = tournament.GetPlayer(byeBoard.WhitePlayerId);
        Assert.NotNull(byePlayer);
        Assert.Equal(5, byePlayer.StartRank); // Lowest seeded player received the bye
    }

    [Fact]
    public void Round2_PreventsRematches_AndBalancesColors()
    {
        var engine = new NativeDutchPairingEngine();
        var tournament = new Tournament
        {
            Name = "Round 2 Swiss Test",
            System = TournamentSystem.SwissDutch,
            Players =
            [
                new Player { StartRank = 1, FullName = "P1" },
                new Player { StartRank = 2, FullName = "P2" },
                new Player { StartRank = 3, FullName = "P3" },
                new Player { StartRank = 4, FullName = "P4" }
            ]
        };

        // Round 1 pairings & results:
        // P1 beat P3 (P1: W=1, P3: B=0)
        // P4 beat P2 (P4: W=1, P2: B=0)
        var p1 = tournament.Players[0];
        var p2 = tournament.Players[1];
        var p3 = tournament.Players[2];
        var p4 = tournament.Players[3];

        tournament.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { WhitePlayerId = p1.Id, BlackPlayerId = p3.Id, Result = MatchResult.WhiteWin },
                new Pairing { WhitePlayerId = p4.Id, BlackPlayerId = p2.Id, Result = MatchResult.WhiteWin }
            ]
        });

        // Generate Round 2:
        // Winners bracket: P1 (1 pt, had W) and P4 (1 pt, had W) must play each other!
        // Losers bracket: P2 (0 pts, had B) and P3 (0 pts, had B) must play each other!
        var r2Result = engine.GenerateRoundPairings(tournament, 2);
        Assert.True(r2Result.Success);
        Assert.Equal(2, r2Result.Pairings.Count);

        // Verify zero rematches
        foreach (var pairing in r2Result.Pairings)
        {
            var w = tournament.GetPlayer(pairing.WhitePlayerId)!;
            var b = tournament.GetPlayer(pairing.BlackPlayerId)!;

            // (P1, P3) and (P4, P2) cannot play again!
            Assert.False(w.StartRank == 1 && b.StartRank == 3);
            Assert.False(w.StartRank == 3 && b.StartRank == 1);
            Assert.False(w.StartRank == 4 && b.StartRank == 2);
            Assert.False(w.StartRank == 2 && b.StartRank == 4);
        }
    }

    [Fact]
    public void GenerateNextRound_FailsIfPrecedingRoundHasUnfinishedGames()
    {
        var engine = new NativeDutchPairingEngine();
        var tournament = new Tournament
        {
            Name = "Incomplete Round Test",
            System = TournamentSystem.SwissDutch,
            Players =
            [
                new Player { StartRank = 1, FullName = "P1" },
                new Player { StartRank = 2, FullName = "P2" },
                new Player { StartRank = 3, FullName = "P3" },
                new Player { StartRank = 4, FullName = "P4" }
            ],
            Rounds =
            [
                new Round
                {
                    RoundNumber = 1,
                    Pairings =
                    [
                        new Pairing { WhitePlayerId = "p1", BlackPlayerId = "p3", Result = MatchResult.WhiteWin },
                        new Pairing { WhitePlayerId = "p2", BlackPlayerId = "p4", Result = MatchResult.NotPlayed }
                    ]
                }
            ]
        };

        var result = engine.GenerateRoundPairings(tournament, 2);
        Assert.False(result.Success);
        Assert.Contains("still has 1 game(s) with results pending", result.ErrorMessage);
    }
}
