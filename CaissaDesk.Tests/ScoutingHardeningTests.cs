using System.Net;
using System.Text;
using CaissaDesk.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaissaDesk.Tests;

public class ScoutingHardeningTests
{
    // ---------- FIDE profile parsing ----------

    [Fact]
    public void ParsePlayerCardHtml_RatingBlocksWithNestedDivs_ExtractsAllRatings()
    {
        // The old StdBlockRegex stopped at the first </div>, so a nested div before the rating <p> lost the value.
        string html = """
            <h1 class="player-title">Carlsen, Magnus</h1>
            <div class="profile-info-title ">
                <p>Grandmaster</p>
            </div>
            <div class="profile-games ">
                <div class="profile-standart profile-game ">
                    <div class="profile-game-icon"><img src="/img/std.svg"></div>
                    <p>2833</p>
                    <p>STANDARD</p>
                </div>
                <div class="profile-rapid profile-game ">
                    <div class="profile-game-icon"><img src="/img/rapid.svg"></div>
                    <p> 2823 </p>
                    <p>RAPID</p>
                </div>
                <div class="profile-blitz profile-game ">
                    <div class="profile-game-icon"><div class="inner"></div></div>
                    <p>2886</p>
                    <p>BLITZ</p>
                </div>
            </div>
            """;

        var card = FideScoutingService.ParsePlayerCardHtml("1503014", html);

        Assert.NotNull(card);
        Assert.Equal("Grandmaster", card.Title);
        Assert.Equal("GM", card.TitleAbbreviation);
        Assert.Equal(2833, card.StandardElo);
        Assert.Equal(2823, card.RapidElo);
        Assert.Equal(2886, card.BlitzElo);
    }

    [Fact]
    public void ParsePlayerCardHtml_UnratedBlock_DoesNotBorrowRatingFromNextBlock()
    {
        string html = """
            <h1 class="player-title">Novak, Jan</h1>
            <div class="profile-standart profile-game ">
                <div class="icon"></div>
                <p>Not rated</p>
                <p>STANDARD</p>
            </div>
            <div class="profile-rapid profile-game ">
                <p>1650</p>
            </div>
            """;

        var card = FideScoutingService.ParsePlayerCardHtml("123", html);

        Assert.NotNull(card);
        Assert.Null(card.StandardElo);
        Assert.Equal(1650, card.RapidElo);
        Assert.Null(card.BlitzElo);
    }

    [Fact]
    public void ParsePlayerCardHtml_RanksTitleNoneAndGender()
    {
        string html = """
            <h1 class="player-title">Kamenicky, Oliver</h1>
            <div class="profile-info-title "><p>None</p></div>
            <p class="profile-info-sex ">Male</p>
            <div class="profile-rank-block">
                <h5>World Rank</h5>
                <div><h6>All players</h6><p>500000</p></div>
                <div><h6>Active players</h6><p>400000</p></div>
            </div>
            <div class="profile-rank-block">
                <h5>National Rank SVK</h5>
                <div><h6>Active players</h6><p>1234</p></div>
            </div>
            """;

        var card = FideScoutingService.ParsePlayerCardHtml("549005456", html);

        Assert.NotNull(card);
        Assert.Null(card.Title);
        Assert.Equal("Male", card.Gender);
        Assert.Equal(500000, card.WorldRankAll);
        Assert.Equal(400000, card.WorldRankActive);
        Assert.Equal(1234, card.NationalRank);
        Assert.Equal("SVK", card.FederationCode);
    }

    [Fact]
    public void ParsePlayerCardHtml_NoName_ReturnsNull()
    {
        Assert.Null(FideScoutingService.ParsePlayerCardHtml("1", "<html><body>Not found</body></html>"));
        Assert.Null(FideScoutingService.ParsePlayerCardHtml("1", ""));
    }

    // ---------- FIDE search parsing ----------

    [Fact]
    public void ParseSearchResultsHtml_IgnoresRowsOutsideResultsTable()
    {
        string html = """
            <table class="layout"><tr><td>111</td><td>Decoy, Row</td><td></td><td></td><td>XXX</td></tr></table>
            <table id="table_results" class="display">
              <thead><tr><th>FIDE ID</th><th>Name</th></tr></thead>
              <tbody>
                <tr>
                  <td data-label="FIDEID">1503014</td>
                  <td data-label="Name"><a href=/profile/1503014>Carlsen, Magnus</a></td>
                  <td data-label="title">GM</td>
                  <td data-label="Train. Ttl."></td>
                  <td class="flag-wrapper"><img src="/svg/NOR.svg">NOR</td>
                  <td data-label="Rtg">2833</td>
                  <td data-label="Rtg">2823</td>
                  <td data-label="Rtg">2886</td>
                  <td data-label="B-Year">1990</td>
                </tr>
                <tr>
                  <td data-label="FIDEID">549005456</td>
                  <td data-label="Name"><a href=/profile/549005456>Kamenicky, Oliver</a></td>
                  <td data-label="title"></td>
                  <td data-label="Train. Ttl."></td>
                  <td class="flag-wrapper"><img src="/svg/SVK.svg">SVK</td>
                  <td data-label="Rtg"></td>
                  <td data-label="Rtg"></td>
                  <td data-label="Rtg"></td>
                  <td data-label="B-Year">2019</td>
                </tr>
              </tbody>
            </table>
            <table class="footer"><tr><td>222</td><td>Footer, Row</td><td></td><td></td><td>YYY</td></tr></table>
            """;

        var list = FideScoutingService.ParseSearchResultsHtml(html);

        Assert.Equal(2, list.Count);
        Assert.Equal("1503014", list[0].FideId);
        Assert.Equal("Carlsen, Magnus", list[0].FullName);
        Assert.Equal("GM", list[0].Title);
        Assert.Equal("NOR", list[0].Federation);
        Assert.Equal(2833, list[0].StandardElo);
        Assert.Equal(2823, list[0].RapidElo);
        Assert.Equal(2886, list[0].BlitzElo);
        Assert.Equal(1990, list[0].BirthYear);
        Assert.Equal("549005456", list[1].FideId);
        Assert.Null(list[1].StandardElo);
    }

    [Fact]
    public void ParseSearchResultsHtml_SkipsNonNumericIdRowsAndHonoursLimit()
    {
        var sb = new StringBuilder("<table id=\"table_results\">");
        sb.Append("<tr><td>No results</td><td>x</td><td></td><td></td><td></td></tr>");
        for (int i = 1; i <= 5; i++)
        {
            sb.Append($"<tr><td>{1000 + i}</td><td>Player, {i}</td><td></td><td></td><td>SVK</td></tr>");
        }
        sb.Append("</table>");

        var list = FideScoutingService.ParseSearchResultsHtml(sb.ToString(), limit: 3);

        Assert.Equal(3, list.Count);
        Assert.Equal("1001", list[0].FideId);
        Assert.Equal("1003", list[2].FideId);
    }

    [Fact]
    public void ParseSearchResultsHtml_NoTable_ReturnsEmpty()
    {
        Assert.Empty(FideScoutingService.ParseSearchResultsHtml("<div>Nothing found</div>"));
    }

    // ---------- Error sanitizing / UA / transport ----------

    [Fact]
    public void SanitizeServerError_StripsHtmlAndScripts()
    {
        string body = "<html><head><style>body{color:red}</style><script>alert('x')</script></head>"
                      + "<body><h1>502 Bad&nbsp;Gateway</h1>\n\n<p>nginx &amp; co</p></body></html>";

        string result = ScoutingHttp.SanitizeServerError(body);

        Assert.Equal("502 Bad Gateway nginx & co", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void SanitizeServerError_TruncatesLongBodies()
    {
        string result = ScoutingHttp.SanitizeServerError(new string('a', 1000));
        Assert.Equal(301, result.Length);
        Assert.EndsWith("…", result);
        Assert.Equal(string.Empty, ScoutingHttp.SanitizeServerError(null));
    }

    [Fact]
    public void UserAgent_IsHonestAndBrowserCompatible()
    {
        Assert.StartsWith("Mozilla/5.0 (compatible; CaissaDesk/", ScoutingHttp.UserAgent);
        Assert.Contains("+https://github.com/duckarp/CaissaDesk", ScoutingHttp.UserAgent);
        Assert.DoesNotContain("Chrome", ScoutingHttp.UserAgent);
        // Must be parseable as a header value.
        using var req = new HttpRequestMessage();
        req.Headers.UserAgent.ParseAdd(ScoutingHttp.UserAgent);
    }

    [Theory]
    [InlineData("https://results.example.org", true)]
    [InlineData("http://localhost:5200", true)]
    [InlineData("http://127.0.0.1:5200", true)]
    [InlineData("http://[::1]:5200", true)]
    [InlineData("http://192.168.1.10:5200", false)]
    [InlineData("http://results.example.org", false)]
    [InlineData("ftp://results.example.org", false)]
    [InlineData("not a url", false)]
    public void GetInsecureTransportError_AllowsOnlyHttpsOrLoopbackHttp(string url, bool allowed)
    {
        var error = CaissaResultsSyncService.GetInsecureTransportError(url);
        Assert.Equal(allowed, error == null);
    }

    // ---------- HTTP behaviour (fake handlers) ----------

    [Fact]
    public async Task FideGetPlayerCard_PropagatesCallerCancellation()
    {
        using var cts = new CancellationTokenSource();
        var handler = new DelegateHandler(async (req, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = new FideScoutingService(new FakeFactory(handler), NullLogger<FideScoutingService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetPlayerCardAsync("1503014", cts.Token));
    }

    [Fact]
    public async Task FideGetPlayerCard_NetworkErrorReturnsNullAndSendsHonestUa()
    {
        string? seenUa = null;
        var handler = new DelegateHandler((Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>)((req, ct) =>
        {
            seenUa = req.Headers.UserAgent.ToString();
            throw new HttpRequestException("boom");
        }));
        var service = new FideScoutingService(new FakeFactory(handler), NullLogger<FideScoutingService>.Instance);

        var card = await service.GetPlayerCardAsync("1503014");

        Assert.Null(card);
        Assert.NotNull(seenUa);
        Assert.Contains("CaissaDesk/", seenUa);
    }

    [Fact]
    public async Task ChessResultsSearch_CarriesCookiesAcrossRedirectAndPostbackWithinOneCall()
    {
        const string formHtml = """
            <form method="post" action="./SpielerSuche.aspx?lan=1&amp;x=1" id="aspnetForm">
            <input type="hidden" name="__VIEWSTATE" id="__VIEWSTATE" value="VS" />
            <input type="hidden" name="__VIEWSTATEGENERATOR" id="__VIEWSTATEGENERATOR" value="GEN" />
            <input type="hidden" name="__EVENTVALIDATION" id="__EVENTVALIDATION" value="EV" />
            </form>
            """;
        const string resultHtml = """
            <table><tr class="CRg1"><td><a href="player.aspx?id=1">Kamenicky</a></td><td></td><td></td>
            <td>Club</td><td>SVK</td><td><a href="tnr1.aspx">Open 2025</a></td><td>2025/01/01</td><td>5</td><td>9</td><td>80</td></tr></table>
            """;

        var requests = new List<(HttpMethod Method, string Url, string? Cookie, string? Body)>();
        var handler = new DelegateHandler(async (req, ct) =>
        {
            string? cookie = req.Headers.TryGetValues("Cookie", out var c) ? string.Join("; ", c) : null;
            string? body = req.Content != null ? await req.Content.ReadAsStringAsync(ct) : null;
            requests.Add((req.Method, req.RequestUri!.ToString(), cookie, body));

            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path == "/SpielerSuche.aspx" && !req.RequestUri.Query.Contains("redirected"))
            {
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.Location = new Uri("/SpielerSuche.aspx?lan=1&redirected=1", UriKind.Relative);
                r.Headers.Add("Set-Cookie", "ASP.NET_SessionId=abc123; path=/; HttpOnly");
                return r;
            }
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(formHtml) };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(resultHtml) };
        });
        var service = new ChessResultsScoutingService(new FakeFactory(handler), NullLogger<ChessResultsScoutingService>.Instance);

        var list = await service.SearchPlayerTournamentsAsync("549005456", "Kamenicky");

        Assert.Equal(3, requests.Count);
        Assert.Null(requests[0].Cookie);
        Assert.Equal("ASP.NET_SessionId=abc123", requests[1].Cookie);
        Assert.Equal(HttpMethod.Post, requests[2].Method);
        Assert.Equal("https://chess-results.com/SpielerSuche.aspx?lan=1&x=1", requests[2].Url);
        Assert.Equal("ASP.NET_SessionId=abc123", requests[2].Cookie);
        Assert.Contains("__VIEWSTATE=VS", requests[2].Body);
        Assert.Contains("549005456", requests[2].Body);

        var entry = Assert.Single(list);
        Assert.Equal("Open 2025", entry.TournamentName);
        Assert.Equal("https://chess-results.com/tnr1.aspx", entry.TournamentUrl);

        // A second call must start with a fresh cookie jar (no cross-call leakage).
        requests.Clear();
        await service.SearchPlayerTournamentsAsync("549005456", "Kamenicky");
        Assert.Null(requests[0].Cookie);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send)
            : this((r, ct) => Task.FromResult(send(r, ct))) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
