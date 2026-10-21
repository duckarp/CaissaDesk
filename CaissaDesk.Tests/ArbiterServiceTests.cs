using CaissaDesk.Core.Arbiter;
using CaissaDesk.Core.PairingEngine;
using Caissa.Shared.Models;
using Xunit;

namespace CaissaDesk.Tests;

public class ArbiterServiceTests
{
    private static Tournament CreateTestTournament()
    {
        var tournament = new Tournament
        {
            Id = "t1",
            Name = "Open Rapid",
            TotalRounds = 5,
            System = TournamentSystem.SwissDutch,
            Players =
            [
                new Player { Id = "p1", StartRank = 1, FullName = "GM Carlsen", FideRating = 2850 },
                new Player { Id = "p2", StartRank = 2, FullName = "GM Nakamura", FideRating = 2800 },
                new Player { Id = "p3", StartRank = 3, FullName = "GM Caruana", FideRating = 2790 },
                new Player { Id = "p4", StartRank = 4, FullName = "GM Firouzja", FideRating = 2780 }
            ]
        };
        return tournament;
    }

    [Fact]
    public void SetAdvanceBye_AssignsAndRemovesByesCorrectly()
    {
        var service = new ArbiterService();
        var t = CreateTestTournament();

        service.SetAdvanceBye(t, "p1", 2, ByeType.HalfPoint);
        Assert.True(t.Players[0].ByeRequests.ContainsKey(2));
        Assert.Equal(ByeType.HalfPoint, t.Players[0].ByeRequests[2]);

        service.SetAdvanceBye(t, "p1", 2, ByeType.None);
        Assert.False(t.Players[0].ByeRequests.ContainsKey(2));
    }

    [Fact]
    public void WithdrawAndReactivate_UpdatesStatusAndClearsFutureByes()
    {
        var service = new ArbiterService();
        var t = CreateTestTournament();
        service.SetAdvanceBye(t, "p1", 3, ByeType.HalfPoint);

        service.WithdrawPlayer(t, "p1", "Illness");
        Assert.Equal(PlayerStatus.Withdrawn, t.Players[0].Status);
        Assert.False(t.Players[0].ByeRequests.ContainsKey(3));

        service.ReactivatePlayer(t, "p1");
        Assert.Equal(PlayerStatus.Active, t.Players[0].Status);
    }

    [Fact]
    public void AddLatePlayer_AssignsSeedAndMissedRoundsByes()
    {
        var service = new ArbiterService();
        var t = CreateTestTournament();

        // Round 1 already generated
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p3", Result = MatchResult.WhiteWin },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p2", BlackPlayerId = "p4", Result = MatchResult.Draw }
            ]
        });

        var latePlayer = new Player { Id = "p5", FullName = "GM So", FideRating = 2770 };
        service.AddLatePlayer(t, latePlayer, ByeType.HalfPoint);

        Assert.Equal(5, t.Players.Count);
        Assert.Equal(5, latePlayer.StartRank);
        Assert.Equal(PlayerStatus.Active, latePlayer.Status);

        // Check that Round 1 got a ½-point bye pairing for p5
        var byePairing = t.Rounds[0].Pairings.FirstOrDefault(p => p.WhitePlayerId == "p5");
        Assert.NotNull(byePairing);
        Assert.Equal(MatchResult.ByeHalf, byePairing.Result);
        Assert.Equal(3, byePairing.BoardNumber);
    }

    [Fact]
    public void SwapBoardColors_SwapsWhiteAndBlack()
    {
        var service = new ArbiterService();
        var round = new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2" }
            ]
        };

        bool ok = service.SwapBoardColors(round, 1);
        Assert.True(ok);
        Assert.Equal("p2", round.Pairings[0].WhitePlayerId);
        Assert.Equal("p1", round.Pairings[0].BlackPlayerId);
        Assert.True(round.Pairings[0].IsManualOverride);
    }

    [Fact]
    public void SwapPlayersAcrossBoards_SwapsCorrectly()
    {
        var service = new ArbiterService();
        var round = new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2" },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p3", BlackPlayerId = "p4" }
            ]
        };

        // Swap White on Board 1 (p1) with White on Board 2 (p3)
        bool ok = service.SwapPlayersAcrossBoards(round, 1, isWhite1: true, 2, isWhite2: true);
        Assert.True(ok);
        Assert.Equal("p3", round.Pairings[0].WhitePlayerId);
        Assert.Equal("p1", round.Pairings[1].WhitePlayerId);
    }

    [Fact]
    public void NativeDutch_WithAdvanceHalfPointBye_PairsRemainingAndAssignsBye()
    {
        var t = CreateTestTournament();
        var engine = new NativeDutchPairingEngine();

        // GM Carlsen requests half point bye for round 1
        t.Players[0].ByeRequests[1] = ByeType.HalfPoint;

        var result = engine.GenerateRoundPairings(t, 1);
        Assert.True(result.Success);

        // Remaining 3 players: 2 will be paired on Board 1, 1 gets 1-point odd bye on Board 2
        // Carlsen gets requested ½ bye on Board 3
        Assert.Equal(3, result.Pairings.Count);

        var carlsenBye = result.Pairings.FirstOrDefault(p => p.WhitePlayerId == "p1");
        Assert.NotNull(carlsenBye);
        Assert.Equal(MatchResult.ByeHalf, carlsenBye.Result);
        Assert.True(carlsenBye.IsBye);

        // Neither p2, p3, nor p4 should be playing Carlsen
        var board1 = result.Pairings.First(p => p.BoardNumber == 1);
        Assert.NotEqual("p1", board1.WhitePlayerId);
        Assert.NotEqual("p1", board1.BlackPlayerId);
    }

    [Fact]
    public void NativeDutch_WithdrawnPlayer_IsExcludedFromPairings()
    {
        var t = CreateTestTournament();
        var engine = new NativeDutchPairingEngine();

        // Withdraw p4
        t.Players[3].Status = PlayerStatus.Withdrawn;

        var result = engine.GenerateRoundPairings(t, 1);
        Assert.True(result.Success);

        // 3 active players: 1 board of 2 players, 1 pairing bye
        Assert.DoesNotContain(result.Pairings, p => p.WhitePlayerId == "p4" || p.BlackPlayerId == "p4");
        Assert.Equal(2, result.Pairings.Count);
    }

    [Fact]
    public void SwapBoardColors_FlipsAnEnteredResult()
    {
        var service = new ArbiterService();
        var round = new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.WhiteWin },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p3", BlackPlayerId = "p4", Result = MatchResult.BlackForfeit }
            ]
        };

        Assert.True(service.SwapBoardColors(round, 1));
        Assert.True(service.SwapBoardColors(round, 2));

        // p1 still won, now as Black; p4 still won by forfeit, now as White
        Assert.Equal(MatchResult.BlackWin, round.Pairings[0].Result);
        Assert.Equal(MatchResult.WhiteForfeit, round.Pairings[1].Result);
    }

    [Fact]
    public void SwapPlayersAcrossBoards_RefusesToMoveIntoEmptyByeSlot()
    {
        var service = new ArbiterService();
        var round = new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2" },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p3", BlackPlayerId = null, Result = MatchResult.ByeFull }
            ]
        };

        bool ok = service.SwapPlayersAcrossBoards(round, 1, true, 2, false);

        Assert.False(ok);
        Assert.Equal("p1", round.Pairings[0].WhitePlayerId);
        Assert.Null(round.Pairings[1].BlackPlayerId);
    }

    [Fact]
    public void AddLatePlayer_WithStartRankGaps_UsesMaxPlusOne()
    {
        var service = new ArbiterService();
        var t = CreateTestTournament();
        t.Players.RemoveAt(1); // ranks now 1, 3, 4

        var late = new Player { Id = "late", FullName = "Late" };
        service.AddLatePlayer(t, late, ByeType.None);

        Assert.Equal(5, late.StartRank);
        Assert.Equal(t.Players.Count, t.Players.Select(p => p.StartRank).Distinct().Count());
    }

    [Fact]
    public void AddLatePlayer_FullPointByes_AreRecordedAsRequestedNotPairingByes()
    {
        var service = new ArbiterService();
        var t = CreateTestTournament();
        t.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings = [new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.Draw }]
        });

        var late = new Player { Id = "late", FullName = "Late" };
        service.AddLatePlayer(t, late, ByeType.FullPoint);

        var bye = t.Rounds[0].Pairings.Single(p => p.WhitePlayerId == "late");
        Assert.Equal(MatchResult.ByeFullRequested, bye.Result);
    }
}
