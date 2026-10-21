using System.Diagnostics;
using System.Formats.Tar;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CaissaDesk.Core.PairingEngine;
using CaissaDesk.Core.Settings;
using CaissaDesk.Core.Updates;
using CaissaDesk.Data;

namespace CaissaDesk.Tests;

/// <summary>Check / download / apply flow of <see cref="UpdateService"/> against a fake GitHub (no network).</summary>
public sealed class UpdateServiceTests : IDisposable
{
    private const string Repo = "duckarp/CaissaDesk";
    private const string ApiLatest = "https://api.github.com/repos/duckarp/CaissaDesk/releases/latest";
    private const string LinuxAssetUrl = "https://github.com/duckarp/CaissaDesk/releases/download/v1.1.0/CaissaDesk-linux-x64.tar.gz";
    private const string SumsUrl = "https://github.com/duckarp/CaissaDesk/releases/download/v1.1.0/SHA256SUMS";

    private static readonly UpdatePlatform LinuxPlatform = new("CaissaDesk-linux-x64.tar.gz", "CaissaDesk", ArchiveKind.TarGz);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "caissadesk-updsvc-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeGitHub _github = new();
    private readonly AppSettingsService _settings;
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly List<ProcessStartInfo> _started = [];
    private int _shutdownRequests;
    private bool _dllNextToExe;

    public UpdateServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _settings = new AppSettingsService(new PairingEngineRegistry(), Path.Combine(_dir, "settings.json"));
        Directory.CreateDirectory(InstallDir);
        File.WriteAllText(InstalledExe, "old");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string InstallDir => Path.Combine(_dir, "install dir");
    private string InstalledExe => Path.Combine(InstallDir, "CaissaDesk");
    private string UpdatesRoot => Path.Combine(_dir, "updates");

    private UpdateService CreateService(string currentVersion = "1.0.0", long maxDownload = UpdateService.DefaultMaxDownloadBytes) => new(
        new FakeFactory(_github),
        _settings,
        options: new UpdateServiceOptions
        {
            CurrentVersion = currentVersion,
            UpdatesRoot = UpdatesRoot,
            HostInfo = () => new UpdateHostInfo(InstalledExe, null, LinuxPlatform, IsWindows: false),
            FileExists = p => _dllNextToExe && p.EndsWith("CaissaDesk.dll", StringComparison.Ordinal),
            IsDirectoryWritable = _ => true,
            TimeProvider = _time,
            StartProcess = psi => { _started.Add(psi); return null!; },
            RequestShutdown = () => _shutdownRequests++,
            MaxDownloadBytes = maxDownload
        });

    private static byte[] TarGzWithExe(byte[] exe)
    {
        var path = Path.GetTempFileName();
        try
        {
            UpdatePackageTests.WriteTarGz(path, UpdatePackageTests.Entry("CaissaDesk", exe));
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private void ServeRelease(string tag = "v1.1.0", string body = "Big release\n\n**Full Changelog**: https://github.com/duckarp/CaissaDesk/compare/v1.0.0...v1.1.0", string? assets = null)
    {
        _github.Json(ApiLatest, UpdateParsingTests.ReleaseJson(tag, body: body, assetsJson: assets));
    }

    private void ServeCompare(string baseTag = "v1.0.0", string head = "v1.1.0") =>
        _github.Json($"https://api.github.com/repos/{Repo}/compare/{baseTag}...{head}?per_page=100", """
            { "status": "ahead", "commits": [
                { "sha": "1111111aaaa", "commit": { "message": "Fix crash when the roster is empty" } },
                { "sha": "2222222bbbb", "commit": { "message": "Add in-app updater\n\nLong body" } },
                { "sha": "3333333cccc", "commit": { "message": "Merge branch 'feature' into main" } },
                { "sha": "4444444dddd", "commit": { "message": "Bump version to 1.1.0" } }
            ] }
            """);

    private (byte[] Archive, byte[] Exe) ServeAssets(Func<byte[], string>? sums = null)
    {
        byte[] exe = Encoding.UTF8.GetBytes("#!/bin/sh\necho caissadesk 1.1.0\n");
        byte[] archive = TarGzWithExe(exe);
        _github.Bytes(LinuxAssetUrl, archive);
        _github.Text(SumsUrl, sums?.Invoke(archive) ?? $"{Sha(archive)}  CaissaDesk-linux-x64.tar.gz\n{new string('0', 64)}  CaissaDesk-win-x64.zip\n");
        return (archive, exe);
    }

    private static string Assets(long linuxSize) => UpdateParsingTests.StandardAssets("v1.1.0", linuxSize: linuxSize);

    // ---------------------------------------------------------------- check

    [Fact]
    public async Task Check_NewerRelease_BuildsChangelogAndSanitizedNotes_AndRecordsTheCheck()
    {
        ServeRelease();
        ServeCompare();
        var service = CreateService();
        int stateChanges = 0;
        service.StateChanged += () => stateChanges++;

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsUpdateAvailable);
        Assert.True(result.CanInstall);
        Assert.Equal("1.1.0", result.Release!.Version.ToString());
        Assert.Equal("CaissaDesk-linux-x64.tar.gz", result.Asset!.Name);
        Assert.Equal("SHA256SUMS", result.ChecksumsAsset!.Name);
        Assert.Equal("Big release", result.AuthorNotes);
        Assert.Equal([ChangelogCategory.Feature, ChangelogCategory.Fix], result.Changelog.Select(g => g.Category));
        Assert.Equal("https://github.com/duckarp/CaissaDesk/compare/v1.0.0...v1.1.0", result.CompareUrl);
        // built from repo + tag, never the API's html_url
        Assert.Equal("https://github.com/duckarp/CaissaDesk/releases/tag/v1.1.0", result.ReleasePageUrl);
        Assert.Same(result, service.LastResult);
        Assert.False(service.IsChecking);
        Assert.Equal(2, stateChanges);

        Assert.Equal(_time.GetUtcNow(), _settings.Settings.UpdateLastCheckedAt);
        Assert.Equal("1.1.0", _settings.Settings.UpdateLatestKnownVersion);
        var reloaded = new AppSettingsService(new PairingEngineRegistry(), Path.Combine(_dir, "settings.json")).Settings;
        Assert.Equal(_time.GetUtcNow(), reloaded.UpdateLastCheckedAt);
        Assert.Equal("1.1.0", reloaded.UpdateLatestKnownVersion);
        Assert.Equal(Repo, reloaded.UpdateRepository);

        var api = _github.Requests[0];
        Assert.Equal(ApiLatest, api.Uri);
        Assert.StartsWith("CaissaDesk/", api.UserAgent);
        Assert.Contains("(+https://github.com/duckarp/CaissaDesk)", api.UserAgent);
        Assert.Contains("application/vnd.github+json", api.Accept);
    }

    [Fact]
    public async Task Check_Prereleases_UsesReleaseListAndPicksHighest()
    {
        _settings.Settings.IncludePrereleaseUpdates = true;
        _github.Json($"https://api.github.com/repos/{Repo}/releases?per_page=30", "[" +
            UpdateParsingTests.ReleaseJson("v1.1.0") + "," +
            UpdateParsingTests.ReleaseJson("v1.2.0-beta.1", prerelease: true) + "]");

        var result = await CreateService().CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("v1.2.0-beta.1", result.Release!.TagName);
        Assert.Empty(result.Changelog); // compare endpoint answered 404
        Assert.Null(result.CompareUrl);
    }

    [Fact]
    public async Task Check_SameVersion_IsUpToDate()
    {
        ServeRelease("v1.0.0");

        var result = await CreateService("1.0.0").CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.False(result.IsUpdateAvailable);
        Assert.Single(_github.Requests); // no compare call when there is nothing to update
    }

    [Fact]
    public async Task Check_CurrentPrerelease_SeesTheFinalRelease()
    {
        ServeRelease("v1.1.0");

        var result = await CreateService("1.1.0-beta.2").CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
    }

    [Fact]
    public async Task Check_NoReleases_On404()
    {
        var result = await CreateService().CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.NoReleases, result.Status);
        Assert.NotNull(_settings.Settings.UpdateLastCheckedAt);
    }

    [Fact]
    public async Task Check_RateLimited_ReportsResetTime_AndBacksOff()
    {
        var reset = new DateTimeOffset(2026, 10, 21, 13, 0, 0, TimeSpan.Zero);
        _github.Handle(ApiLatest, () =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"API rate limit exceeded\"}") };
            r.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            r.Headers.TryAddWithoutValidation("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return r;
        });

        var result = await CreateService().CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
        Assert.Equal(reset, result.RateLimitResetAt);
        Assert.Equal(_time.GetUtcNow(), _settings.Settings.UpdateLastCheckedAt);
    }

    [Fact]
    public async Task Check_429WithRetryAfter_IsRateLimited()
    {
        _github.Handle(ApiLatest, () =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return r;
        });

        var result = await CreateService().CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.RateLimited, result.Status);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromMinutes(2), result.RateLimitResetAt);
    }

    [Fact]
    public async Task Check_ServerErrorOrNetworkFailure_IsFailed_AndNotRecorded()
    {
        _github.Handle(ApiLatest, () => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("<html><script>x</script>boom</html>") });
        var serverError = await CreateService().CheckForUpdatesAsync();

        _github.Handle(ApiLatest, () => throw new HttpRequestException("no route to host"));
        var network = await CreateService().CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.Failed, serverError.Status);
        Assert.Equal("HTTP 500", serverError.ErrorDetail);
        Assert.Equal(UpdateCheckStatus.Failed, network.Status);
        Assert.Contains("no route to host", network.ErrorDetail);
        Assert.Null(_settings.Settings.UpdateLastCheckedAt);
    }

    [Fact]
    public async Task Check_InvalidRepository_DoesNotCallGitHub()
    {
        _settings.Settings.UpdateRepository = "not a repo";

        var result = await CreateService().CheckForUpdatesAsync();

        Assert.Equal(UpdateCheckStatus.InvalidRepository, result.Status);
        Assert.Empty(_github.Requests);
    }

    [Fact]
    public async Task Check_MissingChecksumsOrAsset_DisablesInAppInstall()
    {
        ServeRelease(assets: """[ { "name": "CaissaDesk-linux-x64.tar.gz", "size": 10, "browser_download_url": "https://github.com/duckarp/CaissaDesk/releases/download/v1.1.0/CaissaDesk-linux-x64.tar.gz" } ]""");
        var noSums = await CreateService().CheckForUpdatesAsync();

        ServeRelease(assets: """[ { "name": "CaissaDesk-linux-x64.tar.gz", "size": 10, "browser_download_url": "https://evil.example/CaissaDesk-linux-x64.tar.gz" }, { "name": "SHA256SUMS", "size": 1, "browser_download_url": "https://github.com/x/y/SHA256SUMS" } ]""");
        var untrusted = await CreateService().CheckForUpdatesAsync();

        Assert.Equal(UpdateInstallBlocker.NoChecksums, noSums.InstallBlocker);
        Assert.False(noSums.CanInstall);
        Assert.True(noSums.IsUpdateAvailable);
        Assert.Equal(UpdateInstallBlocker.NoAssetForPlatform, untrusted.InstallBlocker);
        await Assert.ThrowsAsync<UpdateException>(() => CreateService().DownloadAsync(noSums));
    }

    [Fact]
    public async Task Check_DevelopmentBuild_DisablesInAppInstall()
    {
        _dllNextToExe = true;
        ServeRelease();

        var result = await CreateService().CheckForUpdatesAsync();

        Assert.Equal(UpdateInstallBlocker.DevelopmentBuild, result.InstallBlocker);
        Assert.False(result.CanInstall);
        Assert.Equal("https://github.com/duckarp/CaissaDesk/releases/tag/v1.1.0", result.ReleasePageUrl);
    }

    // ---------------------------------------------------------------- minimum interval

    [Fact]
    public void ShouldAutoCheck_RespectsSettingAndMinimumInterval()
    {
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var s = new AppSettings();

        Assert.True(UpdateService.ShouldAutoCheck(s, now, "1.0.0")); // never checked

        s.UpdateLastCheckedAt = now - TimeSpan.FromHours(1);
        Assert.False(UpdateService.ShouldAutoCheck(s, now, "1.0.0"));

        s.UpdateLastCheckedAt = now - UpdateService.MinimumAutoCheckInterval;
        Assert.True(UpdateService.ShouldAutoCheck(s, now, "1.0.0"));

        s.UpdateLastCheckedAt = now + TimeSpan.FromDays(3); // clock went backwards
        Assert.True(UpdateService.ShouldAutoCheck(s, now, "1.0.0"));

        s.UpdateLastCheckedAt = now - TimeSpan.FromMinutes(10);
        s.UpdateLatestKnownVersion = "1.1.0"; // a pending update is shown again after a restart
        Assert.True(UpdateService.ShouldAutoCheck(s, now, "1.0.0"));
        Assert.False(UpdateService.ShouldAutoCheck(s, now, "1.1.0"));

        s.CheckForUpdatesOnStartup = false;
        s.UpdateLastCheckedAt = null;
        Assert.False(UpdateService.ShouldAutoCheck(s, now, "1.0.0"));
    }

    [Fact]
    public async Task CheckIfDue_SkipsRecentChecks_ForcedCheckBypassesInterval()
    {
        ServeRelease("v1.0.0");
        _settings.Settings.UpdateLastCheckedAt = _time.GetUtcNow() - TimeSpan.FromHours(1);
        var service = CreateService();

        Assert.Null(await service.CheckIfDueAsync());
        Assert.Empty(_github.Requests);

        Assert.Equal(UpdateCheckStatus.UpToDate, (await service.CheckForUpdatesAsync()).Status);
        Assert.Single(_github.Requests);

        _time.Advance(TimeSpan.FromHours(7));
        Assert.NotNull(await service.CheckIfDueAsync());
        Assert.Equal(2, _github.Requests.Count);
    }

    // ---------------------------------------------------------------- download

    [Fact]
    public async Task Download_VerifiesChecksum_ExtractsIntoPrivateStaging_AndReportsProgress()
    {
        var (archive, exe) = ServeAssets();
        ServeRelease(assets: Assets(0));
        var service = CreateService();
        var update = await service.CheckForUpdatesAsync();
        var reports = new List<UpdateDownloadProgress>();

        var staged = await service.DownloadAsync(update, new SyncProgress<UpdateDownloadProgress>(reports.Add));

        Assert.Equal("1.1.0", staged.Version);
        Assert.Equal(InstalledExe, staged.TargetExecutablePath);
        Assert.StartsWith(Path.GetFullPath(UpdatesRoot) + Path.DirectorySeparatorChar, staged.StagingDirectory);
        Assert.Equal(exe, File.ReadAllBytes(staged.StagedExecutablePath));
        Assert.False(File.Exists(Path.Combine(staged.StagingDirectory, "CaissaDesk-linux-x64.tar.gz")), "archive is removed after extraction");
        Assert.Equal(archive.Length, reports[^1].BytesReceived);
        Assert.Equal("old", File.ReadAllText(InstalledExe)); // nothing touched the install yet

        if (!OperatingSystem.IsWindows())
        {
            var privateDir = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Assert.Equal(privateDir, File.GetUnixFileMode(UpdatesRoot));
            Assert.Equal(privateDir, File.GetUnixFileMode(staged.StagingDirectory));
        }

        var download = _github.Requests.Single(r => r.Uri == LinuxAssetUrl);
        Assert.StartsWith("CaissaDesk/", download.UserAgent);
    }

    [Fact]
    public async Task Download_ChecksumMismatch_FailsClosed_AndDeletesTheFile()
    {
        ServeAssets(_ => $"{new string('a', 64)}  CaissaDesk-linux-x64.tar.gz\n");
        ServeRelease(assets: Assets(0));
        var service = CreateService();
        var update = await service.CheckForUpdatesAsync();

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(update));

        Assert.Equal(UpdateFailure.ChecksumMismatch, ex.Failure);
        Assert.Empty(Directory.EnumerateFileSystemEntries(UpdatesRoot));
    }

    [Theory]
    [InlineData("missing entry", UpdateFailure.ChecksumEntryMissing)]
    [InlineData("404", UpdateFailure.ChecksumsMissing)]
    [InlineData("ambiguous", UpdateFailure.ChecksumEntryMissing)]
    public async Task Download_ChecksumsUnusable_FailsClosed_BeforeDownloadingTheArchive(string scenario, UpdateFailure expected)
    {
        ServeAssets(archive => scenario switch
        {
            "ambiguous" => $"{Sha(archive)}  CaissaDesk-linux-x64.tar.gz\n{new string('b', 64)}  CaissaDesk-linux-x64.tar.gz\n",
            _ => $"{Sha(archive)}  CaissaDesk-win-x64.zip\n"
        });
        if (scenario == "404") _github.Remove(SumsUrl);
        ServeRelease(assets: Assets(0));
        var service = CreateService();
        var update = await service.CheckForUpdatesAsync();

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(update));

        Assert.Equal(expected, ex.Failure);
        Assert.DoesNotContain(_github.Requests, r => r.Uri == LinuxAssetUrl);
    }

    [Fact]
    public async Task Download_RedirectToUntrustedHost_IsRejected()
    {
        ServeAssets();
        _github.RedirectFinalUri(LinuxAssetUrl, new Uri("https://evil.example/payload.tar.gz"));
        ServeRelease(assets: Assets(0));
        var service = CreateService();
        var update = await service.CheckForUpdatesAsync();

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(update));

        Assert.Equal(UpdateFailure.UntrustedUrl, ex.Failure);
    }

    [Fact]
    public async Task Download_OverSizeLimit_OrWrongSize_IsRejected()
    {
        var (archive, _) = ServeAssets();
        ServeRelease(assets: Assets(0));
        var small = CreateService(maxDownload: archive.Length - 1);
        var tooLarge = await Assert.ThrowsAsync<UpdateException>(async () => await small.DownloadAsync(await small.CheckForUpdatesAsync()));

        ServeRelease(assets: Assets(archive.Length + 5));
        var service = CreateService();
        var wrongSize = await Assert.ThrowsAsync<UpdateException>(async () => await service.DownloadAsync(await service.CheckForUpdatesAsync()));

        ServeRelease(assets: Assets(UpdateService.DefaultMaxDownloadBytes + 1));
        var declared = await Assert.ThrowsAsync<UpdateException>(async () => await service.DownloadAsync(await service.CheckForUpdatesAsync()));

        Assert.Equal(UpdateFailure.TooLarge, tooLarge.Failure);
        Assert.Equal(UpdateFailure.SizeMismatch, wrongSize.Failure);
        Assert.Equal(UpdateFailure.TooLarge, declared.Failure);
    }

    [Fact]
    public async Task Download_Cancelled_CleansUp()
    {
        ServeAssets();
        ServeRelease(assets: Assets(0));
        var service = CreateService();
        var update = await service.CheckForUpdatesAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(update, null, cts.Token));
        Assert.False(Directory.Exists(UpdatesRoot) && Directory.EnumerateDirectories(UpdatesRoot).Any());
    }

    [Theory]
    [InlineData("")]
    [InlineData("CaissaDesk/updates")]
    public async Task Download_WithoutAbsoluteUpdatesFolder_IsRefused(string root)
    {
        ServeAssets();
        ServeRelease(assets: Assets(0));
        var service = new UpdateService(new FakeFactory(_github), _settings, options: new UpdateServiceOptions
        {
            CurrentVersion = "1.0.0",
            UpdatesRoot = root,
            HostInfo = () => new UpdateHostInfo(InstalledExe, null, LinuxPlatform, IsWindows: false),
            FileExists = _ => false,
            IsDirectoryWritable = _ => true
        });
        var update = await service.CheckForUpdatesAsync();

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(update));

        Assert.Equal(UpdateFailure.InstallNotSupported, ex.Failure);
        Assert.False(root.Length > 0 && Directory.Exists(root), "nothing may be created relative to the working directory");
        var staged = new StagedUpdate("1.1.0", Path.Combine(_dir, "s"), Path.Combine(_dir, "s", "CaissaDesk"), InstalledExe);
        Assert.Equal(UpdateFailure.InstallNotSupported, (await Assert.ThrowsAsync<UpdateException>(() => service.ApplyAndRestartAsync(staged))).Failure);
    }

    // ---------------------------------------------------------------- apply

    [Fact]
    public async Task Apply_WritesPrivateScript_StartsItDetached_AndRequestsShutdown()
    {
        ServeAssets();
        ServeRelease(assets: Assets(0));
        var service = CreateService();
        var staged = await service.DownloadAsync(await service.CheckForUpdatesAsync());

        // the fake launcher returns null: the service must report that, not exit
        var failed = await Assert.ThrowsAsync<UpdateException>(() => service.ApplyAndRestartAsync(staged));
        Assert.Equal(UpdateFailure.ApplyFailed, failed.Failure);
        Assert.Equal(0, _shutdownRequests);

        string scriptPath = Path.Combine(staged.StagingDirectory, OperatingSystem.IsWindows() ? "apply-update.cmd" : "apply-update.sh");
        Assert.True(File.Exists(scriptPath));
        var psi = Assert.Single(_started);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.CreateNoWindow);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("/bin/sh", psi.FileName);
            Assert.Equal([scriptPath], psi.ArgumentList);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(scriptPath));
            string content = File.ReadAllText(scriptPath);
            Assert.Contains("PID=" + Environment.ProcessId, content);
            Assert.Contains("TARGET=" + UpdateScriptBuilder.ShellQuote(InstalledExe), content);
        }
    }

    [Fact]
    public async Task Apply_WithRunningLauncher_RequestsShutdown()
    {
        ServeAssets();
        ServeRelease(assets: Assets(0));
        var service = new UpdateService(new FakeFactory(_github), _settings, options: new UpdateServiceOptions
        {
            CurrentVersion = "1.0.0",
            UpdatesRoot = UpdatesRoot,
            HostInfo = () => new UpdateHostInfo(InstalledExe, null, LinuxPlatform, IsWindows: false),
            FileExists = _ => false,
            IsDirectoryWritable = _ => true,
            TimeProvider = _time,
            StartProcess = psi => { _started.Add(psi); return Process.GetCurrentProcess(); },
            RequestShutdown = () => _shutdownRequests++
        });
        var staged = await service.DownloadAsync(await service.CheckForUpdatesAsync());

        await service.ApplyAndRestartAsync(staged);

        Assert.Single(_started);
        Assert.Equal(1, _shutdownRequests);
    }

    [Fact]
    public async Task Apply_RejectsStagingOutsideTheUpdatesFolder_OrAnotherTarget()
    {
        var service = CreateService();
        string elsewhere = Path.Combine(_dir, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "CaissaDesk"), "x");

        var outside = new StagedUpdate("1.1.0", elsewhere, Path.Combine(elsewhere, "CaissaDesk"), InstalledExe);
        var otherTarget = new StagedUpdate("1.1.0", elsewhere, Path.Combine(elsewhere, "CaissaDesk"), "/usr/bin/CaissaDesk");

        Assert.Equal(UpdateFailure.ApplyFailed, (await Assert.ThrowsAsync<UpdateException>(() => service.ApplyAndRestartAsync(outside))).Failure);
        Assert.Equal(UpdateFailure.ApplyFailed, (await Assert.ThrowsAsync<UpdateException>(() => service.ApplyAndRestartAsync(otherTarget))).Failure);
        Assert.Empty(_started);
    }

    // ---------------------------------------------------------------- fakes

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record SeenRequest(string Uri, string UserAgent, string Accept);

    /// <summary>Routes absolute URLs to canned responses; unknown URLs answer 404.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Uri> _finalUris = new(StringComparer.Ordinal);
        public List<SeenRequest> Requests { get; } = [];

        public void Handle(string url, Func<HttpResponseMessage> response) => _routes[url] = response;
        public void Remove(string url) => _routes.Remove(url);
        public void RedirectFinalUri(string url, Uri final) => _finalUris[url] = final;

        public void Json(string url, string json) =>
            Handle(url, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

        public void Text(string url, string text) =>
            Handle(url, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });

        public void Bytes(string url, byte[] data) =>
            Handle(url, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string url = request.RequestUri!.AbsoluteUri;
            Requests.Add(new SeenRequest(url, request.Headers.UserAgent.ToString(), request.Headers.Accept.ToString()));

            var response = _routes.TryGetValue(url, out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = _finalUris.TryGetValue(url, out var final) ? new HttpRequestMessage(HttpMethod.Get, final) : request;
            return Task.FromResult(response);
        }
    }
}
