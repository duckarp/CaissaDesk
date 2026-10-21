using Caissa.Shared.Models;

namespace CaissaDesk.Tests;

public class TournamentModelTests
{
    [Fact]
    public void MatchResult_ToPoints_CalculatesCorrectly()
    {
        Assert.Equal((1.0, 0.0), MatchResult.WhiteWin.ToPoints());
        Assert.Equal((0.0, 1.0), MatchResult.BlackWin.ToPoints());
        Assert.Equal((0.5, 0.5), MatchResult.Draw.ToPoints());
        Assert.Equal((1.0, 0.0), MatchResult.WhiteForfeit.ToPoints());
        Assert.Equal((0.0, 1.0), MatchResult.BlackForfeit.ToPoints());
        Assert.Equal((0.0, 0.0), MatchResult.DoubleForfeit.ToPoints());
        Assert.Equal((1.0, 0.0), MatchResult.ByeFull.ToPoints());
        Assert.Equal((0.5, 0.0), MatchResult.ByeHalf.ToPoints());
        Assert.Equal((0.0, 0.0), MatchResult.ByeZero.ToPoints());
        Assert.Equal((0.0, 0.0), MatchResult.NotPlayed.ToPoints());
    }

    [Fact]
    public void Player_FirstAndLastName_SeparationAndSynchronization()
    {
        // 1. Setting FirstName and LastName separately
        var p1 = new Player
        {
            LastName = "Carlsen",
            FirstName = "Magnus"
        };
        Assert.Equal("Carlsen", p1.LastName);
        Assert.Equal("Magnus", p1.FirstName);
        Assert.Equal("Carlsen, Magnus", p1.FullName);

        // 2. Setting FullName with comma parses into LastName and FirstName
        var p2 = new Player { FullName = "Kasparov, Garry" };
        Assert.Equal("Kasparov", p2.LastName);
        Assert.Equal("Garry", p2.FirstName);
        Assert.Equal("Kasparov, Garry", p2.FullName);

        // 3. Setting FullName with space parses into LastName and FirstName
        var p3 = new Player { FullName = "Viswanathan Anand" };
        Assert.Equal("Anand", p3.LastName);
        Assert.Equal("Viswanathan", p3.FirstName);

        // 4. Updating LastName after setting FullName clears legacy override
        p2.LastName = "Fischer";
        p2.FirstName = "Bobby";
        Assert.Equal("Fischer, Bobby", p2.FullName);

        // 5. Single name player
        var p4 = new Player { LastName = "Madonna" };
        Assert.Equal("Madonna", p4.FullName);
    }

    [Fact]
    public void Player_EffectiveRating_RespectsPrecedence()
    {
        var player = new Player
        {
            FullName = "Test Player",
            FideRating = 2400,
            NationalRating = 2450
        };

        Assert.Equal(2400, player.EffectiveRating(SeedingPrecedence.FideFirst));
        Assert.Equal(2450, player.EffectiveRating(SeedingPrecedence.NationalFirst));

        var fideOnlyPlayer = new Player
        {
            FullName = "FIDE Only",
            FideRating = 2200,
            NationalRating = 0
        };
        Assert.Equal(2200, fideOnlyPlayer.EffectiveRating(SeedingPrecedence.NationalFirst)); // Fallback

        var natOnlyPlayer = new Player
        {
            FullName = "National Only",
            FideRating = 0,
            NationalRating = 1900
        };
        Assert.Equal(1900, natOnlyPlayer.EffectiveRating(SeedingPrecedence.FideFirst)); // Fallback
    }

    [Fact]
    public void Tournament_CurrentRound_ReturnsMaxRound()
    {
        var tournament = new Tournament();
        Assert.Equal(0, tournament.CurrentRoundNumber);

        tournament.Rounds.Add(new Round { RoundNumber = 1 });
        tournament.Rounds.Add(new Round { RoundNumber = 2 });

        Assert.Equal(2, tournament.CurrentRoundNumber);
    }

    [Fact]
    public void Tournament_MinAllowedRounds_WhenNoRounds_ReturnsOne()
    {
        var tournament = new Tournament { TotalRounds = 7 };
        Assert.Equal(0, tournament.FinishedRoundsCount);
        Assert.Equal(1, tournament.MinAllowedRounds);

        Assert.True(tournament.TrySetTotalRounds(5, out var error));
        Assert.Null(error);
        Assert.Equal(5, tournament.TotalRounds);

        Assert.False(tournament.TrySetTotalRounds(0, out var zeroError));
        Assert.NotNull(zeroError);
    }

    [Fact]
    public void Tournament_MinAllowedRounds_WhenRoundsFinished_EnforcesConstraint()
    {
        var tournament = new Tournament { TotalRounds = 7 };
        var p1 = new Player { Id = "p1", FullName = "P1" };
        var p2 = new Player { Id = "p2", FullName = "P2" };
        tournament.Players.AddRange([p1, p2]);

        // Round 1 finished
        tournament.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings = [new Pairing { WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.WhiteWin }]
        });

        // Round 2 finished
        tournament.Rounds.Add(new Round
        {
            RoundNumber = 2,
            Pairings = [new Pairing { WhitePlayerId = "p2", BlackPlayerId = "p1", Result = MatchResult.Draw }]
        });

        Assert.Equal(2, tournament.FinishedRoundsCount);
        Assert.Equal(2, tournament.MinAllowedRounds);

        // Cannot reduce below 2
        Assert.False(tournament.TrySetTotalRounds(1, out var err1));
        Assert.NotNull(err1);
        Assert.Equal(7, tournament.TotalRounds); // Unchanged

        // Can reduce to 2
        Assert.True(tournament.TrySetTotalRounds(2, out var err2));
        Assert.Null(err2);
        Assert.Equal(2, tournament.TotalRounds);

        // Can expand to 9
        Assert.True(tournament.TrySetTotalRounds(9, out var err9));
        Assert.Null(err9);
        Assert.Equal(9, tournament.TotalRounds);
    }

    [Fact]
    public void Tournament_TrySetTotalRounds_CleansUpUnplayedRoundsBeyondNewTotal()
    {
        var tournament = new Tournament { TotalRounds = 5 };

        // Round 1 finished
        tournament.Rounds.Add(new Round
        {
            RoundNumber = 1,
            Pairings = [new Pairing { WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.WhiteWin }]
        });

        // Round 2 paired but unplayed
        tournament.Rounds.Add(new Round
        {
            RoundNumber = 2,
            Pairings = [new Pairing { WhitePlayerId = "p2", BlackPlayerId = "p1", Result = MatchResult.NotPlayed }]
        });

        // Finished rounds is 1, so min is 1
        Assert.Equal(1, tournament.FinishedRoundsCount);
        Assert.Equal(1, tournament.MinAllowedRounds);

        // Reducing to 1 should remove unplayed round 2
        bool ok = tournament.TrySetTotalRounds(1, out var error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(1, tournament.TotalRounds);
        Assert.Single(tournament.Rounds);
        Assert.Equal(1, tournament.Rounds[0].RoundNumber);
    }

    [Fact]
    public void Tournament_ModifyGeneralDetails_Succeeds()
    {
        var tournament = new Tournament
        {
            Name = "Initial Cup",
            Venue = "Hall A",
            ChiefArbiter = "Arbiter One",
            DeputyArbiter = "Deputy One",
            Organizer = "Org One",
            Federation = "SVK",
            TimeControl = TimeControlCategory.Rapid,
            TimeControlDetails = "15m + 5s"
        };

        tournament.Name = "Updated Cup 2026";
        tournament.Venue = "Grand Hotel Bratislava";
        tournament.ChiefArbiter = "IA Tomas K.";
        tournament.DeputyArbiter = "FA John D.";
        tournament.Organizer = "Slovak Chess Academy";
        tournament.Federation = "CZE";
        tournament.TimeControl = TimeControlCategory.Blitz;
        tournament.TimeControlDetails = "3m + 2s";
        tournament.FideRated = true;

        Assert.Equal("Updated Cup 2026", tournament.Name);
        Assert.Equal("Grand Hotel Bratislava", tournament.Venue);
        Assert.Equal("IA Tomas K.", tournament.ChiefArbiter);
        Assert.Equal("FA John D.", tournament.DeputyArbiter);
        Assert.Equal("Slovak Chess Academy", tournament.Organizer);
        Assert.Equal("CZE", tournament.Federation);
        Assert.Equal(TimeControlCategory.Blitz, tournament.TimeControl);
        Assert.Equal("3m + 2s", tournament.TimeControlDetails);
        Assert.True(tournament.FideRated);
    }

    [Fact]
    public void FidePlayerCard_TitleAbbreviation_MapsFideOnlineArenaAndStandardTitles()
    {
        var gmCard = new CaissaDesk.Data.FidePlayerCard { Title = "Grandmaster" };
        var wgmCard = new CaissaDesk.Data.FidePlayerCard { Title = "Woman Grandmaster" };
        var agmCard = new CaissaDesk.Data.FidePlayerCard { Title = "Arena Grandmaster" };
        var aimCard = new CaissaDesk.Data.FidePlayerCard { Title = "Arena International Master" };
        var afmCard = new CaissaDesk.Data.FidePlayerCard { Title = "Arena FIDE Master" };
        var acmCard = new CaissaDesk.Data.FidePlayerCard { Title = "Arena Candidate Master" };

        Assert.Equal("GM", gmCard.TitleAbbreviation);
        Assert.Equal("WGM", wgmCard.TitleAbbreviation);
        Assert.Equal("AGM", agmCard.TitleAbbreviation);
        Assert.Equal("AIM", aimCard.TitleAbbreviation);
        Assert.Equal("AFM", afmCard.TitleAbbreviation);
        Assert.Equal("ACM", acmCard.TitleAbbreviation);
    }

    [Fact]
    public void Player_GetTitleWeight_HierarchyIsPreservedIncludingFOATitles()
    {
        Assert.True(Player.GetTitleWeight("GM") > Player.GetTitleWeight("WGM"));
        Assert.True(Player.GetTitleWeight("WGM") > Player.GetTitleWeight("IM"));
        Assert.True(Player.GetTitleWeight("IM") > Player.GetTitleWeight("WIM"));
        Assert.True(Player.GetTitleWeight("WIM") > Player.GetTitleWeight("FM"));
        Assert.True(Player.GetTitleWeight("FM") > Player.GetTitleWeight("WFM"));
        Assert.True(Player.GetTitleWeight("WFM") > Player.GetTitleWeight("CM"));
        Assert.True(Player.GetTitleWeight("CM") > Player.GetTitleWeight("WCM"));
        Assert.True(Player.GetTitleWeight("WCM") > Player.GetTitleWeight("AGM"));
        Assert.True(Player.GetTitleWeight("AGM") > Player.GetTitleWeight("AIM"));
        Assert.True(Player.GetTitleWeight("AIM") > Player.GetTitleWeight("AFM"));
        Assert.True(Player.GetTitleWeight("AFM") > Player.GetTitleWeight("ACM"));
        Assert.True(Player.GetTitleWeight("ACM") > Player.GetTitleWeight(null));
        Assert.Equal(0, Player.GetTitleWeight(""));
    }

    [Fact]
    public void Player_EditProperties_UpdatesCorrectly()
    {
        var player = new Player
        {
            FullName = "Old Name",
            Title = "ACM",
            Federation = "FID",
            FideRating = 1200,
            NationalRating = 1150,
            Club = "Old Club",
            BirthYear = 2005,
            Gender = "M",
            Status = PlayerStatus.Active,
            FideId = "123456",
            NationalId = "987"
        };

        player.FullName = "Novak, Jan";
        player.Title = "AGM";
        player.Federation = "SVK";
        player.FideRating = 1950;
        player.NationalRating = 1920;
        player.Club = "ŠK Slovan";
        player.BirthYear = 2000;
        player.Gender = "M";
        player.Status = PlayerStatus.Paused;
        player.FideId = "1490001";
        player.NationalId = "54321";

        Assert.Equal("Novak, Jan", player.FullName);
        Assert.Equal("AGM", player.Title);
        Assert.Equal("SVK", player.Federation);
        Assert.Equal(1950, player.FideRating);
        Assert.Equal(1920, player.NationalRating);
        Assert.Equal("ŠK Slovan", player.Club);
        Assert.Equal(2000, player.BirthYear);
        Assert.Equal("M", player.Gender);
        Assert.Equal(PlayerStatus.Paused, player.Status);
        Assert.Equal("1490001", player.FideId);
        Assert.Equal("54321", player.NationalId);
    }

    [Fact]
    public void Tournament_HasAnyResultsEntered_DetectsActiveResults()
    {
        var tournament = new Tournament { Id = "test_res" };
        int activeCount1 = tournament.Rounds.SelectMany(r => r.Pairings).Count(p => p.Result != MatchResult.NotPlayed);
        Assert.Equal(0, activeCount1);

        // Round 1 paired, no results yet
        var r1 = new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, Result = MatchResult.NotPlayed },
                new Pairing { BoardNumber = 2, Result = MatchResult.NotPlayed }
            ]
        };
        tournament.Rounds.Add(r1);
        int activeCount2 = tournament.Rounds.SelectMany(r => r.Pairings).Count(p => p.Result != MatchResult.NotPlayed);
        Assert.Equal(0, activeCount2);

        // 1 result entered
        r1.Pairings[0].Result = MatchResult.WhiteWin;
        int activeCount3 = tournament.Rounds.SelectMany(r => r.Pairings).Count(p => p.Result != MatchResult.NotPlayed);
        Assert.Equal(1, activeCount3);
    }

    [Fact]
    public void Tournament_ResetCurrentRound_PreservesPriorRounds()
    {
        var tournament = new Tournament
        {
            Id = "test_reset",
            System = TournamentSystem.SwissDutch,
            TotalRounds = 5
        };

        var r1 = new Round
        {
            RoundNumber = 1,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p2", Result = MatchResult.WhiteWin },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p3", BlackPlayerId = "p4", Result = MatchResult.Draw }
            ]
        };
        var r2 = new Round
        {
            RoundNumber = 2,
            Pairings =
            [
                new Pairing { BoardNumber = 1, WhitePlayerId = "p1", BlackPlayerId = "p3", Result = MatchResult.WhiteWin },
                new Pairing { BoardNumber = 2, WhitePlayerId = "p4", BlackPlayerId = "p2", Result = MatchResult.NotPlayed }
            ]
        };
        tournament.Rounds.Add(r1);
        tournament.Rounds.Add(r2);

        Assert.Equal(2, tournament.Rounds.Count);

        // Reset Round 2
        tournament.Rounds.RemoveAll(r => r.RoundNumber == 2);

        // Verify Round 1 is preserved with all results
        Assert.Single(tournament.Rounds);
        Assert.Equal(1, tournament.Rounds[0].RoundNumber);
        Assert.Equal(MatchResult.WhiteWin, tournament.Rounds[0].Pairings[0].Result);
        Assert.Equal(MatchResult.Draw, tournament.Rounds[0].Pairings[1].Result);
    }
}
