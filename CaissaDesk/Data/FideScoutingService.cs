using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Caissa.Shared.Models;
using Microsoft.Extensions.Logging;

namespace CaissaDesk.Data;

public class FideScoutingService : IFideScoutingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FideScoutingService> _logger;

    private static readonly Regex NameRegex = new(@"<h1\s+class=""player-title"">([^<]+)</h1>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FideIdRegex = new(@"<p\s+class=""profile-info-id\s*"">([^<]+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TitleRegex = new(@"<div\s+class=""profile-info-title\s*"">\s*<p>([^<]+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CountryRegex = new(@"<div\s+class=""profile-info-country\s*"">(?:[\s\S]*?<img\s+src=""([^""]+)""[^>]*>)?\s*([^<\r\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BirthYearRegex = new(@"<p\s+class=""profile-info-byear\s*"">(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GenderRegex = new(@"<p\s+class=""profile-info-sex\s*"">([^<]+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Opening tags of the rating blocks. The block body is extracted with balanced <div> matching
    // (see ExtractElementInner) so nested <div>s inside a block no longer truncate it.
    private static readonly Regex StdBlockRegex = new(@"<div\b[^>]*\bclass=""[^""]*\bprofile-standart\b[^""]*\bprofile-game\b[^""]*""[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RapidBlockRegex = new(@"<div\b[^>]*\bclass=""[^""]*\bprofile-rapid\b[^""]*\bprofile-game\b[^""]*""[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BlitzBlockRegex = new(@"<div\b[^>]*\bclass=""[^""]*\bprofile-blitz\b[^""]*\bprofile-game\b[^""]*""[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ResultsTableRegex = new(@"<table\b[^>]*\bid=[""']?table_results\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AnyTableRegex = new(@"<table\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RatingNumRegex = new(@"<p>\s*(\d+)\s*</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WorldRankActiveRegex = new(@"<h5>World Rank</h5>[\s\S]*?<h6>Active players</h6>\s*<p>(\d+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WorldRankAllRegex = new(@"<h5>World Rank</h5>[\s\S]*?<h6>All players</h6>\s*<p>(\d+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NationalRankRegex = new(@"<h5>National Rank\s*([A-Z]{3})?</h5>[\s\S]*?<h6>Active players</h6>\s*<p>(\d+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public FideScoutingService(IHttpClientFactory httpClientFactory, ILogger<FideScoutingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<FidePlayerCard?> GetPlayerCardAsync(string fideId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fideId)) return null;
        string cleanId = Regex.Replace(fideId.Trim(), @"[^\d]", "");
        if (string.IsNullOrEmpty(cleanId)) return null;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            string url = $"https://ratings.fide.com/profile/{cleanId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(ScoutingHttp.UserAgent);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) return null;

            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParsePlayerCardHtml(cleanId, html);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching FIDE profile for {FideId}", cleanId);
            return null;
        }
    }

    public async Task<List<FideSearchResult>> SearchPlayersByNameAsync(string query, int limit = 15, CancellationToken cancellationToken = default)
    {
        var results = new List<FideSearchResult>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        // FIDE search backend fails or drops query tokens with accents (e.g. 'Kamenický' -> matches 'Oliver' only).
        // Strip diacritics and collapse multiple spaces before calling FIDE search.
        string cleanQuery = Player.RemoveDiacritics(query.Trim());
        cleanQuery = Regex.Replace(cleanQuery, @"\s+", " ").Trim();
        if (cleanQuery.Length < 2) return results;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            string url = $"https://ratings.fide.com/incl_search_l.php?search={Uri.EscapeDataString(cleanQuery)}&simple=1";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(ScoutingHttp.UserAgent);
            request.Headers.Referrer = new Uri("https://ratings.fide.com/");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return results;

            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseSearchResultsHtml(html, limit);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching FIDE players for {Query}", cleanQuery);
            return results;
        }
    }

    public static FidePlayerCard? ParsePlayerCardHtml(string fideId, string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var nameMatch = NameRegex.Match(html);
        if (!nameMatch.Success) return null;

        string fullName = HttpUtility.HtmlDecode(nameMatch.Groups[1].Value.Trim());
        var card = new FidePlayerCard
        {
            FideId = fideId,
            FullName = fullName
        };

        var titleMatch = TitleRegex.Match(html);
        if (titleMatch.Success)
        {
            string t = HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
            if (!string.IsNullOrEmpty(t) && !string.Equals(t, "None", StringComparison.OrdinalIgnoreCase))
            {
                card.Title = t;
            }
        }

        var countryMatch = CountryRegex.Match(html);
        if (countryMatch.Success)
        {
            if (countryMatch.Groups[1].Success)
            {
                string flagRel = countryMatch.Groups[1].Value.Trim();
                card.FlagUrl = flagRel.StartsWith("http", StringComparison.OrdinalIgnoreCase) 
                    ? flagRel 
                    : $"https://ratings.fide.com{flagRel}";

                string? codeFromFlag = FideCountryHelper.TryExtractFromFlagUrl(card.FlagUrl);
                if (!string.IsNullOrEmpty(codeFromFlag))
                {
                    card.FederationCode = codeFromFlag;
                }
            }
            string fed = HttpUtility.HtmlDecode(countryMatch.Groups[2].Value.Trim());
            if (!string.IsNullOrEmpty(fed))
            {
                card.Federation = fed;
                card.FederationCode ??= FideCountryHelper.ToFideCode(fed);
            }
        }

        var byearMatch = BirthYearRegex.Match(html);
        if (byearMatch.Success && int.TryParse(byearMatch.Groups[1].Value, out int byear))
        {
            card.BirthYear = byear;
        }

        var genderMatch = GenderRegex.Match(html);
        if (genderMatch.Success)
        {
            card.Gender = HttpUtility.HtmlDecode(genderMatch.Groups[1].Value.Trim());
        }

        card.StandardElo = ExtractRatingFromBlock(StdBlockRegex, html);
        card.RapidElo = ExtractRatingFromBlock(RapidBlockRegex, html);
        card.BlitzElo = ExtractRatingFromBlock(BlitzBlockRegex, html);

        var rankActMatch = WorldRankActiveRegex.Match(html);
        if (rankActMatch.Success && int.TryParse(rankActMatch.Groups[1].Value, out int rankAct))
        {
            card.WorldRankActive = rankAct;
        }

        var rankAllMatch = WorldRankAllRegex.Match(html);
        if (rankAllMatch.Success && int.TryParse(rankAllMatch.Groups[1].Value, out int rankAll))
        {
            card.WorldRankAll = rankAll;
        }

        var natRankMatch = NationalRankRegex.Match(html);
        if (natRankMatch.Success && int.TryParse(natRankMatch.Groups[2].Value, out int natRank))
        {
            card.NationalRank = natRank;
            if (natRankMatch.Groups[1].Success && string.IsNullOrEmpty(card.FederationCode))
            {
                card.FederationCode = natRankMatch.Groups[1].Value.ToUpperInvariant();
            }
        }

        if (string.IsNullOrEmpty(card.FederationCode) && !string.IsNullOrEmpty(card.Federation))
        {
            card.FederationCode = FideCountryHelper.ToFideCode(card.Federation);
        }

        return card;
    }

    public static List<FideSearchResult> ParseSearchResultsHtml(string html, int limit = 15)
    {
        var list = new List<FideSearchResult>();
        if (string.IsNullOrWhiteSpace(html)) return list;

        // Restrict parsing to the #table_results table (fall back to the first table in the fragment);
        // rows elsewhere on the page (navigation, footers, ads) are ignored.
        string? tableHtml = ExtractElementInner(html, "table", ResultsTableRegex)
                            ?? ExtractElementInner(html, "table", AnyTableRegex);
        if (tableHtml == null) return list;

        var rowMatches = Regex.Matches(tableHtml, @"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase);
        foreach (Match rm in rowMatches)
        {
            string rowContent = rm.Groups[1].Value;
            if (rowContent.Contains("<th", StringComparison.OrdinalIgnoreCase)) continue;

            var tdMatches = Regex.Matches(rowContent, @"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
            if (tdMatches.Count < 5) continue;

            string id = StripHtml(tdMatches[0].Groups[1].Value).Trim();
            string name = StripHtml(tdMatches[1].Groups[1].Value).Trim();
            string title = StripHtml(tdMatches[2].Groups[1].Value).Trim();
            // td[3] is Trainer Title
            string fed = StripHtml(tdMatches[4].Groups[1].Value).Trim();

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;
            if (!id.All(char.IsAsciiDigit)) continue; // FIDE IDs are numeric; skip stray non-result rows

            string fedDecoded = HttpUtility.HtmlDecode(fed);
            string fedCode = FideCountryHelper.ToFideCode(fedDecoded) ?? fedDecoded;

            var item = new FideSearchResult
            {
                FideId = id,
                FullName = HttpUtility.HtmlDecode(name),
                Title = HttpUtility.HtmlDecode(title),
                Federation = fedCode
            };

            if (tdMatches.Count > 5)
            {
                string std = StripHtml(tdMatches[5].Groups[1].Value).Trim();
                if (int.TryParse(std, out int sVal)) item.StandardElo = sVal;
            }
            if (tdMatches.Count > 6)
            {
                string rpd = StripHtml(tdMatches[6].Groups[1].Value).Trim();
                if (int.TryParse(rpd, out int rVal)) item.RapidElo = rVal;
            }
            if (tdMatches.Count > 7)
            {
                string blz = StripHtml(tdMatches[7].Groups[1].Value).Trim();
                if (int.TryParse(blz, out int bVal)) item.BlitzElo = bVal;
            }
            if (tdMatches.Count > 8)
            {
                string by = StripHtml(tdMatches[8].Groups[1].Value).Trim();
                if (int.TryParse(by, out int yVal)) item.BirthYear = yVal;
            }

            list.Add(item);
            if (list.Count >= limit) break;
        }

        return list;
    }

    private static int? ExtractRatingFromBlock(Regex blockOpenRegex, string html)
    {
        string? block = ExtractElementInner(html, "div", blockOpenRegex);
        if (block != null)
        {
            var numMatch = RatingNumRegex.Match(block);
            if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int elo))
            {
                return elo;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the first element whose opening tag matches <paramref name="openTagRegex"/> and returns its inner HTML,
    /// honouring nested elements of the same <paramref name="tagName"/>. If the element is never closed,
    /// the rest of the document is returned. Returns null when no opening tag matches.
    /// </summary>
    internal static string? ExtractElementInner(string html, string tagName, Regex openTagRegex)
    {
        var open = openTagRegex.Match(html);
        if (!open.Success) return null;

        int start = open.Index + open.Length;
        var tokenRegex = new Regex($@"<(/?){tagName}\b[^>]*>", RegexOptions.IgnoreCase);
        int depth = 1;
        for (var m = tokenRegex.Match(html, start); m.Success; m = m.NextMatch())
        {
            if (m.Groups[1].Length > 0)
            {
                depth--;
                if (depth == 0) return html[start..m.Index];
            }
            else if (!m.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                depth++;
            }
        }
        return html[start..];
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return Regex.Replace(input, @"<[^>]+>", " ");
    }
}
