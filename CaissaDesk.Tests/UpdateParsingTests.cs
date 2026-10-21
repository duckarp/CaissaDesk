using System.Runtime.InteropServices;
using System.Text.Json;
using CaissaDesk.Core.Updates;

namespace CaissaDesk.Tests;

/// <summary>SemVer, GitHub release JSON, asset selection, SHA256SUMS, changelog and notes parsing.</summary>
public class UpdateParsingTests
{
    // ---------------------------------------------------------------- SemVer

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V2.0.0", "2.0.0")]
    [InlineData("1.0.0+build.123", "1.0.0")]
    [InlineData("v2.1.0-beta.1", "2.1.0-beta.1")]
    [InlineData("v1.0.0-rc1+2026", "1.0.0-rc1")]
    [InlineData("  v1.5  ", "1.5.0")]
    [InlineData("3", "3.0.0")]
    public void SemVersion_Parses(string input, string expected)
    {
        Assert.True(SemVersion.TryParse(input, out var v));
        Assert.Equal(expected, v.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2.3.4")]
    [InlineData("1..2")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("-1.0.0")]
    [InlineData("99999999999.0.0")]
    public void SemVersion_RejectsInvalid(string? input)
    {
        Assert.False(SemVersion.TryParse(input, out _));
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("1.0.0+other", "1.0.0", false)]
    [InlineData("0.9.9", "1.0.0", false)]
    // pre-release ordering (semver.org §11)
    [InlineData("1.2.0", "1.2.0-beta", true)]
    [InlineData("1.2.0-beta", "1.2.0", false)]
    [InlineData("1.2.0-beta", "1.1.9", true)]
    [InlineData("1.2.0-rc.1", "1.2.0-beta.11", true)]
    [InlineData("1.2.0-beta.11", "1.2.0-beta.2", true)]
    [InlineData("1.2.0-beta.2", "1.2.0-beta", true)]
    [InlineData("1.2.0-beta", "1.2.0-alpha.9", true)]
    [InlineData("1.2.0-alpha.beta", "1.2.0-alpha.1", true)]
    [InlineData("1.0.0", "0.0.0-ci.42", true)]
    // unparsable inputs never report an update
    [InlineData("garbage", "1.0.0", false)]
    [InlineData("9.9.9", "dev", false)]
    public void SemVersion_IsNewer(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, SemVersion.IsNewer(candidate, current));
    }

    [Fact]
    public void SemVersion_Equality_IgnoresBuildMetadata()
    {
        Assert.Equal(SemVersion.ParseOrNull("1.2.3+a"), SemVersion.ParseOrNull("v1.2.3+b"));
        Assert.Equal(0, SemVersion.ParseOrNull("1.2")!.CompareTo(SemVersion.ParseOrNull("1.2.0")));
    }

    // ---------------------------------------------------------------- release JSON

    internal static string ReleaseJson(string tag, bool prerelease = false, bool draft = false, string body = "", string? assetsJson = null) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "CaissaDesk {{tag}}",
          "body": {{JsonSerializer.Serialize(body)}},
          "html_url": "https://evil.example/should-not-be-used",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "published_at": "2026-10-01T12:00:00Z",
          "assets": {{assetsJson ?? StandardAssets(tag)}}
        }
        """;

    internal static string StandardAssets(string tag, long linuxSize = 1000, long winSize = 2000) => $$"""
        [
          { "name": "CaissaDesk-linux-x64.tar.gz", "size": {{linuxSize}}, "browser_download_url": "https://github.com/duckarp/CaissaDesk/releases/download/{{tag}}/CaissaDesk-linux-x64.tar.gz" },
          { "name": "CaissaDesk-win-x64.zip", "size": {{winSize}}, "browser_download_url": "https://github.com/duckarp/CaissaDesk/releases/download/{{tag}}/CaissaDesk-win-x64.zip" },
          { "name": "SHA256SUMS", "size": 200, "browser_download_url": "https://github.com/duckarp/CaissaDesk/releases/download/{{tag}}/SHA256SUMS" }
        ]
        """;

    [Fact]
    public void ParseRelease_ReadsFieldsAndAssets()
    {
        using var doc = JsonDocument.Parse(ReleaseJson("v1.2.0", body: "Hello"));
        var release = ReleaseParser.ParseRelease(doc.RootElement);

        Assert.NotNull(release);
        Assert.Equal("v1.2.0", release.TagName);
        Assert.Equal("1.2.0", release.Version.ToString());
        Assert.Equal("CaissaDesk v1.2.0", release.Title);
        Assert.Equal("Hello", release.Body);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.False(release.IsPrerelease);
        Assert.Equal(3, release.Assets.Count);
        Assert.Equal(1000, ReleaseParser.FindAsset(release, "CaissaDesk-linux-x64.tar.gz")!.Size);
    }

    [Fact]
    public void ParseRelease_SkipsDraftsAndNonSemverTags()
    {
        using var draft = JsonDocument.Parse(ReleaseJson("v1.2.0", draft: true));
        using var nightly = JsonDocument.Parse(ReleaseJson("nightly"));
        Assert.Null(ReleaseParser.ParseRelease(draft.RootElement));
        Assert.Null(ReleaseParser.ParseRelease(nightly.RootElement));
    }

    [Fact]
    public void SelectRelease_FromArray_PicksHighestVersion_AndHonorsPrereleaseSetting()
    {
        string json = "[" + string.Join(",",
            ReleaseJson("v1.3.0-beta.1", prerelease: true),
            ReleaseJson("v1.1.0"),
            ReleaseJson("v1.2.0"),
            ReleaseJson("v9.0.0", draft: true),
            ReleaseJson("not-a-version")) + "]";
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("v1.3.0-beta.1", ReleaseParser.SelectRelease(doc.RootElement, includePrereleases: true)!.TagName);
        Assert.Equal("v1.2.0", ReleaseParser.SelectRelease(doc.RootElement, includePrereleases: false)!.TagName);
    }

    [Fact]
    public void SelectRelease_SingleLatestObject_AndEmptyList()
    {
        using var latest = JsonDocument.Parse(ReleaseJson("v1.4.0"));
        using var empty = JsonDocument.Parse("[]");
        using var preOnly = JsonDocument.Parse(ReleaseJson("v1.4.0-rc.1", prerelease: true));

        Assert.Equal("v1.4.0", ReleaseParser.SelectRelease(latest.RootElement, false)!.TagName);
        Assert.Null(ReleaseParser.SelectRelease(empty.RootElement, true));
        Assert.Null(ReleaseParser.SelectRelease(preOnly.RootElement, false));
    }

    // ---------------------------------------------------------------- assets & URLs

    [Fact]
    public void GetPlatform_MapsOsAndArchitectureToExactAssetNames()
    {
        var linux = ReleaseParser.GetPlatform(OSPlatform.Linux, Architecture.X64);
        Assert.Equal(new UpdatePlatform("CaissaDesk-linux-x64.tar.gz", "CaissaDesk", ArchiveKind.TarGz), linux);

        var win = ReleaseParser.GetPlatform(OSPlatform.Windows, Architecture.X64);
        Assert.Equal(new UpdatePlatform("CaissaDesk-win-x64.zip", "CaissaDesk.exe", ArchiveKind.Zip), win);

        // Windows on Arm runs the x64 build under emulation
        Assert.Equal(win, ReleaseParser.GetPlatform(OSPlatform.Windows, Architecture.Arm64));

        Assert.Null(ReleaseParser.GetPlatform(OSPlatform.Linux, Architecture.Arm64));
        Assert.Null(ReleaseParser.GetPlatform(OSPlatform.OSX, Architecture.Arm64));
        Assert.Null(ReleaseParser.GetPlatform(OSPlatform.Windows, Architecture.X86));
    }

    [Fact]
    public void FindAsset_RequiresExactName()
    {
        string assets = """
            [
              { "name": "CaissaDesk-linux-x64.tar.gz.sig", "size": 1, "browser_download_url": "https://github.com/a/b/x" },
              { "name": "caissadesk-linux-x64.tar.gz", "size": 1, "browser_download_url": "https://github.com/a/b/y" },
              { "name": "CaissaDesk-linux-arm64.tar.gz", "size": 1, "browser_download_url": "https://github.com/a/b/z" }
            ]
            """;
        using var doc = JsonDocument.Parse(ReleaseJson("v2.0.0", assetsJson: assets));
        var release = ReleaseParser.ParseRelease(doc.RootElement)!;

        Assert.Null(ReleaseParser.FindAsset(release, "CaissaDesk-linux-x64.tar.gz"));
    }

    [Theory]
    [InlineData("https://github.com/duckarp/CaissaDesk/releases/download/v1/CaissaDesk-linux-x64.tar.gz", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1", true)]
    [InlineData("http://github.com/duckarp/CaissaDesk/releases/download/v1/x", false)]
    [InlineData("https://github.com.evil.example/x", false)]
    [InlineData("https://evilgithub.com/x", false)]
    [InlineData("https://user:pass@github.com/x", false)]
    [InlineData("https://github.com:8443/x", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void IsTrustedAssetUrl(string? url, bool expected)
    {
        Assert.Equal(expected, ReleaseParser.IsTrustedAssetUrl(url));
    }

    [Theory]
    [InlineData("duckarp/CaissaDesk", "duckarp/CaissaDesk")]
    [InlineData("  someone/my.repo_2  ", "someone/my.repo_2")]
    [InlineData("https://github.com/duckarp/CaissaDesk/", "duckarp/CaissaDesk")]
    [InlineData("https://github.com/duckarp/CaissaDesk.git", "duckarp/CaissaDesk")]
    [InlineData("duckarp", null)]
    [InlineData("duckarp/CaissaDesk/releases", null)]
    [InlineData("../etc/passwd", null)]
    [InlineData("owner/..", null)]
    [InlineData("owner/repo?x=1", null)]
    [InlineData("", null)]
    public void NormalizeRepository(string input, string? expected)
    {
        Assert.Equal(expected, ReleaseParser.NormalizeRepository(input));
    }

    // ---------------------------------------------------------------- SHA256SUMS

    private const string HashA = "bffd2d5a4dc9d86eb3d9886339e8ca446d88683f77559f0889ea0d2040e7d827";
    private const string HashB = "d2bfc61cbd291a5458f18adccb43b19b6f1be40a9c0cc86a5142b605298dc0d7";

    [Fact]
    public void ParseSha256Sums_ReadsTextAndBinaryModeLines()
    {
        string sums = $"{HashA}  CaissaDesk-linux-x64.tar.gz\r\n{HashB.ToUpperInvariant()} *CaissaDesk-win-x64.zip\n\n# comment\ngarbage line\n";
        var map = ReleaseParser.ParseSha256Sums(sums);

        Assert.Equal(2, map.Count);
        Assert.Equal(HashA, map["CaissaDesk-linux-x64.tar.gz"]);
        Assert.Equal(HashB, map["CaissaDesk-win-x64.zip"]);
    }

    [Fact]
    public void ParseSha256Sums_DropsAmbiguousAndMalformedEntries()
    {
        string sums = $"{HashA}  CaissaDesk-linux-x64.tar.gz\n{HashB}  CaissaDesk-linux-x64.tar.gz\n{HashA[..63]}  short.zip\n{HashA.Replace('b', 'g')}  nonhex.zip\n";
        var map = ReleaseParser.ParseSha256Sums(sums);

        Assert.Empty(map);
        Assert.Empty(ReleaseParser.ParseSha256Sums(null));
    }

    // ---------------------------------------------------------------- changelog

    [Theory]
    [InlineData("feat(pairing): add Dutch engine", ChangelogCategory.Feature, "Pairing", "Add Dutch engine")]
    [InlineData("fix: crash on empty roster.", ChangelogCategory.Fix, "", "Crash on empty roster")]
    [InlineData("perf!: faster standings", ChangelogCategory.Performance, "", "Faster standings")]
    [InlineData("docs(readme): explain updates", ChangelogCategory.Docs, "Readme", "Explain updates")]
    [InlineData("ci: cache NuGet", ChangelogCategory.Build, "", "Cache NuGet")]
    [InlineData("test: cover TRF import", ChangelogCategory.Tests, "", "Cover TRF import")]
    // CaissaDesk's imperative style
    [InlineData("Add README, LICENSE, and standalone desktop publisher scripts", ChangelogCategory.Feature, "", "Add README, LICENSE, and standalone desktop publisher scripts")]
    [InlineData("Implement manual pairing", ChangelogCategory.Feature, "", "Implement manual pairing")]
    [InlineData("Fix Bbp engine test check when binary is not installed in CI", ChangelogCategory.Fix, "", "Fix Bbp engine test check when binary is not installed in CI")]
    [InlineData("Fix crash: null roster", ChangelogCategory.Fix, "", "Fix crash: null roster")]
    [InlineData("Remove 22 unused localization keys", ChangelogCategory.Refactor, "", "Remove 22 unused localization keys")]
    [InlineData("Centralize HTTP helpers", ChangelogCategory.Refactor, "", "Centralize HTTP helpers")]
    [InlineData("Localize remaining UI strings", ChangelogCategory.Docs, "", "Localize remaining UI strings")]
    [InlineData("Harden scouting HTTP", ChangelogCategory.Update, "", "Harden scouting HTTP")]
    [InlineData("Align CI and release test step", ChangelogCategory.Update, "", "Align CI and release test step")]
    [InlineData("Fixed typo", ChangelogCategory.Fix, "", "Fixed typo")]
    [InlineData("Adds export", ChangelogCategory.Feature, "", "Adds export")]
    [InlineData("Capture and annotate build errors", ChangelogCategory.Update, "", "Capture and annotate build errors")]
    [InlineData("RoundGenerationService: report failure reasons instead of English text", ChangelogCategory.Update, "RoundGenerationService", "Report failure reasons instead of English text")]
    [InlineData("Manual pairing and attendance modals: fix state reset", ChangelogCategory.Fix, "Manual pairing and attendance modals", "Fix state reset")]
    public void ParseCommit_MapsCategoryScopeAndDescription(string message, ChangelogCategory category, string scope, string description)
    {
        var entry = ChangelogParser.ParseCommit(message + "\n\nBody text\nCo-Authored-By: someone", "0123456789abcdef");

        Assert.NotNull(entry);
        Assert.Equal(category, entry.Category);
        Assert.Equal(scope, entry.Scope);
        Assert.Equal(description, entry.Description);
        Assert.Equal("0123456", entry.Sha);
    }

    [Theory]
    [InlineData("Merge branch 'worktree-agent-a2040581165eb0e5b' into audit-fixes-3")]
    [InlineData("Merge pull request #12 from duckarp/feature")]
    [InlineData("Merge remote-tracking branch 'origin/main'")]
    [InlineData("Bump version to 1.2.3")]
    [InlineData("chore(release): 1.2.3")]
    [InlineData("Release v1.2.0")]
    [InlineData("v1.2.0")]
    [InlineData("1.2.0")]
    [InlineData("Version 2.0.0-beta.1")]
    [InlineData("chore: bump version")]
    [InlineData("")]
    [InlineData("   \n")]
    public void IsIgnoredCommit_DropsMergesAndVersionBumps(string message)
    {
        Assert.True(ChangelogParser.IsIgnoredCommit(message));
        Assert.Null(ChangelogParser.ParseCommit(message));
    }

    [Theory]
    [InlineData("Bump Photino.Blazor to 4.0.13")]
    [InlineData("Fix crash in 1.2.0")]
    [InlineData("Merge results from two files")]
    public void IsIgnoredCommit_KeepsRealChanges(string message)
    {
        Assert.False(ChangelogParser.IsIgnoredCommit(message));
    }

    [Fact]
    public void Build_GroupsInDisplayOrder_NewestFirst_WithoutDuplicates()
    {
        var commits = new List<(string, string)>
        {
            ("Fix first bug", "a1"),
            ("Add feature one", "a2"),
            ("Merge branch 'x' into main", "a3"),
            ("Harden thing", "a4"),
            ("Fix second bug", "a5"),
            ("Fix second bug", "a6"),
            ("Bump version to 1.1.0", "a7")
        };

        var groups = ChangelogParser.Build(commits);

        Assert.Equal([ChangelogCategory.Feature, ChangelogCategory.Fix, ChangelogCategory.Update], groups.Select(g => g.Category));
        Assert.Equal(["Fix second bug", "Fix first bug"], groups[1].Entries.Select(e => e.Description));
        Assert.Equal(4, groups.Sum(g => g.Entries.Count));
    }

    [Fact]
    public void SanitizeReleaseNotes_DropsFullChangelogLines()
    {
        string notes = "## What's Changed\r\n* Faster pairing by @dev in #3\r\n\r\n\r\n\r\n**Full Changelog**: https://github.com/duckarp/CaissaDesk/compare/v1.0.0...v1.1.0\r\nFull Changelog: x\r\nhttps://github.com/duckarp/CaissaDesk/compare/v1.0.0...v1.1.0\n";

        string sanitized = ChangelogParser.SanitizeReleaseNotes(notes);

        Assert.Equal("## What's Changed\n* Faster pairing by @dev in #3", sanitized);
        Assert.Equal("", ChangelogParser.SanitizeReleaseNotes("**Full Changelog**: https://github.com/a/b/compare/v1...v2"));
        Assert.Equal("", ChangelogParser.SanitizeReleaseNotes(null));
    }

    [Fact]
    public void SanitizeReleaseNotes_StripsControlCharactersAndCapsLength()
    {
        string sanitized = ChangelogParser.SanitizeReleaseNotes("a\u0007b\u001b[31mc\tok\n" + new string('x', 100), maxLength: 20);

        Assert.DoesNotContain('\u0007', sanitized);
        Assert.DoesNotContain('\u001b', sanitized);
        Assert.StartsWith("ab[31mc\tok", sanitized);
        Assert.EndsWith("…", sanitized);
        Assert.Equal(21, sanitized.Length);
    }
}
