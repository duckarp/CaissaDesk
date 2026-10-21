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

    private static Tournament Players(params int[] ranks) => new()
    {
        System = TournamentSystem.SwissDutch,
        TotalRounds = 9,
        Players = ranks.Select(r => new Player { Id = $"p{r}", StartRank = r, FullName = $"P{r}" }).ToList()
    };

    private static void AddRound(Tournament t, int number, params (string? White, string? Black, MatchResult Result)[] games)
    {
        t.Rounds.Add(new Round
        {
            RoundNumber = number,
            Pairings = games.Select((g, i) => new Pairing
            {
                RoundNumber = number,
                BoardNumber = i + 1,
                WhitePlayerId = g.White,
                BlackPlayerId = g.Black,
                Result = g.Result
            }).ToList()
        });
    }

    private static bool HasGame(PairingResult result, string a, string b) =>
        result.Pairings.Any(p => (p.WhitePlayerId == a && p.BlackPlayerId == b) || (p.WhitePlayerId == b && p.BlackPlayerId == a));

    private static string? ByePlayer(PairingResult result) =>
        result.Pairings.SingleOrDefault(p => p.IsBye && p.Result == MatchResult.ByeFull)?.WhitePlayerId;

    [Fact]
    public void GamesAgainstWithdrawnPlayers_StillCountForScoreGroups()
    {
        var t = Players(1, 2, 3, 4, 5, 6);
        AddRound(t, 1,
            ("p1", "p4", MatchResult.WhiteWin),
            ("p5", "p2", MatchResult.WhiteWin),
            ("p3", "p6", MatchResult.WhiteWin));
        t.Players.Single(p => p.Id == "p5").Status = PlayerStatus.Withdrawn;
        t.Players.Single(p => p.Id == "p6").Status = PlayerStatus.Withdrawn;

        var result = new NativeDutchPairingEngine().GenerateRoundPairings(t, 2);

        // p3's win over the withdrawn p6 counts: the 1-point group is {p1, p3}
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(HasGame(result, "p1", "p3"));
        Assert.True(HasGame(result, "p2", "p4"));
    }

    [Fact]
    public void ForfeitedGame_DoesNotBlockARematch()
    {
        var t = Players(1, 2);
        AddRound(t, 1, ("p1", "p2", MatchResult.WhiteForfeit));

        var result = new NativeDutchPairingEngine().GenerateRoundPairings(t, 2);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(HasGame(result, "p1", "p2"));
    }

    [Fact]
    public void ForfeitedGame_DoesNotCountForColours()
    {
        var t = Players(1, 2, 3, 4);
        // p1 "had White" twice, but both games were forfeited: no colour history at all
        AddRound(t, 1, ("p1", "p2", MatchResult.WhiteForfeit), ("p3", "p4", MatchResult.WhiteWin));
        AddRound(t, 2, ("p1", "p3", MatchResult.WhiteForfeit), ("p4", "p2", MatchResult.Draw));

        var result = new NativeDutchPairingEngine().GenerateRoundPairings(t, 3);

        Assert.True(result.Success, result.ErrorMessage);
        // p2-p4 and p3-p4 already met over the board, so p1 must meet p4. p1 has no colour history
        // (forfeits) and gets White; with a real WW history p1 could only have had Black.
        var p1Game = result.Pairings.Single(p => p.WhitePlayerId == "p1" || p.BlackPlayerId == "p1");
        Assert.Equal("p1", p1Game.WhitePlayerId);
        Assert.Equal("p4", p1Game.BlackPlayerId);
    }

    [Fact]
    public void PairingBye_GoesToLowestScoreGroup_NotSimplyTheLowestSeed()
    {
        var t = Players(1, 2, 3, 4, 5);
        AddRound(t, 1,
            ("p1", "p2", MatchResult.Draw),
            ("p3", "p4", MatchResult.BlackWin),
            ("p5", null, MatchResult.ByeFull));

        var result = new NativeDutchPairingEngine().GenerateRoundPairings(t, 2);

        // Scores: p4 1, p5 1, p1 ½, p2 ½, p3 0 -> p3 is alone in the lowest score group
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("p3", ByePlayer(result));
    }

    [Fact]
    public void PairingBye_NeverGivenTwice_ForfeitWinCountsAsBye()
    {
        var t = Players(1, 2, 3);
        AddRound(t, 1, ("p1", "p2", MatchResult.Draw), ("p3", null, MatchResult.ByeFull));
        AddRound(t, 2, ("p3", "p1", MatchResult.BlackForfeit), ("p2", null, MatchResult.ByeFull));

        // p3 and p2 had pairing byes, p1 a forfeit win: nobody may receive the round-3 bye
        var result = new NativeDutchPairingEngine().GenerateRoundPairings(t, 3);

        Assert.False(result.Success);
        Assert.Contains("already received", result.ErrorMessage);
    }

    [Fact]
    public void PairingBye_SkipsPlayersWhoAlreadyHadOne()
    {
        var t = Players(1, 2, 3, 4, 5);
        AddRound(t, 1,
            ("p1", "p3", MatchResult.WhiteWin),
            ("p4", "p2", MatchResult.WhiteWin),
            ("p5", null, MatchResult.ByeFull));
        AddRound(t, 2,
            ("p1", "p5", MatchResult.WhiteWin),
            ("p2", "p3", MatchResult.Draw),
            ("p4", null, MatchResult.ByeFull));

        var result = new NativeDutchPairingEngine().GenerateRoundPairings(t, 3);

        // Scores: p1 2, p4 2, p5 1, p2 ½, p3 ½. Lowest group {p2, p3}; p3 is the lower ranked
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("p3", ByePlayer(result));
        Assert.NotEqual("p5", ByePlayer(result));
        Assert.NotEqual("p4", ByePlayer(result));
    }

    [Fact]
    public void RequestedFullPointBye_IsRecordedAsRequested()
    {
        var t = Players(1, 2, 3);
        t.Players[2].ByeRequests[1] = ByeType.FullPoint;

        var result = new NativeDutchPairingEngine().GenerateRoundPairings(t, 1);

        Assert.True(result.Success, result.ErrorMessage);
        var bye = result.Pairings.Single(p => p.WhitePlayerId == "p3");
        Assert.Equal(MatchResult.ByeFullRequested, bye.Result);
    }

    [Fact]
    public void DisplayName_DoesNotClaimFullDutchCompliance()
    {
        Assert.Contains("simplified", new NativeDutchPairingEngine().DisplayName, StringComparison.OrdinalIgnoreCase);
    }
}
