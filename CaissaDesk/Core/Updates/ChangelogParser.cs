using System.Text.RegularExpressions;

namespace CaissaDesk.Core.Updates;

/// <summary>
/// Turns commit messages from the GitHub compare API into a grouped changelog. Understands conventional
/// commits ("feat(ui): ...") as well as CaissaDesk's plain imperative style ("Fix ...", "Harden ...",
/// "RoundGenerationService: report ...") by mapping the leading verb to a category.
/// </summary>
public static partial class ChangelogParser
{
    /// <summary>Display order of the groups in the update dialog.</summary>
    public static readonly IReadOnlyList<ChangelogCategory> CategoryOrder =
    [
        ChangelogCategory.Feature,
        ChangelogCategory.Fix,
        ChangelogCategory.Performance,
        ChangelogCategory.Update,
        ChangelogCategory.Refactor,
        ChangelogCategory.Docs,
        ChangelogCategory.Tests,
        ChangelogCategory.Build
    ];

    private static readonly Dictionary<string, ChangelogCategory> ConventionalTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["feat"] = ChangelogCategory.Feature,
        ["feature"] = ChangelogCategory.Feature,
        ["fix"] = ChangelogCategory.Fix,
        ["bugfix"] = ChangelogCategory.Fix,
        ["hotfix"] = ChangelogCategory.Fix,
        ["perf"] = ChangelogCategory.Performance,
        ["refactor"] = ChangelogCategory.Refactor,
        ["docs"] = ChangelogCategory.Docs,
        ["doc"] = ChangelogCategory.Docs,
        ["i18n"] = ChangelogCategory.Docs,
        ["test"] = ChangelogCategory.Tests,
        ["tests"] = ChangelogCategory.Tests,
        ["build"] = ChangelogCategory.Build,
        ["ci"] = ChangelogCategory.Build,
        ["chore"] = ChangelogCategory.Build,
        ["style"] = ChangelogCategory.Update,
        ["ui"] = ChangelogCategory.Update
    };

    private static readonly Dictionary<string, ChangelogCategory> Verbs = BuildVerbMap();

    private static Dictionary<string, ChangelogCategory> BuildVerbMap()
    {
        var map = new Dictionary<string, ChangelogCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(ChangelogCategory category, params string[] verbs)
        {
            foreach (string v in verbs) map[v] = category;
        }

        Add(ChangelogCategory.Feature, "Add", "Implement", "Introduce", "Support", "Enable", "Allow", "Create", "New");
        Add(ChangelogCategory.Fix, "Fix", "Correct", "Resolve", "Repair", "Prevent", "Avoid", "Restore");
        Add(ChangelogCategory.Performance, "Optimize", "Optimise", "Speed", "Cache", "Accelerate");
        Add(ChangelogCategory.Refactor, "Remove", "Refactor", "Extract", "Split", "Centralize", "Centralise", "Rename",
            "Move", "Simplify", "Replace", "Consolidate", "Clean", "Cleanup", "Drop", "Delete", "Deduplicate", "Unify");
        Add(ChangelogCategory.Docs, "Localize", "Localise", "Translate", "Document", "Docs", "Doc");
        Add(ChangelogCategory.Tests, "Test", "Tests");
        Add(ChangelogCategory.Update, "Harden", "Align", "Improve", "Make", "Update", "Polish", "Tweak", "Adjust",
            "Upgrade", "Use", "Show", "Guard", "Change", "Bump");
        return map;
    }

    [GeneratedRegex(@"^(?<type>[A-Za-z][A-Za-z0-9]*)(?:\((?<scope>[^)]{1,40})\))?!?:\s*(?<desc>\S.*)$")]
    private static partial Regex ConventionalRegex();

    // "Manual pairing modal: reset ..." / "CI: ...": a short free-text scope before a colon
    [GeneratedRegex(@"^(?<scope>[A-Za-z][A-Za-z0-9 ._/&-]{0,39}):\s+(?<desc>\S.*)$")]
    private static partial Regex ScopePrefixRegex();

    [GeneratedRegex(@"^Merge (pull request|branch|remote-tracking branch|tag|commit|'|"")", RegexOptions.IgnoreCase)]
    private static partial Regex MergeRegex();

    [GeneratedRegex(@"^(?:(?:chore|build|release)(?:\([^)]*\))?!?:\s*)?(?:release|version|bump(?:ed)?(?:\s+(?:the\s+)?(?:app\s+)?version)?(?:\s+to)?|set\s+version(?:\s+to)?|prepare\s+release)?\s*v?\d+\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.-]+)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionBumpRegex();

    [GeneratedRegex(@"^(?:chore(?:\([^)]*\))?:\s*)?(?:bump(?:ed)?\s+(?:the\s+)?(?:app\s+)?version|version\s+bump)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionBumpPhraseRegex();

    public static string FirstLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "";
        int nl = message.IndexOfAny(['\r', '\n']);
        return (nl >= 0 ? message[..nl] : message).Trim();
    }

    /// <summary>Merge commits, version bumps and empty messages are not shown in the changelog.</summary>
    public static bool IsIgnoredCommit(string? message)
    {
        string line = FirstLine(message);
        if (line.Length == 0) return true;
        if (MergeRegex().IsMatch(line)) return true;
        if (VersionBumpRegex().IsMatch(line)) return true;
        if (VersionBumpPhraseRegex().IsMatch(line)) return true;
        return false;
    }

    public static ChangelogEntry? ParseCommit(string? message, string? sha = null)
    {
        if (IsIgnoredCommit(message)) return null;

        string line = FirstLine(message);
        string shortSha = sha is { Length: >= 7 } ? sha[..7] : sha ?? "";

        var conventional = ConventionalRegex().Match(line);
        if (conventional.Success && ConventionalTypes.TryGetValue(conventional.Groups["type"].Value, out var type))
        {
            string scope = conventional.Groups["scope"].Success ? conventional.Groups["scope"].Value.Trim() : "";
            return new ChangelogEntry(type, Capitalize(scope), CleanDescription(conventional.Groups["desc"].Value), shortSha);
        }

        // Plain imperative message ("Fix crash: ..."): the verb decides even if a colon follows later
        if (TryCategoryFromVerb(line, out var verbCategory))
        {
            return new ChangelogEntry(verbCategory, "", CleanDescription(line), shortSha);
        }

        // "RoundGenerationService: report failure reasons" / "Manual pairing modal: reset state": scope prefix
        var scoped = ScopePrefixRegex().Match(line);
        if (scoped.Success)
        {
            string desc = scoped.Groups["desc"].Value;
            return new ChangelogEntry(CategoryFromVerb(desc), scoped.Groups["scope"].Value.Trim(), CleanDescription(desc), shortSha);
        }

        return new ChangelogEntry(ChangelogCategory.Update, "", CleanDescription(line), shortSha);
    }

    /// <summary>Category from the leading verb; also accepts third-person/past forms ("Adds", "Fixed").</summary>
    public static ChangelogCategory CategoryFromVerb(string text) =>
        TryCategoryFromVerb(text, out var category) ? category : ChangelogCategory.Update;

    private static bool TryCategoryFromVerb(string text, out ChangelogCategory category)
    {
        category = ChangelogCategory.Update;
        string word = new string(text.TrimStart().TakeWhile(char.IsLetter).ToArray());
        if (word.Length == 0) return false;

        if (Verbs.TryGetValue(word, out category)) return true;

        foreach (string suffix in (string[])["es", "ed", "s", "d"])
        {
            if (word.Length > suffix.Length + 2 && word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                Verbs.TryGetValue(word[..^suffix.Length], out category))
            {
                return true;
            }
        }
        category = ChangelogCategory.Update;
        return false;
    }

    /// <summary>Groups entries in <see cref="CategoryOrder"/>, keeping the given order inside each group.</summary>
    public static IReadOnlyList<ChangelogGroup> Group(IEnumerable<ChangelogEntry> entries)
    {
        var list = entries.ToList();
        return CategoryOrder
            .Select(c => new ChangelogGroup(c, list.Where(e => e.Category == c).ToList()))
            .Where(g => g.Entries.Count > 0)
            .ToList();
    }

    /// <summary>Builds the grouped changelog from (message, sha) pairs, newest first, dropping duplicates.</summary>
    public static IReadOnlyList<ChangelogGroup> Build(IEnumerable<(string Message, string Sha)> commitsOldestFirst, int maxEntries = 100)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ChangelogEntry>();
        foreach (var (message, sha) in commitsOldestFirst.Reverse())
        {
            var entry = ParseCommit(message, sha);
            if (entry == null || !seen.Add(entry.Description)) continue;
            entries.Add(entry);
            if (entries.Count >= maxEntries) break;
        }
        return Group(entries);
    }

    /// <summary>
    /// Release body for display: drops GitHub's "Full Changelog" and bare compare-link lines, collapses
    /// runs of blank lines and caps the length. Rendered as plain text by the UI (never as HTML).
    /// </summary>
    public static string SanitizeReleaseNotes(string? notes, int maxLength = 8000)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";

        var kept = new List<string>();
        foreach (string raw in notes.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string trimmed = raw.Trim();
            string bare = trimmed.Replace("*", "").Trim();
            if (bare.StartsWith("Full Changelog", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("/compare/", StringComparison.Ordinal)) continue;

            if (trimmed.Length == 0 && (kept.Count == 0 || kept[^1].Length == 0)) continue;
            kept.Add(raw.TrimEnd());
        }

        string text = string.Join("\n", kept).Trim();
        text = new string(text.Where(c => c == '\n' || c == '\t' || !char.IsControl(c)).ToArray());
        return text.Length > maxLength ? text[..maxLength].TrimEnd() + "…" : text;
    }

    private static string CleanDescription(string desc)
    {
        string d = desc.Trim().TrimEnd('.');
        return Capitalize(d);
    }

    private static string Capitalize(string s) =>
        s.Length > 0 && char.IsLower(s[0]) ? char.ToUpperInvariant(s[0]) + s[1..] : s;
}
