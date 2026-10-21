using System.Text.RegularExpressions;
using System.Web;
using Caissa.Shared.Models;

namespace CaissaDesk.Data;

public class NationalScoutingService : INationalScoutingService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public NationalScoutingService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<NationalPlayerCard?> LookupPlayerAsync(
        string? nationalId,
        string? fideId,
        string? lastName,
        string? firstName,
        string? federation,
        CancellationToken cancellationToken = default)
    {
        string? fedCode = FideCountryHelper.ToFideCode(federation);

        // Currently supports SVK (Slovak Chess Federation / SŠZ). Extensible to other national federations.
        if (string.IsNullOrEmpty(fedCode) || string.Equals(fedCode, "SVK", StringComparison.OrdinalIgnoreCase))
        {
            var svkCard = await LookupSlovakPlayerAsync(nationalId, fideId, lastName, firstName, cancellationToken);
            if (svkCard != null)
            {
                return svkCard;
            }
        }

        return null;
    }

    public async Task<NationalPlayerCard?> LookupSlovakPlayerAsync(
        string? nationalId,
        string? fideId,
        string? lastName,
        string? firstName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            string url = "https://www.chess.sk/index.php?str=clenovia&detail=10";

            // 1. If national ID (SSZ Id) is provided, query by SSZ Id
            if (!string.IsNullOrWhiteSpace(nationalId))
            {
                string cleanNatId = nationalId.Trim();
                var postData = new Dictionary<string, string>
                {
                    ["dgphp_filter_p_lokId"] = cleanNatId,
                    ["datagridphp_submit_p"] = "Hľadať"
                };

                var results = await PostSearchAsync(client, url, postData, cancellationToken);
                var match = results.FirstOrDefault(r => string.Equals(r.NationalId, cleanNatId, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            string cleanLast = (lastName ?? "").Trim();
            string cleanFirst = (firstName ?? "").Trim();
            string cleanFideId = !string.IsNullOrWhiteSpace(fideId) ? Regex.Replace(fideId.Trim(), @"[^\d]", "") : "";

            // 2. Query by "LastName, FirstName"
            if (!string.IsNullOrWhiteSpace(cleanLast) && !string.IsNullOrWhiteSpace(cleanFirst))
            {
                var postData = new Dictionary<string, string>
                {
                    ["dgphp_filter_p_hladaj"] = $"{cleanLast}, {cleanFirst}",
                    ["datagridphp_submit_p"] = "Hľadať"
                };

                var results = await PostSearchAsync(client, url, postData, cancellationToken);
                var match = SelectBestSvkMatch(results, cleanLast, cleanFirst, cleanFideId);
                if (match != null) return match;
            }

            // 3. Fall back to search by LastName alone
            if (!string.IsNullOrWhiteSpace(cleanLast))
            {
                var postData = new Dictionary<string, string>
                {
                    ["dgphp_filter_p_hladaj"] = cleanLast,
                    ["datagridphp_submit_p"] = "Hľadať"
                };

                var results = await PostSearchAsync(client, url, postData, cancellationToken);
                var match = SelectBestSvkMatch(results, cleanLast, cleanFirst, cleanFideId);
                if (match != null) return match;
            }

            // 4. Fall back to search by FirstName alone if no last name was given
            if (!string.IsNullOrWhiteSpace(cleanFirst) && string.IsNullOrWhiteSpace(cleanLast))
            {
                var postData = new Dictionary<string, string>
                {
                    ["dgphp_filter_p_hladaj"] = cleanFirst,
                    ["datagridphp_submit_p"] = "Hľadať"
                };

                var results = await PostSearchAsync(client, url, postData, cancellationToken);
                var match = SelectBestSvkMatch(results, cleanLast, cleanFirst, cleanFideId);
                if (match != null) return match;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NationalScoutingService] Slovak lookup failed: {ex.Message}");
        }

        return null;
    }

    private async Task<List<NationalPlayerCard>> PostSearchAsync(
        HttpClient client,
        string url,
        Dictionary<string, string> postData,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(postData)
        };
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        string html = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSlovakSearchResultsHtml(html);
    }

    public static List<NationalPlayerCard> ParseSlovakSearchResultsHtml(string html)
    {
        var list = new List<NationalPlayerCard>();
        if (string.IsNullOrWhiteSpace(html)) return list;

        // Match each row starting from datagridphp_item until the next row or end of HTML
        var rowMatches = Regex.Matches(html, @"<div\s+class=""[^""]*datagridphp_item[^""]*""[^>]*>([\s\S]*?)(?=<div\s+class=""[^""]*datagridphp_item|\z)", RegexOptions.IgnoreCase);

        foreach (Match rm in rowMatches)
        {
            string rowHtml = rm.Groups[1].Value;

            string col1 = ExtractColumn(rowHtml, 1); // SSZ Id
            string col2 = ExtractColumn(rowHtml, 2); // Priezvisko, Meno
            string col3 = ExtractColumn(rowHtml, 3); // Klub
            string col4 = ExtractColumn(rowHtml, 4); // Rating / LOK
            string col5 = ExtractColumn(rowHtml, 5); // FideId

            if (string.IsNullOrWhiteSpace(col1) && string.IsNullOrWhiteSpace(col2)) continue;

            string cleanName = CleanTitlePrefix(col2);

            int? rating = null;
            if (int.TryParse(col4.Trim(), out int rVal) && rVal > 0)
            {
                rating = rVal;
            }

            string? cleanFideId = !string.IsNullOrWhiteSpace(col5) && col5.Trim().All(char.IsDigit) ? col5.Trim() : null;

            list.Add(new NationalPlayerCard
            {
                NationalId = col1.Trim(),
                FullName = cleanName.Trim(),
                Club = string.IsNullOrWhiteSpace(col3) ? null : col3.Trim(),
                NationalRating = rating,
                FideId = cleanFideId,
                Federation = "SVK"
            });
        }

        return list;
    }

    private static NationalPlayerCard? SelectBestSvkMatch(
        List<NationalPlayerCard> results,
        string? targetLastName,
        string? targetFirstName,
        string? targetFideId)
    {
        if (results == null || results.Count == 0) return null;

        // 1. If exact FIDE ID match is found, return it immediately
        if (!string.IsNullOrEmpty(targetFideId))
        {
            var fideMatch = results.FirstOrDefault(r => string.Equals(r.FideId, targetFideId, StringComparison.OrdinalIgnoreCase));
            if (fideMatch != null) return fideMatch;
        }

        string tLast = Player.RemoveDiacritics(targetLastName ?? "").Trim().ToLowerInvariant();
        string tFirst = Player.RemoveDiacritics(targetFirstName ?? "").Trim().ToLowerInvariant();

        NationalPlayerCard? bestResult = null;
        int bestScore = -1;

        foreach (var r in results)
        {
            var (cLastRaw, cFirstRaw) = Player.SplitFullName(r.FullName);
            string cLast = Player.RemoveDiacritics(cLastRaw).Trim().ToLowerInvariant();
            string cFirst = Player.RemoveDiacritics(cFirstRaw).Trim().ToLowerInvariant();

            int score = 0;

            // Last name scoring
            if (!string.IsNullOrEmpty(tLast))
            {
                if (cLast == tLast)
                {
                    score += 100;
                }
                else if (cLast.StartsWith(tLast) || tLast.StartsWith(cLast))
                {
                    score += 60;
                }
                else
                {
                    // Fatal mismatch
                    continue;
                }
            }
            else
            {
                score += 10;
            }

            // First name scoring
            if (!string.IsNullOrEmpty(tFirst))
            {
                if (cFirst == tFirst)
                {
                    score += 60;
                }
                else if (cFirst.StartsWith(tFirst) || tFirst.StartsWith(cFirst))
                {
                    score += 40;
                }
                else if (cFirst.Contains(tFirst) || tFirst.Contains(cFirst))
                {
                    score += 25;
                }
                else
                {
                    score -= 30;
                }
            }

            if (r.NationalRating.HasValue && r.NationalRating.Value > 0)
            {
                score += 5;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestResult = r;
            }
        }

        return bestScore > 0 ? bestResult : null;
    }

    private static string ExtractColumn(string rowHtml, int colIndex)
    {
        var m = Regex.Match(rowHtml, $@"<div[^>]*class=""[^""]*datagridphp_column_class_{colIndex}[^""]*""[^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
        if (!m.Success) return string.Empty;
        return StripHtml(m.Groups[1].Value).Trim();
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        string stripped = Regex.Replace(input, @"<[^>]+>", " ");
        return HttpUtility.HtmlDecode(stripped).Trim();
    }

    private static string CleanTitlePrefix(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
        return Regex.Replace(fullName, @"^(?:GM|IM|FM|CM|WGM|WIM|WFM|WCM)\s+", "", RegexOptions.IgnoreCase).Trim();
    }
}
