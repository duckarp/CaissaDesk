using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.Logging;

namespace CaissaDesk.Data;

public class ChessResultsScoutingService : IChessResultsScoutingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChessResultsScoutingService> _logger;

    private static readonly Regex ViewStateRegex = new(@"id=""__VIEWSTATE""\s+value=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex EventValidationRegex = new(@"id=""__EVENTVALIDATION""\s+value=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ViewStateGenRegex = new(@"id=""__VIEWSTATEGENERATOR""\s+value=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex FormActionRegex = new(@"<form[^>]+action=""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ChessResultsScoutingService(IHttpClientFactory httpClientFactory, ILogger<ChessResultsScoutingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Creates a pooled client from the named factory registration. Cookies are not shared between calls:
    /// each search gets its own <see cref="CookieContainer"/> applied by <see cref="ScoutingHttp.SendWithCookiesAsync"/>.
    /// </summary>
    private HttpClient CreateClient(TimeSpan timeout)
    {
        var client = _httpClientFactory.CreateClient(ScoutingHttp.ChessResultsClientName);
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ScoutingHttp.UserAgent);
        return client;
    }

    public async Task<List<ChessResultsTournamentEntry>> SearchPlayerTournamentsAsync(
        string? fideId,
        string? lastName,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var list = new List<ChessResultsTournamentEntry>();
        if (string.IsNullOrWhiteSpace(fideId) && string.IsNullOrWhiteSpace(lastName)) return list;

        try
        {
            var cookies = new CookieContainer();
            var client = CreateClient(TimeSpan.FromSeconds(20));

            string initialUrl = "https://chess-results.com/SpielerSuche.aspx?lan=1";
            using var getReq = new HttpRequestMessage(HttpMethod.Get, initialUrl);
            using var getResp = await ScoutingHttp.SendWithCookiesAsync(client, getReq, cookies, cancellationToken);
            if (!getResp.IsSuccessStatusCode) return list;

            string initialHtml = await getResp.Content.ReadAsStringAsync(cancellationToken);
            string baseUri = getResp.RequestMessage?.RequestUri?.ToString() ?? initialUrl;

            var postData = ExtractAspFormFields(initialHtml);
            if (postData == null) return list;

            postData["ctl00$P1$txt_nachname"] = (lastName ?? "").Trim();
            postData["ctl00$P1$txt_vorname"] = "";
            postData["ctl00$P1$txt_verein"] = "";
            postData["ctl00$P1$txt_ident"] = "";
            postData["ctl00$P1$txt_fideID"] = (fideId ?? "").Trim();
            postData["ctl00$P1$txt_von_tag"] = "";
            postData["ctl00$P1$txt_bis_tag"] = "";
            postData["ctl00$P1$txt_GJahr"] = "";
            postData["ctl00$P1$txt_min_elo"] = "";
            postData["ctl00$P1$txt_FED"] = "";
            postData["ctl00$P1$txt_Fed_tur"] = "";
            postData["ctl00$P1$combo_Sort"] = string.IsNullOrWhiteSpace(fideId) ? "0" : "2";
            postData["ctl00$P1$combo_anzahl_zeilen"] = "1";
            postData["ctl00$P1$cb_suchen"] = "Search";

            string action = FormActionRegex.Match(initialHtml).Groups[1].Value.Replace("&amp;", "&");
            var targetUri = new Uri(new Uri(baseUri), action);

            using var postReq = new HttpRequestMessage(HttpMethod.Post, targetUri)
            {
                Content = new FormUrlEncodedContent(postData)
            };
            postReq.Headers.Referrer = new Uri(baseUri);

            using var postResp = await ScoutingHttp.SendWithCookiesAsync(client, postReq, cookies, cancellationToken);
            if (!postResp.IsSuccessStatusCode) return list;

            string resultHtml = await postResp.Content.ReadAsStringAsync(cancellationToken);
            return ParseTournamentTableHtml(resultHtml, targetUri.ToString(), limit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching chess-results tournaments (FIDE ID {FideId}, last name {LastName})", fideId, lastName);
            return list;
        }
    }

    public async Task<string?> DownloadPlayerPgnsAsync(
        string? fideId,
        string? lastName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fideId) && string.IsNullOrWhiteSpace(lastName)) return null;

        try
        {
            var cookies = new CookieContainer();
            var client = CreateClient(TimeSpan.FromSeconds(25));

            string initialUrl = "https://chess-results.com/PartieSuche.aspx?lan=1";
            using var getReq = new HttpRequestMessage(HttpMethod.Get, initialUrl);
            using var getResp = await ScoutingHttp.SendWithCookiesAsync(client, getReq, cookies, cancellationToken);
            if (!getResp.IsSuccessStatusCode) return null;

            string initialHtml = await getResp.Content.ReadAsStringAsync(cancellationToken);
            string baseUri = getResp.RequestMessage?.RequestUri?.ToString() ?? initialUrl;

            var postData = ExtractAspFormFields(initialHtml);
            if (postData == null) return null;

            postData["ctl00$P1$txt_nachname"] = (lastName ?? "").Trim();
            postData["ctl00$P1$txt_vorname"] = "";
            postData["ctl00$P1$Txt_FideID"] = (fideId ?? "").Trim();
            postData["ctl00$P1$Txt_NatID"] = "";
            postData["ctl00$P1$txt_bez"] = "";
            postData["ctl00$P1$txt_dbkey"] = "";
            postData["ctl00$P1$txt_rdvon"] = "";
            postData["ctl00$P1$txt_rdbis"] = "";
            postData["ctl00$P1$txt_von_tag"] = "";
            postData["ctl00$P1$txt_bis_tag"] = "";
            postData["ctl00$P1$combo_spielerfarbe"] = "-";
            postData["ctl00$P1$cb_DownLoadPGN"] = "Download as PGN-File";

            string action = FormActionRegex.Match(initialHtml).Groups[1].Value.Replace("&amp;", "&");
            var targetUri = new Uri(new Uri(baseUri), action);

            using var postReq = new HttpRequestMessage(HttpMethod.Post, targetUri)
            {
                Content = new FormUrlEncodedContent(postData)
            };
            postReq.Headers.Referrer = new Uri(baseUri);

            using var postResp = await ScoutingHttp.SendWithCookiesAsync(client, postReq, cookies, cancellationToken);
            if (!postResp.IsSuccessStatusCode) return null;

            string content = await postResp.Content.ReadAsStringAsync(cancellationToken);
            if (content.Contains("[Event ", StringComparison.OrdinalIgnoreCase))
            {
                return content;
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error downloading chess-results PGNs (FIDE ID {FideId}, last name {LastName})", fideId, lastName);
            return null;
        }
    }

    public static List<ChessResultsTournamentEntry> ParseTournamentTableHtml(string html, string baseUrl = "https://chess-results.com", int limit = 25)
    {
        var list = new List<ChessResultsTournamentEntry>();
        if (string.IsNullOrWhiteSpace(html)) return list;

        var baseUri = new Uri(baseUrl);
        var rowMatches = Regex.Matches(html, @"<tr\s+class=""CRg[12]"">([\s\S]*?)</tr>", RegexOptions.IgnoreCase);

        foreach (Match rm in rowMatches)
        {
            string rowHtml = rm.Groups[1].Value;
            var tdMatches = Regex.Matches(rowHtml, @"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
            if (tdMatches.Count < 7) continue;

            // Col 0: Player link
            string playerCol = tdMatches[0].Groups[1].Value;
            string playerCardUrl = "";
            var pLink = Regex.Match(playerCol, @"href=""([^""]+)""", RegexOptions.IgnoreCase);
            if (pLink.Success)
            {
                string rel = pLink.Groups[1].Value.Replace("&amp;", "&");
                playerCardUrl = new Uri(baseUri, rel).ToString();
            }

            // Col 3: Club
            string club = tdMatches.Count > 3 ? StripHtml(tdMatches[3].Groups[1].Value).Trim() : "";

            // Col 4: Fed
            string fed = tdMatches.Count > 4 ? StripHtml(tdMatches[4].Groups[1].Value).Trim() : "";

            // Col 5: Tournament Name & Link
            string tourCol = tdMatches.Count > 5 ? tdMatches[5].Groups[1].Value : "";
            string tourName = StripHtml(tourCol).Trim();
            string tourUrl = "";
            var tLink = Regex.Match(tourCol, @"href=""([^""]+)""", RegexOptions.IgnoreCase);
            if (tLink.Success)
            {
                string rel = tLink.Groups[1].Value.Replace("&amp;", "&");
                tourUrl = new Uri(baseUri, rel).ToString();
            }

            // Col 6: End Date
            string endDate = tdMatches.Count > 6 ? StripHtml(tdMatches[6].Groups[1].Value).Trim() : "";

            // Col 7: Score / Rank
            string score = tdMatches.Count > 7 ? StripHtml(tdMatches[7].Groups[1].Value).Trim() : "";

            // Col 8: Rounds
            string rounds = tdMatches.Count > 8 ? StripHtml(tdMatches[8].Groups[1].Value).Trim() : "";

            // Col 9: Total Players
            string players = tdMatches.Count > 9 ? StripHtml(tdMatches[9].Groups[1].Value).Trim() : "";

            if (!string.IsNullOrEmpty(tourName))
            {
                list.Add(new ChessResultsTournamentEntry
                {
                    TournamentName = HttpUtility.HtmlDecode(tourName),
                    EndDate = endDate,
                    ScoreOrRank = score,
                    Rounds = rounds,
                    TotalPlayers = players,
                    Club = HttpUtility.HtmlDecode(club),
                    Federation = fed,
                    TournamentUrl = tourUrl,
                    PlayerCardUrl = playerCardUrl
                });

                if (list.Count >= limit) break;
            }
        }

        return list;
    }

    private static Dictionary<string, string>? ExtractAspFormFields(string html)
    {
        var vsMatch = ViewStateRegex.Match(html);
        var evMatch = EventValidationRegex.Match(html);
        if (!vsMatch.Success || !evMatch.Success) return null;

        var vsgMatch = ViewStateGenRegex.Match(html);

        return new Dictionary<string, string>
        {
            ["__LASTFOCUS"] = "",
            ["__VIEWSTATE"] = vsMatch.Groups[1].Value,
            ["__VIEWSTATEGENERATOR"] = vsgMatch.Success ? vsgMatch.Groups[1].Value : "",
            ["__EVENTTARGET"] = "",
            ["__EVENTARGUMENT"] = "",
            ["__EVENTVALIDATION"] = evMatch.Groups[1].Value
        };
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return Regex.Replace(input, @"<[^>]+>", " ");
    }
}
