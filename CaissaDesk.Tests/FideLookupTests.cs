using CaissaDesk.Data;

namespace CaissaDesk.Tests;

public class FideLookupTests
{
    [Fact]
    public void RemoveDiacritics_StripsAccentsProperly()
    {
        Assert.Equal("Kamenicky", Player.RemoveDiacritics("Kamenický"));
        Assert.Equal("Cestina", Player.RemoveDiacritics("Čeština"));
        Assert.Equal("Zltucky kon", Player.RemoveDiacritics("Žltučký kôň"));
        Assert.Equal("Tomas", Player.RemoveDiacritics("Tomáš"));
        Assert.Equal("Goran", Player.RemoveDiacritics("Göran"));
    }

    [Theory]
    [InlineData("Slovakia", "SVK")]
    [InlineData("Slovak Republic", "SVK")]
    [InlineData("SVK", "SVK")]
    [InlineData("svk", "SVK")]
    [InlineData("Czech Republic", "CZE")]
    [InlineData("Czechia", "CZE")]
    [InlineData("CZE", "CZE")]
    [InlineData("Austria", "AUT")]
    [InlineData("Poland", "POL")]
    [InlineData("Hungary", "HUN")]
    [InlineData("Germany", "GER")]
    [InlineData("sk", "SVK")]
    [InlineData("cz", "CZE")]
    public void FideCountryHelper_ResolvesCountryToCode(string input, string expectedCode)
    {
        var code = FideCountryHelper.ToFideCode(input);
        Assert.Equal(expectedCode, code);
    }

    [Theory]
    [InlineData("https://ratings.fide.com/images/flags/sk.svg", "SVK")]
    [InlineData("/images/flags/cz.svg", "CZE")]
    [InlineData("/svg/SVK.svg", "SVK")]
    [InlineData("https://ratings.fide.com/svg/POL.svg", "POL")]
    public void FideCountryHelper_ExtractsCodeFromFlagUrl(string flagUrl, string expectedCode)
    {
        var code = FideCountryHelper.TryExtractFromFlagUrl(flagUrl);
        Assert.Equal(expectedCode, code);
    }

    [Fact]
    public void ParsePlayerCardHtml_ExtractsCountryAndFederationCode()
    {
        string sampleHtml = """
            <h1 class="player-title">Kamenicky, Oliver</h1>
            <p class="profile-info-id ">549005456</p>
            <div class="profile-info-row block1">
                <div class="profile_fed"><h5>Federation</h5></div>
                <div class="profile-info-country ">
                    <img src="/images/flags/sk.svg" height=30> Slovakia
                </div>
            </div>
            <div class="profile-info-row ">
                <h5>B-Year</h5>
                <p class="profile-info-byear ">2019 </p>
            </div>
            """;

        var card = FideScoutingService.ParsePlayerCardHtml("549005456", sampleHtml);

        Assert.NotNull(card);
        Assert.Equal("549005456", card.FideId);
        Assert.Equal("Kamenicky, Oliver", card.FullName);
        Assert.Equal("Slovakia", card.Federation);
        Assert.Equal("SVK", card.FederationCode);
        Assert.Equal(2019, card.BirthYear);
    }

    [Fact]
    public void ParseSearchResultsHtml_ExtractsFederationAndBirthYear()
    {
        string sampleSearchHtml = """
            <table id="table_results">
            <tr><th>Header</th></tr>
            <tr>
                <td data-label="FIDEID">549005456</td>
                <td data-label="Name"><a href=/profile/549005456>Kamenicky, Oliver</a></td>
                <td data-label="title"></td>
                <td data-label="Train. Ttl."></td>
                <td class="flag-wrapper"><img src="/svg/SVK.svg" alt="SVK">SVK</td>
                <td data-label="Rtg"></td>
                <td data-label="Rtg"></td>
                <td data-label="Rtg"></td>
                <td data-label="B-Year">2019</td>
            </tr>
            </table>
            """;

        var list = FideScoutingService.ParseSearchResultsHtml(sampleSearchHtml);

        Assert.Single(list);
        var item = list[0];
        Assert.Equal("549005456", item.FideId);
        Assert.Equal("Kamenicky, Oliver", item.FullName);
        Assert.Equal("SVK", item.Federation);
        Assert.Equal(2019, item.BirthYear);
    }

    [Fact]
    public void NameDiacriticsPreservation_WhenFideReturnsAscii()
    {
        string userEnteredLast = "Kamenický";
        string userEnteredFirst = "Oliver";

        var (fideLast, fideFirst) = Player.SplitFullName("Kamenicky, Oliver");

        // UI preservation check
        string finalLast = string.Equals(Player.RemoveDiacritics(userEnteredLast).Trim(), Player.RemoveDiacritics(fideLast).Trim(), StringComparison.OrdinalIgnoreCase)
            ? userEnteredLast
            : fideLast;

        string finalFirst = string.Equals(Player.RemoveDiacritics(userEnteredFirst).Trim(), Player.RemoveDiacritics(fideFirst).Trim(), StringComparison.OrdinalIgnoreCase)
            ? userEnteredFirst
            : fideFirst;

        Assert.Equal("Kamenický", finalLast);
        Assert.Equal("Oliver", finalFirst);
    }
}
