using Caissa.Shared.Tiebreak;
using CaissaDesk.Localization;
using System.Text.RegularExpressions;

namespace CaissaDesk.Tests;

/// <summary>
/// Scans the UI sources of the CaissaDesk project so that references which only fail at runtime
/// (icon names, localization keys) are caught by the test suite instead.
/// </summary>
public partial class UiSourceConsistencyTests
{
    private static readonly Lazy<string> AppSourceDirectory = new(FindAppSourceDirectory);

    private static string FindAppSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CaissaDesk.slnx")))
            {
                return Path.Combine(dir.FullName, "CaissaDesk");
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate CaissaDesk.slnx above the test output directory.");
    }

    private static IEnumerable<string> SourceFiles(params string[] extensions)
    {
        string root = AppSourceDirectory.Value;
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f =>
            {
                string relative = Path.GetRelativePath(root, f).Replace('\\', '/');
                return !relative.StartsWith("bin/", StringComparison.Ordinal) && !relative.StartsWith("obj/", StringComparison.Ordinal);
            });
    }

    // ---------------------------------------------------------------- localization keys

    private static Dictionary<string, string> Strings(string method) =>
        (Dictionary<string, string>)typeof(LocalizationService)
            .GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null)!;

    private static Dictionary<string, string> English => Strings("GetEnglishStrings");
    private static Dictionary<string, string> Slovak => Strings("GetSlovakStrings");

    // Loc["Key"...] / loc["Key"...]; interpolated keys (Loc[$"..."]) are not literal and are skipped
    [GeneratedRegex(@"\b[Ll]oc\[\s*""([^""]+)""")]
    private static partial Regex LiteralKeyRegex();

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>Key prefixes built at runtime from enum names (see LocalizationExtensions.TiebreakLabel).</summary>
    private static readonly string[] DynamicKeyPrefixes = [LocalizationExtensions.TiebreakKeyPrefix];

    private static string LocalizationSourcePath =>
        Path.Combine(AppSourceDirectory.Value, "Localization", "LocalizationService.cs");

    private static IEnumerable<string> UiAndServiceSources() =>
        SourceFiles(".razor", ".cs").Where(f => !string.Equals(f, LocalizationSourcePath, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void EnglishAndSlovak_HaveIdenticalKeySets_AndNoEmptyValues()
    {
        var en = English;
        var sk = Slovak;

        Assert.Empty(en.Keys.Except(sk.Keys).OrderBy(k => k));
        Assert.Empty(sk.Keys.Except(en.Keys).OrderBy(k => k));
        Assert.Empty(en.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
        Assert.Empty(sk.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
    }

    [Fact]
    public void EnglishAndSlovak_UseTheSameFormatPlaceholders()
    {
        var en = English;
        var sk = Slovak;

        var mismatched = en.Keys
            .Where(sk.ContainsKey)
            .Where(k =>
            {
                var enArgs = PlaceholderRegex().Matches(en[k]).Select(m => m.Groups[1].Value).ToHashSet();
                var skArgs = PlaceholderRegex().Matches(sk[k]).Select(m => m.Groups[1].Value).ToHashSet();
                return !enArgs.SetEquals(skArgs);
            })
            .OrderBy(k => k)
            .ToList();

        Assert.True(mismatched.Count == 0, "Placeholder mismatch between EN and SK: " + string.Join(", ", mismatched));
    }

    [Fact]
    public void EveryLiteralLocalizationKeyInTheSources_Exists()
    {
        var en = English;
        var missing = new SortedSet<string>();
        int found = 0;

        foreach (string file in UiAndServiceSources())
        {
            foreach (Match m in LiteralKeyRegex().Matches(File.ReadAllText(file)))
            {
                found++;
                string key = m.Groups[1].Value;
                if (!en.ContainsKey(key))
                {
                    missing.Add($"{key} ({Path.GetFileName(file)})");
                }
            }
        }

        Assert.True(found > 200, $"Only {found} Loc[...] usages found: is the source scan pointing at the right folder?");
        Assert.True(missing.Count == 0, "Unknown localization keys: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryLocalizationKey_IsUsedSomewhere()
    {
        string corpus = string.Join("\n", UiAndServiceSources().Select(File.ReadAllText));

        var unused = English.Keys
            .Where(k => !DynamicKeyPrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
            .Where(k => !corpus.Contains($"\"{k}\"", StringComparison.Ordinal))
            .OrderBy(k => k)
            .ToList();

        Assert.True(unused.Count == 0, "Localization keys never referenced in the sources: " + string.Join(", ", unused));
    }

    [Fact]
    public void DynamicTiebreakKeys_CoverEveryTiebreakType()
    {
        var en = English;
        var missing = Enum.GetValues<TiebreakType>()
            .Select(t => LocalizationExtensions.TiebreakKeyPrefix + t)
            .Where(k => !en.ContainsKey(k))
            .ToList();

        Assert.Empty(missing);
    }

    // ---------------------------------------------------------------- icons

    [GeneratedRegex(@"case ""([a-z0-9-]+)"":")]
    private static partial Regex IconCaseRegex();

    // Literal icon names: Name="trash" on AppIcon, plus string literals inside a Name="@(...)" expression
    [GeneratedRegex(@"<AppIcon[^>]*?\bName=""(?<value>(?:@\((?:[^()]|\([^()]*\))*\))|[^""@][^""]*)""")]
    private static partial Regex AppIconNameRegex();

    [GeneratedRegex(@"""([a-z][a-z0-9-]*)""")]
    private static partial Regex QuotedLowercaseRegex();

    // Icon names handed over indirectly: icon: "trash", HeaderIcon="trash", Icon => "trophy"
    [GeneratedRegex(@"(?:\bicon:\s*(?:[^,;)]*?\?\s*)?|(?:Header|Confirm)Icon=|Icon\s*=>\s*)""([a-z0-9-]+)""(?:\s*:\s*""([a-z0-9-]+)"")?")]
    private static partial Regex IndirectIconRegex();

    [Fact]
    public void EveryAppIconNameUsedInTheUi_HasAGlyph()
    {
        string iconSource = File.ReadAllText(Path.Combine(AppSourceDirectory.Value, "Components", "Icons", "AppIcon.razor"));
        var known = IconCaseRegex().Matches(iconSource).Select(m => m.Groups[1].Value).ToHashSet();
        Assert.NotEmpty(known);

        var used = new HashSet<string>();
        foreach (string file in SourceFiles(".razor", ".cs"))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in AppIconNameRegex().Matches(text))
            {
                string value = m.Groups["value"].Value;
                if (value.StartsWith("@(", StringComparison.Ordinal))
                {
                    foreach (Match q in QuotedLowercaseRegex().Matches(value))
                    {
                        used.Add(q.Groups[1].Value);
                    }
                }
                else
                {
                    used.Add(value);
                }
            }
            foreach (Match m in IndirectIconRegex().Matches(text))
            {
                used.Add(m.Groups[1].Value);
                if (m.Groups[2].Success)
                {
                    used.Add(m.Groups[2].Value);
                }
            }
        }

        Assert.Contains("trash", used);
        var missing = used.Where(n => !known.Contains(n)).OrderBy(n => n).ToList();
        Assert.True(missing.Count == 0, "AppIcon has no glyph for: " + string.Join(", ", missing));
    }
}
