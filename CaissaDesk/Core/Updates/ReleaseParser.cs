using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CaissaDesk.Core.Updates;

/// <summary>Pure helpers for the GitHub release metadata: parsing, asset selection, checksums, URL trust.</summary>
public static partial class ReleaseParser
{
    public const string DefaultRepository = "duckarp/CaissaDesk";
    public const string ChecksumsAssetName = "SHA256SUMS";

    /// <summary>Hosts a release asset may be served from (the github.com URL redirects to one of the CDN hosts).</summary>
    private static readonly string[] TrustedAssetHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    ];

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepositoryRegex();

    /// <summary>Normalizes "owner/repo" (also accepts a pasted https://github.com/owner/repo URL); null when invalid.</summary>
    public static string? NormalizeRepository(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string s = value.Trim();

        const string prefix = "https://github.com/";
        if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) s = s[prefix.Length..];
        s = s.TrimEnd('/');
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s[..^4];

        if (!RepositoryRegex().IsMatch(s)) return null;
        string repo = s.Split('/')[1];
        return repo is "." or ".." ? null : s;
    }

    /// <summary>Expected release asset for the OS/architecture, or null when the workflow publishes none.</summary>
    public static UpdatePlatform? GetPlatform(OSPlatform os, Architecture arch)
    {
        if (os == OSPlatform.Windows && arch is Architecture.X64 or Architecture.Arm64)
        {
            // Windows on Arm runs the x64 build under emulation
            return new UpdatePlatform("CaissaDesk-win-x64.zip", "CaissaDesk.exe", ArchiveKind.Zip);
        }
        if (os == OSPlatform.Linux && arch == Architecture.X64)
        {
            return new UpdatePlatform("CaissaDesk-linux-x64.tar.gz", "CaissaDesk", ArchiveKind.TarGz);
        }
        return null;
    }

    public static UpdatePlatform? GetCurrentPlatform()
    {
        var os = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsLinux() ? OSPlatform.Linux
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OSPlatform.FreeBSD;
        return GetPlatform(os, RuntimeInformation.OSArchitecture);
    }

    /// <summary>Exact-name match only: no fuzzy scoring, so an unexpected file is never installed.</summary>
    public static ReleaseAsset? FindAsset(ReleaseInfo release, string name) =>
        release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));

    /// <summary>Parses one release object; null when it is a draft or its tag is not a semantic version.</summary>
    public static ReleaseInfo? ParseRelease(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (GetBool(element, "draft")) return null;

        string tag = GetString(element, "tag_name");
        if (!SemVersion.TryParse(tag, out var version)) return null;

        string title = GetString(element, "name");
        DateTimeOffset? published = element.TryGetProperty("published_at", out var p) &&
                                    p.ValueKind == JsonValueKind.String && p.TryGetDateTimeOffset(out var dto)
            ? dto
            : null;

        var assets = new List<ReleaseAsset>();
        if (element.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in arr.EnumerateArray())
            {
                string name = GetString(a, "name");
                string url = GetString(a, "browser_download_url");
                long size = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out long sz) ? sz : 0;
                if (name.Length > 0 && url.Length > 0)
                {
                    assets.Add(new ReleaseAsset(name, url, size));
                }
            }
        }

        return new ReleaseInfo(
            tag,
            version,
            string.IsNullOrWhiteSpace(title) ? tag : title.Trim(),
            GetString(element, "body"),
            published,
            GetBool(element, "prerelease"),
            assets);
    }

    /// <summary>
    /// Picks the release to offer from either a single /releases/latest object or a /releases array.
    /// From an array the highest version wins (not the first item: GitHub sorts by creation date);
    /// pre-releases are only considered when <paramref name="includePrereleases"/> is set.
    /// </summary>
    public static ReleaseInfo? SelectRelease(JsonElement root, bool includePrereleases)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            var single = ParseRelease(root);
            return single != null && (includePrereleases || !single.IsPrerelease) ? single : null;
        }
        if (root.ValueKind != JsonValueKind.Array) return null;

        ReleaseInfo? best = null;
        foreach (var item in root.EnumerateArray())
        {
            var release = ParseRelease(item);
            if (release == null) continue;
            if (!includePrereleases && (release.IsPrerelease || release.Version.IsPreRelease)) continue;
            if (best == null || release.Version.CompareTo(best.Version) > 0) best = release;
        }
        return best;
    }

    /// <summary>Only https downloads from GitHub's release hosts are accepted (no userinfo, default port).</summary>
    public static bool IsTrustedAssetUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsTrustedAssetUri(uri);

    public static bool IsTrustedAssetUri(Uri? uri) =>
        uri != null &&
        uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        TrustedAssetHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses "sha256sum" output ("&lt;64 hex&gt;  name" or "&lt;64 hex&gt; *name"). Names listed twice with
    /// different hashes are dropped (ambiguous: treated as missing, so verification fails closed).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseSha256Sums(string? content)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(content)) return result;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            int space = line.IndexOfAny([' ', '\t']);
            if (space != 64) continue;

            string hash = line[..64];
            if (!IsSha256Hex(hash)) continue;

            string name = line[64..].TrimStart(' ', '\t');
            if (name.StartsWith('*')) name = name[1..];
            if (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
            if (name.Length == 0) continue;

            hash = hash.ToLowerInvariant();
            if (result.TryGetValue(name, out var existing) && existing != hash)
            {
                ambiguous.Add(name);
            }
            result[name] = hash;
        }

        foreach (string name in ambiguous) result.Remove(name);
        return result;
    }

    public static bool IsSha256Hex(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public static string BuildReleasePageUrl(string repository, string tag) =>
        $"https://github.com/{repository}/releases/tag/{Uri.EscapeDataString(tag)}";

    public static string BuildReleasesListUrl(string repository) => $"https://github.com/{repository}/releases";

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
