using System.Globalization;

namespace CaissaDesk.Core.Updates;

/// <summary>
/// Minimal Semantic Versioning 2.0 value: MAJOR.MINOR.PATCH with optional pre-release identifiers.
/// Accepts a leading "v"/"V" and ignores "+build" metadata. Missing minor/patch parts count as 0
/// ("1.2" == "1.2.0"). Precedence follows semver.org §11, so 1.2.0-beta &lt; 1.2.0-rc.1 &lt; 1.2.0.
/// </summary>
public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Dot-separated pre-release identifiers; empty for a release version.</summary>
    public IReadOnlyList<string> PreRelease { get; }

    public bool IsPreRelease => PreRelease.Count > 0;

    private SemVersion(int major, int minor, int patch, IReadOnlyList<string> preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public static bool TryParse(string? text, out SemVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        string core = s;
        string? pre = null;
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            core = s[..dash];
            pre = s[(dash + 1)..];
        }

        string[] parts = core.Split('.');
        if (parts.Length is < 1 or > 3) return false;

        var numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit) ||
                !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        var identifiers = Array.Empty<string>();
        if (pre != null)
        {
            identifiers = pre.Split('.');
            if (identifiers.Any(id => id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
            {
                return false;
            }
        }

        version = new SemVersion(numbers[0], numbers[1], numbers[2], identifiers);
        return true;
    }

    public static SemVersion? ParseOrNull(string? text) => TryParse(text, out var v) ? v : null;

    /// <summary>True when <paramref name="candidate"/> parses and has higher precedence than <paramref name="current"/>.
    /// An unparsable current version never reports an update (so odd local builds are not nagged).</summary>
    public static bool IsNewer(string? candidate, string? current) =>
        TryParse(candidate, out var c) && TryParse(current, out var cur) && c.CompareTo(cur) > 0;

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;

        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A version without pre-release identifiers has higher precedence than one with them
        if (!IsPreRelease && !other.IsPreRelease) return 0;
        if (!IsPreRelease) return 1;
        if (!other.IsPreRelease) return -1;

        int n = Math.Min(PreRelease.Count, other.PreRelease.Count);
        for (int i = 0; i < n; i++)
        {
            c = CompareIdentifier(PreRelease[i], other.PreRelease[i]);
            if (c != 0) return c;
        }
        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    private static int CompareIdentifier(string a, string b)
    {
        bool aNum = a.All(char.IsAsciiDigit);
        bool bNum = b.All(char.IsAsciiDigit);
        if (aNum && bNum)
        {
            // Compare numerically without overflow: strip leading zeros, then length, then ordinal
            string ta = a.TrimStart('0'), tb = b.TrimStart('0');
            int len = ta.Length.CompareTo(tb.Length);
            return len != 0 ? len : string.CompareOrdinal(ta, tb);
        }
        if (aNum) return -1; // numeric identifiers have lower precedence than alphanumeric ones
        if (bNum) return 1;
        return string.CompareOrdinal(a, b);
    }

    public bool Equals(SemVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', PreRelease));

    public override string ToString() =>
        IsPreRelease ? $"{Major}.{Minor}.{Patch}-{string.Join('.', PreRelease)}" : $"{Major}.{Minor}.{Patch}";
}
