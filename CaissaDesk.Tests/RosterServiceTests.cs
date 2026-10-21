using CaissaDesk.Core.Roster;

namespace CaissaDesk.Tests;

public class RosterServiceTests
{
    private static Tournament WithPlayers(params Player[] players) => new() { Players = players.ToList() };

    [Fact]
    public void Reseed_OrdersByRatingThenTitleThenName()
    {
        var t = WithPlayers(
            new Player { Id = "a", StartRank = 1, FullName = "Zeta, Z", FideRating = 2000 },
            new Player { Id = "b", StartRank = 2, FullName = "Alpha, A", FideRating = 2000 },
            new Player { Id = "c", StartRank = 3, FullName = "Mid, M", FideRating = 2000, Title = "FM" },
            new Player { Id = "d", StartRank = 4, FullName = "Top, T", FideRating = 2400 });

        Assert.True(new RosterService().Reseed(t));

        Assert.Equal(["d", "c", "b", "a"], t.Players.Select(p => p.Id));
        Assert.Equal([1, 2, 3, 4], t.Players.Select(p => p.StartRank));
    }

    [Fact]
    public void Reseed_UsesSeedingPrecedence()
    {
        var t = WithPlayers(
            new Player { Id = "fide", StartRank = 1, FullName = "F", FideRating = 2300, NationalRating = 1900 },
            new Player { Id = "nat", StartRank = 2, FullName = "N", FideRating = 2000, NationalRating = 2350 });
        t.SeedingPrecedence = SeedingPrecedence.NationalFirst;

        new RosterService().Reseed(t);

        Assert.Equal("nat", t.Players[0].Id);
    }

    [Fact]
    public void Reseed_EmptyRoster_ReturnsFalse()
    {
        Assert.False(new RosterService().Reseed(new Tournament()));
    }

    [Fact]
    public void RemovePlayer_RenumbersRemainingInSeedOrder()
    {
        var t = WithPlayers(
            new Player { Id = "a", StartRank = 1 },
            new Player { Id = "c", StartRank = 3 },
            new Player { Id = "b", StartRank = 2 });

        Assert.True(new RosterService().RemovePlayer(t, "a"));

        Assert.Equal(1, t.Players.Single(p => p.Id == "b").StartRank);
        Assert.Equal(2, t.Players.Single(p => p.Id == "c").StartRank);
        Assert.False(new RosterService().RemovePlayer(t, "missing"));
    }

    [Fact]
    public void AddPlayer_AppendsAtNextSeedWithNormalisedValues()
    {
        var t = WithPlayers(new Player { Id = "a", StartRank = 1 });
        var form = new PlayerFormModel
        {
            LastName = "  Kamenický ",
            FirstName = " Tomáš ",
            Title = " ",
            Federation = "Slovakia",
            FideRating = 2100,
            Club = "",
            NationalId = " 123 ",
            FideId = ""
        };

        var player = new RosterService().AddPlayer(t, form);

        Assert.Same(player, t.Players[^1]);
        Assert.Equal(2, player.StartRank);
        Assert.Equal("Kamenický", player.LastName);
        Assert.Equal("Tomáš", player.FirstName);
        Assert.Null(player.Title);
        Assert.Equal("SVK", player.Federation);
        Assert.Null(player.Club);
        Assert.Equal("123", player.NationalId);
        Assert.Null(player.FideId);
        Assert.Equal("M", player.Gender);
    }

    [Theory]
    [InlineData("", "FID")]
    [InlineData("  ", "FID")]
    [InlineData("xyz", "XYZ")]
    [InlineData("svk", "SVK")]
    public void PlayerForm_NormalizedFederation(string input, string expected)
    {
        Assert.Equal(expected, new PlayerFormModel { Federation = input }.NormalizedFederation);
    }

    [Fact]
    public void PlayerForm_FromPlayer_RoundTripsThroughApplyTo()
    {
        var original = new Player
        {
            LastName = "Carlsen",
            FirstName = "Magnus",
            Title = "GM",
            Federation = "NOR",
            FideRating = 2830,
            NationalRating = 2800,
            Club = "Club",
            BirthYear = 1990,
            Gender = "M",
            NationalId = "42",
            FideId = "1503014",
            Status = PlayerStatus.Paused
        };

        var form = PlayerFormModel.FromPlayer(original);
        var copy = new Player();
        form.ApplyTo(copy);

        Assert.Equal(original.LastName, copy.LastName);
        Assert.Equal(original.FirstName, copy.FirstName);
        Assert.Equal(original.Title, copy.Title);
        Assert.Equal(original.Federation, copy.Federation);
        Assert.Equal(original.FideRating, copy.FideRating);
        Assert.Equal(original.NationalRating, copy.NationalRating);
        Assert.Equal(original.Club, copy.Club);
        Assert.Equal(original.BirthYear, copy.BirthYear);
        Assert.Equal(original.NationalId, copy.NationalId);
        Assert.Equal(original.FideId, copy.FideId);
        Assert.Equal(PlayerStatus.Paused, copy.Status);
    }

    [Fact]
    public void PlayerForm_ForNewPlayer_DefaultsToTournamentFederation()
    {
        Assert.Equal("CZE", PlayerFormModel.ForNewPlayer(new Tournament { Federation = "CZE" }).Federation);
        Assert.Equal("SVK", PlayerFormModel.ForNewPlayer(null).Federation);
    }

    [Fact]
    public void PlayerForm_QueryFlags()
    {
        Assert.False(new PlayerFormModel().HasName);
        Assert.False(new PlayerFormModel().CanLookup);
        Assert.True(new PlayerFormModel { NationalId = "1" }.HasLookupQuery);
        Assert.False(new PlayerFormModel { NationalId = "1" }.CanLookup);
        Assert.True(new PlayerFormModel { FideId = "1" }.CanLookup);
        Assert.True(new PlayerFormModel { FirstName = "x" }.HasName);
    }
}
