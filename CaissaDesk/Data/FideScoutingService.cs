using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Caissa.Shared.Models;

namespace CaissaDesk.Data;

public class FideScoutingService : IFideScoutingService
{
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly Regex NameRegex = new(@"<h1\s+class=""player-title"">([^<]+)</h1>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FideIdRegex = new(@"<p\s+class=""profile-info-id\s*"">([^<]+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TitleRegex = new(@"<div\s+class=""profile-info-title\s*"">\s*<p>([^<]+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CountryRegex = new(@"<div\s+class=""profile-info-country\s*"">(?:[\s\S]*?<img\s+src=""([^""]+)""[^>]*>)?\s*([^<\r\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BirthYearRegex = new(@"<p\s+class=""profile-info-byear\s*"">(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GenderRegex = new(@"<p\s+class=""profile-info-sex\s*"">([^<]+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StdBlockRegex = new(@"<div\s+class=""profile-standart\s+profile-game\s*"">([\s\S]*?)</div>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RapidBlockRegex = new(@"<div\s+class=""profile-rapid\s+profile-game\s*"">([\s\S]*?)</div>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BlitzBlockRegex = new(@"<div\s+class=""profile-blitz\s+profile-game\s*"">([\s\S]*?)</div>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RatingNumRegex = new(@"<p>\s*(\d+)\s*</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WorldRankActiveRegex = new(@"<h5>World Rank</h5>[\s\S]*?<h6>Active players</h6>\s*<p>(\d+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WorldRankAllRegex = new(@"<h5>World Rank</h5>[\s\S]*?<h6>All players</h6>\s*<p>(\d+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NationalRankRegex = new(@"<h5>National Rank\s*([A-Z]{3})?</h5>[\s\S]*?<h6>Active players</h6>\s*<p>(\d+)</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public FideScoutingService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
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
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) return null;

            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParsePlayerCardHtml(cleanId, html);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FideScoutingService] Error fetching FIDE profile for {fideId}: {ex.Message}");
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
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Referrer = new Uri("https://ratings.fide.com/");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");

            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return results;

            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseSearchResultsHtml(html, limit);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FideScoutingService] Error searching FIDE players for '{query}': {ex.Message}");
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

        // Match table rows in #table_results
        var rowMatches = Regex.Matches(html, @"<tr[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase);
        foreach (Match rm in rowMatches)
        {
            string rowContent = rm.Groups[1].Value;
            if (rowContent.Contains("<th", StringComparison.OrdinalIgnoreCase)) continue;

            var tdMatches = Regex.Matches(rowContent, @"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
            if (tdMatches.Count < 5) continue;

            string id = StripHtml(tdMatches[0].Groups[1].Value).Trim();
            string name = StripHtml(tdMatches[1].Groups[1].Value).Trim();
            string title = StripHtml(tdMatches[2].Groups[1].Value).Trim();
            // td[3] is Trainer Title
            string fed = StripHtml(tdMatches[4].Groups[1].Value).Trim();

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;

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

    private static int? ExtractRatingFromBlock(Regex blockRegex, string html)
    {
        var blockMatch = blockRegex.Match(html);
        if (blockMatch.Success)
        {
            var numMatch = RatingNumRegex.Match(blockMatch.Groups[1].Value);
            if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int elo))
            {
                return elo;
            }
        }
        return null;
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return Regex.Replace(input, @"<[^>]+>", " ");
    }
}
