using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.DependencyInjection;

namespace CaissaDesk.Data;

/// <summary>
/// Shared HTTP helpers for the scouting / sync services: honest User-Agent, the named
/// chess-results client, per-call cookie handling and sanitizing of remote error bodies.
/// </summary>
public static class ScoutingHttp
{
    /// <summary>Named HttpClient used for chess-results.com (cookies and redirects handled per call).</summary>
    public const string ChessResultsClientName = "chess-results";

    private const int MaxRedirects = 10;

    /// <summary>
    /// Honest User-Agent identifying CaissaDesk. The leading "Mozilla/5.0 (compatible; ...)" token is the
    /// conventional crawler/bot form and keeps sites that only accept browser-like UA prefixes working.
    /// </summary>
    public static string UserAgent { get; } = BuildUserAgent();

    private static string BuildUserAgent()
    {
        var asm = typeof(ScoutingHttp).Assembly;
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "1.0.0";
        // Strip any "+commit" build metadata suffix; UA product tokens should be simple.
        int plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];
        return $"Mozilla/5.0 (compatible; CaissaDesk/{version}; +https://github.com/duckarp/CaissaDesk)";
    }

    /// <summary>
    /// Registers the named HttpClients used by the scouting services.
    /// The chess-results client disables the handler's shared cookie jar and automatic redirects so that
    /// cookies can be scoped to a single search (see <see cref="SendWithCookiesAsync"/>).
    /// </summary>
    public static IServiceCollection AddScoutingHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient(ChessResultsClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        return services;
    }

    /// <summary>
    /// Sends a request using a per-call <see cref="CookieContainer"/> and follows redirects manually
    /// (mirrors HttpClientHandler's AllowAutoRedirect semantics: 301/302/303 switch POST to GET,
    /// 307/308 keep method and body). The returned response's RequestMessage reflects the final URI.
    /// </summary>
    public static async Task<HttpResponseMessage> SendWithCookiesAsync(
        HttpClient client,
        HttpRequestMessage request,
        CookieContainer cookies,
        CancellationToken cancellationToken)
    {
        var current = request;
        byte[]? bufferedBody = null;
        string? bodyContentType = null;
        if (request.Content != null)
        {
            bufferedBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            bodyContentType = request.Content.Headers.ContentType?.ToString();
        }

        for (int hop = 0; ; hop++)
        {
            ApplyCookies(current, cookies);
            var response = await client.SendAsync(current, cancellationToken);
            response.RequestMessage ??= current;
            StoreCookies(response, current.RequestUri, cookies);

            if (!IsRedirect(response.StatusCode) || response.Headers.Location == null || hop >= MaxRedirects)
            {
                return response;
            }

            var location = response.Headers.Location;
            var nextUri = location.IsAbsoluteUri ? location : new Uri(current.RequestUri!, location);
            bool keepMethod = response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

            var next = new HttpRequestMessage(keepMethod ? current.Method : HttpMethod.Get, nextUri);
            foreach (var h in current.Headers)
            {
                if (string.Equals(h.Key, "Cookie", StringComparison.OrdinalIgnoreCase)) continue;
                next.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            if (keepMethod && bufferedBody != null)
            {
                next.Content = new ByteArrayContent(bufferedBody);
                if (bodyContentType != null) next.Content.Headers.TryAddWithoutValidation("Content-Type", bodyContentType);
            }

            response.Dispose();
            if (!ReferenceEquals(current, request)) current.Dispose();
            current = next;
        }
    }

    private static bool IsRedirect(HttpStatusCode code) => code is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static void ApplyCookies(HttpRequestMessage request, CookieContainer cookies)
    {
        request.Headers.Remove("Cookie");
        if (request.RequestUri == null) return;
        string header = cookies.GetCookieHeader(request.RequestUri);
        if (!string.IsNullOrEmpty(header))
        {
            request.Headers.TryAddWithoutValidation("Cookie", header);
        }
    }

    private static void StoreCookies(HttpResponseMessage response, Uri? uri, CookieContainer cookies)
    {
        if (uri == null || !response.Headers.TryGetValues("Set-Cookie", out var values)) return;
        foreach (var v in values)
        {
            try { cookies.SetCookies(uri, v); }
            catch (CookieException) { /* ignore malformed cookies, like HttpClientHandler does */ }
        }
    }

    private static readonly Regex ScriptStyleRegex = new(@"<(script|style)\b[\s\S]*?</\1\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TagRegex = new(@"<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Makes a raw server error body safe and short enough to show to the user:
    /// removes script/style blocks and HTML tags, decodes entities, strips control characters,
    /// collapses whitespace and truncates to <paramref name="maxLength"/> characters.
    /// </summary>
    public static string SanitizeServerError(string? body, int maxLength = 300)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        string text = ScriptStyleRegex.Replace(body, " ");
        text = TagRegex.Replace(text, " ");
        text = HttpUtility.HtmlDecode(text);
        text = new string(text.Select(c => char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c).ToArray());
        text = WhitespaceRegex.Replace(text, " ").Trim();

        if (text.Length > maxLength)
        {
            text = text[..maxLength].TrimEnd() + "…";
        }
        return text;
    }
}
