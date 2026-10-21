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
