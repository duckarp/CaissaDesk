using CaissaDesk.Core.Roster;
using CaissaDesk.Data;

namespace CaissaDesk.Tests;

public class PlayerLookupServiceTests
{
    private sealed class FakeFide : IFideScoutingService
    {
        public Dictionary<string, FidePlayerCard> Cards { get; } = [];
        public Dictionary<string, List<FideSearchResult>> Searches { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Queries { get; } = [];
        public bool Throw { get; set; }

        public Task<FidePlayerCard?> GetPlayerCardAsync(string fideId, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new HttpRequestException("offline");
            return Task.FromResult(Cards.GetValueOrDefault(fideId));
        }

        public Task<List<FideSearchResult>> SearchPlayersByNameAsync(string query, int limit = 15, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new HttpRequestException("offline");
            Queries.Add(query);
            return Task.FromResult(Searches.GetValueOrDefault(query) ?? []);
        }
    }

    private sealed class FakeNational : INationalScoutingService
    {
        public NationalPlayerCard? Card { get; set; }
        public bool Throw { get; set; }
        public int Calls { get; private set; }

        public Task<NationalPlayerCard?> LookupPlayerAsync(string? nationalId, string? fideId, string? lastName, string? firstName, string? federation, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("national down");
            return Task.FromResult(Card);
        }
    }

    private static FidePlayerCard Carlsen() => new()
    {
        FideId = "1503014",
        FullName = "Carlsen, Magnus",
        Title = "Grandmaster",
        FederationCode = "NOR",
        Federation = "Norway",
        StandardElo = 2830,
        RapidElo = 2820,
        BlitzElo = 2880,
        BirthYear = 1990
    };

    private static (PlayerLookupService Service, FakeFide Fide, FakeNational National) Create()
    {
        var fide = new FakeFide();
        var national = new FakeNational();
        return (new PlayerLookupService(fide, national) { SyncThrottle = TimeSpan.Zero }, fide, national);
    }

    // ------------------------------------------------------------ best match heuristics

    [Fact]
    public void SelectBestFideMatch_PrefersExactNameAndFederation()
    {
        var results = new List<FideSearchResult>
        {
            new() { FideId = "1", FullName = "Novak, Jan", Federation = "CZE" },
            new() { FideId = "2", FullName = "Novak, Jan", Federation = "SVK" },
            new() { FideId = "3", FullName = "Novak, Peter", Federation = "SVK" }
        };

        Assert.Equal("2", PlayerLookupService.SelectBestFideMatch(results, "Novák", "Ján", "SVK")!.FideId);
    }

    [Fact]
    public void SelectBestFideMatch_NeverPicksDifferentLastName()
    {
        var results = new List<FideSearchResult> { new() { FideId = "1", FullName = "Smith, Jan", Federation = "SVK" } };

        Assert.Null(PlayerLookupService.SelectBestFideMatch(results, "Novak", "Jan", "SVK"));
    }

    [Fact]
    public void SelectBestFideMatch_RatedCandidateWinsTie()
    {
        var results = new List<FideSearchResult>
        {
            new() { FideId = "unrated", FullName = "Novak, Jan" },
            new() { FideId = "rated", FullName = "Novak, Jan", StandardElo = 1800 }
        };

        Assert.Equal("rated", PlayerLookupService.SelectBestFideMatch(results, "Novak", "Jan", null)!.FideId);
    }

    [Fact]
    public void SelectBestFideMatch_EmptyInput_IsNull()
    {
        Assert.Null(PlayerLookupService.SelectBestFideMatch(null, "a", "b", null));
        Assert.Null(PlayerLookupService.SelectBestFideMatch([], "a", "b", null));
    }

    [Theory]
    [InlineData(null, 2830)]
    [InlineData(TimeControlCategory.Standard, 2830)]
    [InlineData(TimeControlCategory.Rapid, 2820)]
    [InlineData(TimeControlCategory.Blitz, 2880)]
    public void GetRatingForTimeControl_PicksMatchingList(TimeControlCategory? tc, int expected)
    {
        Assert.Equal(expected, PlayerLookupService.GetRatingForTimeControl(Carlsen(), tc));
    }

    [Fact]
    public void GetRatingForTimeControl_FallsBackToOtherLists()
    {
        Assert.Equal(2100, PlayerLookupService.GetRatingForTimeControl(new FidePlayerCard { StandardElo = 2100 }, TimeControlCategory.Blitz));
        Assert.Equal(0, PlayerLookupService.GetRatingForTimeControl(new FidePlayerCard(), TimeControlCategory.Rapid));
    }

    [Theory]
    [InlineData("F", null, "F")]
    [InlineData("female", "GM", "F")]
    [InlineData(null, "WGM", "F")]
    [InlineData("M", "GM", "M")]
    [InlineData(null, null, "M")]
    public void NormalizeGender_UsesCardThenTitle(string? gender, string? title, string expected)
    {
        Assert.Equal(expected, PlayerLookupService.NormalizeGender(gender, title));
    }

    // ------------------------------------------------------------ smart lookup

    [Fact]
    public async Task SmartLookup_ById_StripsNonDigits()
    {
        var (svc, fide, _) = Create();
        fide.Cards["1503014"] = Carlsen();

        var card = await svc.SmartFideLookupAsync(" 1503-014 ", null, null, null);

        Assert.Equal("1503014", card!.FideId);
        Assert.Empty(fide.Queries);
    }

    [Fact]
    public async Task SmartLookup_FallsBackToLastNameSearch_AndBuildsCardFromSearchResult()
    {
        var (svc, fide, _) = Create();
        fide.Searches["Carlsen"] = [new FideSearchResult { FideId = "1503014", FullName = "Carlsen, Magnus", Federation = "Norway", StandardElo = 2830 }];

        var card = await svc.SmartFideLookupAsync(null, "Carlsen", "Magnus", "NOR");

        Assert.Equal(["Carlsen Magnus", "Carlsen"], fide.Queries);
        Assert.Equal("1503014", card!.FideId);
        Assert.Equal(2830, card.StandardElo);
    }

    [Fact]
    public async Task SmartLookup_TooShortQuery_ReturnsNull()
    {
        var (svc, fide, _) = Create();

        Assert.Null(await svc.SmartFideLookupAsync(null, "X", "", null));
        Assert.Empty(fide.Queries);
    }

    // ------------------------------------------------------------ form lookup

    [Fact]
    public async Task LookupIntoForm_FillsFromFideAndNational_KeepingTypedDiacritics()
    {
        var (svc, fide, national) = Create();
        fide.Cards["1"] = new FidePlayerCard { FideId = "1", FullName = "Kamenicky, Tomas", FederationCode = "SVK", RapidElo = 1900, Title = "FIDE Master" };
        national.Card = new NationalPlayerCard { NationalId = "N7", Club = "ŠK Slovan", NationalRating = 1950, FullName = "Kamenický, Tomáš" };
        var form = new PlayerFormModel { FideId = "1", LastName = "Kamenický", FirstName = "Tomáš", Federation = "" };

        var outcome = await svc.LookupIntoFormAsync(form, TimeControlCategory.Rapid);

        Assert.Equal(PlayerLookupStatus.Found, outcome.Status);
        Assert.Equal("Kamenicky, Tomas", outcome.MatchedName);
        Assert.Equal("Kamenický", form.LastName);
        Assert.Equal("Tomáš", form.FirstName);
        Assert.Equal("FM", form.Title);
        Assert.Equal("SVK", form.Federation);
        Assert.Equal(1900, form.FideRating);
        Assert.Equal("N7", form.NationalId);
        Assert.Equal("ŠK Slovan", form.Club);
        Assert.Equal(1950, form.NationalRating);
    }

    [Fact]
    public async Task LookupIntoForm_OnlyNationalMatch_FillsIdentityFromNational()
    {
        var (svc, fide, national) = Create();
        fide.Cards["99"] = new FidePlayerCard { FideId = "99", FullName = "Doe, Jane", Title = "Woman FIDE Master", StandardElo = 2000 };
        national.Card = new NationalPlayerCard { FullName = "Doe, Jane", FideId = "99", Federation = "SVK" };
        var form = new PlayerFormModel { NationalId = "abc", Federation = "" };

        var outcome = await svc.LookupIntoFormAsync(form, null);

        Assert.Equal(PlayerLookupStatus.Found, outcome.Status);
        Assert.Equal("Doe, Jane", outcome.MatchedName);
        Assert.Equal("Doe", form.LastName);
        Assert.Equal("Jane", form.FirstName);
        Assert.Equal("99", form.FideId);
        Assert.Equal("WFM", form.Title);
        Assert.Equal("F", form.Gender);
        Assert.Equal(2000, form.FideRating);
        Assert.Equal("SVK", form.Federation);
    }

    [Fact]
    public async Task LookupIntoForm_NothingFound_ReportsNotFound()
    {
        var (svc, _, _) = Create();

        var outcome = await svc.LookupIntoFormAsync(new PlayerFormModel { LastName = "Nobody" }, null);

        Assert.Equal(PlayerLookupStatus.NotFound, outcome.Status);
    }

    [Fact]
    public async Task LookupIntoForm_NationalFailure_IsSwallowed()
    {
        var (svc, fide, national) = Create();
        fide.Cards["1503014"] = Carlsen();
        national.Throw = true;

        var outcome = await svc.LookupIntoFormAsync(new PlayerFormModel { FideId = "1503014" }, null);

        Assert.Equal(PlayerLookupStatus.Found, outcome.Status);
    }

    [Fact]
    public async Task LookupIntoForm_FideFailure_ReportsFailed()
    {
        var (svc, fide, _) = Create();
        fide.Throw = true;

        var outcome = await svc.LookupIntoFormAsync(new PlayerFormModel { FideId = "1" }, null);

        Assert.Equal(PlayerLookupStatus.Failed, outcome.Status);
        Assert.Equal("offline", outcome.FailureMessage);
    }

    // ------------------------------------------------------------ bulk sync

    [Fact]
    public async Task SyncPlayers_UpdatesMatchedPlayers_ReportsProgress_AndKeepsExistingNationalData()
    {
        var (svc, fide, national) = Create();
        fide.Cards["1503014"] = Carlsen();
        national.Card = new NationalPlayerCard { NationalId = "N1", Club = "New club", NationalRating = 2850 };
        var players = new List<Player>
        {
            new() { FullName = "Carlsen, Magnus", FideId = "1503014", Club = "Old club" },
            new() { FullName = "Unknown, Player" }
        };
        var progress = new List<(int, int)>();

        int updated = await svc.SyncPlayersAsync(players, TimeControlCategory.Blitz, (c, t) => progress.Add((c, t)));

        Assert.Equal(1, updated);
        Assert.Equal([(1, 2), (2, 2)], progress);
        Assert.Equal(2880, players[0].FideRating);
        Assert.Equal("GM", players[0].Title);
        Assert.Equal("NOR", players[0].Federation);
        Assert.Equal("Old club", players[0].Club);
        Assert.Equal("N1", players[0].NationalId);
        Assert.Equal(2850, players[0].NationalRating);
        Assert.Equal(2, national.Calls);
    }

    [Fact]
    public async Task SyncPlayers_FailuresForOnePlayer_DoNotStopTheRest()
    {
        var (svc, fide, national) = Create();
        fide.Throw = true;
        var players = new List<Player> { new() { FullName = "A, B", FideId = "1" }, new() { FullName = "C, D", FideId = "2" } };

        int updated = await svc.SyncPlayersAsync(players, null);

        Assert.Equal(0, updated);
        Assert.Equal(0, national.Calls);
    }
}
