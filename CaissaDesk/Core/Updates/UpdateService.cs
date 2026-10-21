using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CaissaDesk.Core.Settings;
using CaissaDesk.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CaissaDesk.Core;

namespace CaissaDesk.Core.Updates;

/// <summary>Test seams and limits for <see cref="UpdateService"/>; production uses the defaults.</summary>
public sealed class UpdateServiceOptions
{
    public string? CurrentVersion { get; init; }
    public string? UpdatesRoot { get; init; }
    public Func<UpdateHostInfo>? HostInfo { get; init; }
    public Func<string, bool>? FileExists { get; init; }
    public Func<string, bool>? IsDirectoryWritable { get; init; }
    public TimeProvider? TimeProvider { get; init; }
    public Func<ProcessStartInfo, Process?>? StartProcess { get; init; }
    public Action? RequestShutdown { get; init; }
    public long MaxDownloadBytes { get; init; } = UpdateService.DefaultMaxDownloadBytes;
}

/// <summary>
/// Checks GitHub Releases for a newer CaissaDesk, builds the changelog from the compare API and installs
/// the update: SHA256SUMS-verified download into a private per-update folder, strict extraction of the
/// single executable, then a generated helper script swaps the binary after the app has exited.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    public const string HttpClientName = "github-updates";
    public const long DefaultMaxDownloadBytes = 300L * 1024 * 1024;
    public static readonly TimeSpan MinimumAutoCheckInterval = TimeSpan.FromHours(6);

    private const long MaxApiResponseBytes = 8L * 1024 * 1024;
    private const long MaxChecksumsBytes = 64 * 1024;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleStagingAge = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppSettingsService _settings;
    private readonly ILogger<UpdateService> _logger;
    private readonly IAppWindowProvider? _windowProvider;
    private readonly UpdateServiceOptions _options;
    private readonly TimeProvider _time;
    private readonly string _updatesRoot;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    private volatile UpdateCheckResult? _lastResult;
    private volatile bool _isChecking;

    public UpdateService(
        IHttpClientFactory httpClientFactory,
        IAppSettingsService settings,
        ILogger<UpdateService>? logger = null,
        IAppWindowProvider? windowProvider = null,
        UpdateServiceOptions? options = null)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger ?? NullLogger<UpdateService>.Instance;
        _windowProvider = windowProvider;
        _options = options ?? new UpdateServiceOptions();
        _time = _options.TimeProvider ?? TimeProvider.System;
        CurrentVersion = _options.CurrentVersion ?? AppVersion.Current;
        _updatesRoot = _options.UpdatesRoot ?? DefaultUpdatesRoot();
    }

    /// <summary>
    /// LocalApplicationData/CaissaDesk/updates. DoNotVerify: the default option returns "" when the folder does
    /// not exist yet (fresh profile), which would silently turn this into a path relative to the working directory.
    /// </summary>
    private static string DefaultUpdatesRoot()
    {
        string baseDir = AppPaths.LocalAppDataRoot;
        return string.IsNullOrEmpty(baseDir) ? "" : Path.Combine(baseDir, "CaissaDesk", "updates");
    }

    /// <summary>Honest User-Agent (GitHub's API requires one).</summary>
    public static string UserAgent { get; } = $"CaissaDesk/{AppVersion.Current} (+https://github.com/duckarp/CaissaDesk)";

    public string CurrentVersion { get; }
    public UpdateCheckResult? LastResult => _lastResult;
    public bool IsChecking => _isChecking;
    public event Action? StateChanged;

    public static IServiceCollection AddUpdateHttpClient(IServiceCollection services)
    {
        services.AddHttpClient(HttpClientName, client =>
            {
                // API calls use their own shorter timeout; this bounds a whole (large) download
                client.Timeout = TimeSpan.FromMinutes(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true, // github.com download URLs redirect to the asset CDN (validated after)
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        return services;
    }

    // ------------------------------------------------------------------ checking

    /// <summary>
    /// Automatic checks run at most every <see cref="MinimumAutoCheckInterval"/> (GitHub allows 60 anonymous
    /// API requests per hour per IP), except while a previously seen newer version is still pending, so the
    /// header pill reappears after a restart. A LastCheckedAt in the future (clock change) counts as stale.
    /// </summary>
    public static bool ShouldAutoCheck(AppSettings settings, DateTimeOffset now, string currentVersion)
    {
        if (!settings.CheckForUpdatesOnStartup) return false;
        if (settings.UpdateLastCheckedAt is not { } last) return true;
        if (SemVersion.IsNewer(settings.UpdateLatestKnownVersion, currentVersion)) return true;
        if (last > now + TimeSpan.FromMinutes(5)) return true;
        return now - last >= MinimumAutoCheckInterval;
    }

    public async Task<UpdateCheckResult?> CheckIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (!ShouldAutoCheck(_settings.Settings, _time.GetUtcNow(), CurrentVersion))
        {
            _logger.LogDebug("Automatic update check skipped (disabled or checked recently).");
            return null;
        }
        return await CheckForUpdatesAsync(cancellationToken);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        // A second caller (badge click during the startup check) waits for and shares the running check
        bool joinedRunningCheck = false;
        if (!await _checkGate.WaitAsync(0, cancellationToken))
        {
            await _checkGate.WaitAsync(cancellationToken);
            joinedRunningCheck = true;
        }

        try
        {
            if (joinedRunningCheck && _lastResult is { } shared)
            {
                return shared;
            }

            _isChecking = true;
            RaiseStateChanged();

            var result = await RunCheckAsync(cancellationToken);
            _lastResult = result;
            return result;
        }
        finally
        {
            _isChecking = false;
            _checkGate.Release();
            RaiseStateChanged();
        }
    }

    private async Task<UpdateCheckResult> RunCheckAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Settings;
        var now = _time.GetUtcNow();
        string configured = string.IsNullOrWhiteSpace(settings.UpdateRepository) ? ReleaseParser.DefaultRepository : settings.UpdateRepository;
        string? repo = ReleaseParser.NormalizeRepository(configured);
        if (repo == null)
        {
            _logger.LogWarning("Update repository {Repository} is not a valid owner/repo name.", configured);
            return new UpdateCheckResult { Status = UpdateCheckStatus.InvalidRepository, CurrentVersion = CurrentVersion, Repository = configured, CheckedAt = now };
        }

        bool includePrereleases = settings.IncludePrereleaseUpdates;
        string url = includePrereleases
            ? $"https://api.github.com/repos/{repo}/releases?per_page=30"
            : $"https://api.github.com/repos/{repo}/releases/latest";

        try
        {
            using var response = await SendApiAsync(url, cancellationToken);

            if (IsRateLimited(response))
            {
                var reset = GetRateLimitReset(response, now);
                _logger.LogWarning("GitHub API rate limit reached while checking for updates (resets {Reset}).", reset);
                await RecordCheckAsync(now, null);
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.RateLimited,
                    CurrentVersion = CurrentVersion,
                    Repository = repo,
                    CheckedAt = now,
                    RateLimitResetAt = reset,
                    ReleasePageUrl = ReleaseParser.BuildReleasesListUrl(repo)
                };
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // /releases/latest answers 404 when the repository has no (non-prerelease) release yet
                await RecordCheckAsync(now, null);
                return new UpdateCheckResult { Status = UpdateCheckStatus.NoReleases, CurrentVersion = CurrentVersion, Repository = repo, CheckedAt = now };
            }

            if (!response.IsSuccessStatusCode)
            {
                string body = await ReadCappedStringAsync(response.Content, 4096, cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Update check for {Repository} failed: HTTP {Status} {Body}.", repo, (int)response.StatusCode, ScoutingHttp.SanitizeServerError(body));
                return Failed(repo, now, $"HTTP {(int)response.StatusCode}");
            }

            string json = await ReadCappedStringAsync(response.Content, MaxApiResponseBytes, cancellationToken);
            ReleaseInfo? release;
            using (var doc = JsonDocument.Parse(json))
            {
                release = ReleaseParser.SelectRelease(doc.RootElement, includePrereleases);
            }

            if (release == null)
            {
                await RecordCheckAsync(now, null);
                return new UpdateCheckResult { Status = UpdateCheckStatus.NoReleases, CurrentVersion = CurrentVersion, Repository = repo, CheckedAt = now };
            }

            string releasePage = ReleaseParser.BuildReleasePageUrl(repo, release.TagName);
            await RecordCheckAsync(now, release.Version.ToString());

            if (!SemVersion.IsNewer(release.TagName, CurrentVersion))
            {
                _logger.LogInformation("CaissaDesk {Current} is up to date (latest release {Tag}).", CurrentVersion, release.TagName);
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpToDate,
                    CurrentVersion = CurrentVersion,
                    Repository = repo,
                    CheckedAt = now,
                    Release = release,
                    ReleasePageUrl = releasePage
                };
            }

            var host = HostInfo();
            var platform = host.Platform;
            var asset = platform == null ? null : ReleaseParser.FindAsset(release, platform.ArchiveName);
            var checksums = ReleaseParser.FindAsset(release, ReleaseParser.ChecksumsAssetName);
            var blocker = EvaluateBlocker(host);
            if (blocker == UpdateInstallBlocker.None)
            {
                if (asset == null || !ReleaseParser.IsTrustedAssetUrl(asset.DownloadUrl)) blocker = UpdateInstallBlocker.NoAssetForPlatform;
                else if (checksums == null || !ReleaseParser.IsTrustedAssetUrl(checksums.DownloadUrl)) blocker = UpdateInstallBlocker.NoChecksums;
            }

            var (changelog, compareUrl) = await FetchChangelogAsync(repo, release.TagName, cancellationToken);

            _logger.LogInformation("Update available: {Current} -> {Tag} (in-app install: {Blocker}).", CurrentVersion, release.TagName, blocker);
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                CurrentVersion = CurrentVersion,
                Repository = repo,
                CheckedAt = now,
                Release = release,
                Asset = asset,
                ChecksumsAsset = checksums,
                Platform = platform,
                InstallBlocker = blocker,
                AuthorNotes = ChangelogParser.SanitizeReleaseNotes(release.Body),
                Changelog = changelog,
                CompareUrl = compareUrl,
                ReleasePageUrl = releasePage
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or IOException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Update check for {Repository} failed.", repo);
            return Failed(repo, now, ex is OperationCanceledException ? "timeout" : ex.Message);
        }
    }

    private UpdateCheckResult Failed(string repo, DateTimeOffset now, string detail) => new()
    {
        Status = UpdateCheckStatus.Failed,
        CurrentVersion = CurrentVersion,
        Repository = repo,
        CheckedAt = now,
        ErrorDetail = ScoutingHttp.SanitizeServerError(detail, 200),
        ReleasePageUrl = ReleaseParser.BuildReleasesListUrl(repo)
    };

    private async Task<(IReadOnlyList<ChangelogGroup> Groups, string? CompareUrl)> FetchChangelogAsync(string repo, string headTag, CancellationToken cancellationToken)
    {
        if (!SemVersion.TryParse(CurrentVersion, out var current)) return ([], null);

        string baseTag = "v" + current;
        string url = $"https://api.github.com/repos/{repo}/compare/{Uri.EscapeDataString(baseTag)}...{Uri.EscapeDataString(headTag)}?per_page=100";
        try
        {
            using var response = await SendApiAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Typically 404: the running version has no tag (local build); the notes still show
                _logger.LogInformation("No changelog between {Base} and {Head}: HTTP {Status}.", baseTag, headTag, (int)response.StatusCode);
                return ([], null);
            }

            string json = await ReadCappedStringAsync(response.Content, MaxApiResponseBytes, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var commits = new List<(string, string)>();
            if (doc.RootElement.TryGetProperty("commits", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in arr.EnumerateArray())
                {
                    string sha = c.TryGetProperty("sha", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
                    string message = c.TryGetProperty("commit", out var commit) && commit.TryGetProperty("message", out var m) &&
                                     m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                    commits.Add((message, sha));
                }
            }

            return (ChangelogParser.Build(commits), $"https://github.com/{repo}/compare/{Uri.EscapeDataString(baseTag)}...{Uri.EscapeDataString(headTag)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or IOException or InvalidDataException)
        {
            _logger.LogInformation(ex, "Could not load the changelog between {Base} and {Head}.", baseTag, headTag);
            return ([], null);
        }
    }

    private async Task<HttpResponseMessage> SendApiAsync(string url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApiTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.Content.Headers.ContentLength is long length && length > MaxApiResponseBytes)
        {
            response.Dispose();
            throw new InvalidDataException($"GitHub API response is too large ({length} bytes).");
        }
        return response;
    }

    public static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests) return true;
        if (response.StatusCode != HttpStatusCode.Forbidden) return false;
        return response.Headers.RetryAfter != null ||
               (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && values.FirstOrDefault() == "0");
    }

    public static DateTimeOffset? GetRateLimitReset(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.RetryAfter is { } retry)
        {
            if (retry.Delta is { } delta) return now + delta;
            if (retry.Date is { } date) return date;
        }
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long epoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        }
        return null;
    }

    private async Task RecordCheckAsync(DateTimeOffset checkedAt, string? latestVersion)
    {
        try
        {
            await _settings.UpdateSettingsAsync(s =>
            {
                s.UpdateLastCheckedAt = checkedAt;
                if (latestVersion != null) s.UpdateLatestKnownVersion = latestVersion;
            });
        }
        catch (Exception ex)
        {
            // Not fatal: the next automatic check simply happens sooner
            _logger.LogWarning(ex, "Could not store the update check time.");
        }
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An update state subscriber failed.");
        }
    }

    // ------------------------------------------------------------------ downloading

    public async Task<StagedUpdate> DownloadAsync(UpdateCheckResult update, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var host = HostInfo();
        var blocker = EvaluateBlocker(host);
        if (!update.CanInstall || blocker != UpdateInstallBlocker.None || update.Release == null || update.Asset == null || update.Platform == null)
        {
            throw new UpdateException(UpdateFailure.InstallNotSupported, $"In-app installation is not available ({(blocker != UpdateInstallBlocker.None ? blocker : update.InstallBlocker)}).");
        }

        var asset = update.Asset;
        var platform = update.Platform;
        if (!string.Equals(asset.Name, platform.ArchiveName, StringComparison.Ordinal))
        {
            throw new UpdateException(UpdateFailure.InstallNotSupported, $"Unexpected release asset {asset.Name}.");
        }
        if (update.ChecksumsAsset == null)
        {
            throw new UpdateException(UpdateFailure.ChecksumsMissing, $"Release {update.Release.TagName} has no {ReleaseParser.ChecksumsAssetName} file.");
        }
        if (!ReleaseParser.IsTrustedAssetUrl(asset.DownloadUrl) || !ReleaseParser.IsTrustedAssetUrl(update.ChecksumsAsset.DownloadUrl))
        {
            throw new UpdateException(UpdateFailure.UntrustedUrl, "Release asset URLs must be https downloads from GitHub.");
        }
        if (asset.Size > _options.MaxDownloadBytes)
        {
            throw new UpdateException(UpdateFailure.TooLarge, $"{asset.Name} is {asset.Size} bytes, over the {_options.MaxDownloadBytes} byte limit.");
        }

        string sums = await DownloadChecksumsAsync(update.ChecksumsAsset.DownloadUrl, cancellationToken);
        if (!ReleaseParser.ParseSha256Sums(sums).TryGetValue(asset.Name, out var expectedHash))
        {
            throw new UpdateException(UpdateFailure.ChecksumEntryMissing, $"{ReleaseParser.ChecksumsAssetName} has no (unambiguous) entry for {asset.Name}.");
        }

        EnsureUpdatesRoot();
        CleanupStaleStaging();

        string version = update.Release.Version.ToString();
        string stagingDir = Path.Combine(_updatesRoot, $"{version}-{Guid.NewGuid():N}");
        CreatePrivateDirectory(stagingDir);

        try
        {
            string archivePath = Path.Combine(stagingDir, asset.Name);
            await DownloadVerifiedAsync(asset, expectedHash, archivePath, progress, cancellationToken);

            string exe = await UpdatePackageExtractor.ExtractAsync(archivePath, platform, Path.Combine(stagingDir, "payload"), cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            File.Delete(archivePath);

            _logger.LogInformation("Update {Version} downloaded, verified (SHA-256 {Hash}) and staged in {Dir}.", version, expectedHash, stagingDir);
            return new StagedUpdate(version, stagingDir, exe, host.ProcessPath!);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException) _logger.LogWarning(ex, "Downloading update {Version} failed.", version);
            TryDeleteDirectory(stagingDir);
            if (ex is HttpRequestException or IOException)
            {
                throw new UpdateException(UpdateFailure.DownloadFailed, ex.Message, ex);
            }
            throw;
        }
    }

    private async Task<string> DownloadChecksumsAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendDownloadAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new UpdateException(UpdateFailure.ChecksumsMissing, $"{ReleaseParser.ChecksumsAssetName} could not be downloaded (HTTP 404).");
            }
            response.EnsureSuccessStatusCode();
            return await ReadCappedStringAsync(response.Content, MaxChecksumsBytes, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            throw new UpdateException(UpdateFailure.ChecksumsMissing, ex.Message, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException(UpdateFailure.ChecksumsMissing, $"{ReleaseParser.ChecksumsAssetName} could not be downloaded: {ex.Message}", ex);
        }
    }

    /// <summary>Streams the archive to disk while hashing it; size-capped; deleted unless the SHA-256 matches.</summary>
    private async Task DownloadVerifiedAsync(ReleaseAsset asset, string expectedHash, string destination, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        bool verified = false;
        try
        {
            using var response = await SendDownloadAsync(asset.DownloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            long max = _options.MaxDownloadBytes;
            long? total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : null);
            if (total > max)
            {
                throw new UpdateException(UpdateFailure.TooLarge, $"{asset.Name} is {total} bytes, over the {max} byte limit.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                long lastReport = -1;
                progress?.Report(new UpdateDownloadProgress(0, total));
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    received += read;
                    if (received > max)
                    {
                        throw new UpdateException(UpdateFailure.TooLarge, $"{asset.Name} exceeds the {max} byte limit.");
                    }
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                    if (received - lastReport >= 256 * 1024)
                    {
                        lastReport = received;
                        progress?.Report(new UpdateDownloadProgress(received, total));
                    }
                }
            }
            progress?.Report(new UpdateDownloadProgress(received, total ?? received));

            if (asset.Size > 0 && received != asset.Size)
            {
                throw new UpdateException(UpdateFailure.SizeMismatch, $"{asset.Name}: received {received} bytes, the release lists {asset.Size}.");
            }

            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException(UpdateFailure.ChecksumMismatch, $"SHA-256 of {asset.Name} is {actual}, {ReleaseParser.ChecksumsAssetName} lists {expectedHash}.");
            }
            verified = true;
        }
        finally
        {
            if (!verified) TryDeleteFile(destination);
        }
    }

    private async Task<HttpResponseMessage> SendDownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/octet-stream");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // Redirects are followed automatically: the final host must still be one of GitHub's release hosts
        var finalUri = response.RequestMessage?.RequestUri ?? new Uri(url);
        if (!ReleaseParser.IsTrustedAssetUri(finalUri))
        {
            response.Dispose();
            throw new UpdateException(UpdateFailure.UntrustedUrl, $"Download was redirected to an untrusted location ({finalUri.Host}).");
        }
        return response;
    }

    private static async Task<string> ReadCappedStringAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long declared && declared > maxBytes)
        {
            throw new InvalidDataException($"Response is too large ({declared} bytes).");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw new InvalidDataException($"Response exceeds {maxBytes} bytes.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    // ------------------------------------------------------------------ applying

    public Task ApplyAndRestartAsync(StagedUpdate staged, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var host = HostInfo();
        var blocker = EvaluateBlocker(host);
        if (blocker != UpdateInstallBlocker.None)
        {
            throw new UpdateException(UpdateFailure.InstallNotSupported, $"In-app installation is not available ({blocker}).");
        }

        var comparison = host.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetFullPath(staged.TargetExecutablePath), Path.GetFullPath(host.ProcessPath!), comparison))
        {
            throw new UpdateException(UpdateFailure.ApplyFailed, "The staged update targets a different executable.");
        }
        if (!Path.IsPathFullyQualified(_updatesRoot))
        {
            throw new UpdateException(UpdateFailure.InstallNotSupported, "No absolute per-user folder is available for updates.");
        }
        string root = Path.GetFullPath(_updatesRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string stagingDir = Path.GetFullPath(staged.StagingDirectory);
        string stagedExe = Path.GetFullPath(staged.StagedExecutablePath);
        if (!stagingDir.StartsWith(root, comparison) || !stagedExe.StartsWith(stagingDir + Path.DirectorySeparatorChar, comparison) || !File.Exists(stagedExe))
        {
            throw new UpdateException(UpdateFailure.ApplyFailed, "The staged update is missing or outside the updates folder.");
        }

        var plan = new UpdateScriptPlan(Environment.ProcessId, stagedExe, host.ProcessPath!, stagingDir, Path.Combine(_updatesRoot, "update.log"));
        var script = host.IsWindows ? UpdateScriptBuilder.BuildWindowsScript(plan) : UpdateScriptBuilder.BuildShellScript(plan);
        string scriptPath = Path.Combine(stagingDir, script.FileName);
        WriteScript(scriptPath, script.Content);

        var psi = CreateScriptStartInfo(scriptPath, host.IsWindows, stagingDir);
        foreach (var (key, value) in script.Environment)
        {
            psi.Environment[key] = value;
        }

        try
        {
            var start = _options.StartProcess ?? Process.Start;
            using var process = start(psi) ?? throw new UpdateException(UpdateFailure.ApplyFailed, "The update helper could not be started.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            throw new UpdateException(UpdateFailure.ApplyFailed, $"The update helper could not be started: {ex.Message}", ex);
        }

        _logger.LogInformation("Update helper {Script} started; closing CaissaDesk {Current} to install {Version}.", scriptPath, CurrentVersion, staged.Version);
        (_options.RequestShutdown ?? RequestShutdown)();
        return Task.CompletedTask;
    }

    public static ProcessStartInfo CreateScriptStartInfo(string scriptPath, bool isWindows, string workingDirectory)
    {
        ProcessStartInfo psi;
        if (isWindows)
        {
            // cmd.exe expands %VAR% on its command line: refuse such paths rather than risk mis-parsing
            if (scriptPath.Contains('%') || scriptPath.Contains('"'))
            {
                throw new UpdateException(UpdateFailure.InstallNotSupported, "The updates folder path contains characters cmd.exe cannot run safely.");
            }
            // /s: strip only the outer quotes, so the quoted script path survives special characters
            psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                Arguments = $"/d /s /c \"\"{scriptPath}\"\""
            };
        }
        else
        {
            psi = new ProcessStartInfo("/bin/sh");
            psi.ArgumentList.Add(scriptPath);
        }

        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = workingDirectory;
        return psi;
    }

    private static void WriteScript(string path, string content)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }
        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    /// <summary>Closes the Photino window so the app shuts down normally; hard exit only as a fallback.</summary>
    private void RequestShutdown()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            _logger.LogWarning("CaissaDesk did not exit after the window was closed; forcing exit for the update.");
            Environment.Exit(0);
        });

        try
        {
            var window = _windowProvider?.Window;
            if (window != null)
            {
                window.Close();
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not close the main window for the update.");
        }
        Environment.Exit(0);
    }

    // ------------------------------------------------------------------ helpers

    private UpdateHostInfo HostInfo() => (_options.HostInfo ?? UpdateHostInfo.Current)();

    private UpdateInstallBlocker EvaluateBlocker(UpdateHostInfo host) =>
        UpdateInstallability.Evaluate(host, _options.FileExists ?? File.Exists, _options.IsDirectoryWritable ?? UpdateInstallability.IsDirectoryWritable);

    /// <summary>The updates folder is per-user (LocalApplicationData) and, on Unix, accessible to the owner only.</summary>
    private void EnsureUpdatesRoot()
    {
        if (!Path.IsPathFullyQualified(_updatesRoot))
        {
            throw new UpdateException(UpdateFailure.InstallNotSupported, $"No absolute per-user folder is available for updates ('{_updatesRoot}').");
        }
        CreatePrivateDirectory(_updatesRoot);
        var info = new DirectoryInfo(_updatesRoot);
        if (info.LinkTarget != null)
        {
            throw new UpdateException(UpdateFailure.ApplyFailed, $"The updates folder {_updatesRoot} is a symbolic link.");
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_updatesRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private void CleanupStaleStaging()
    {
        try
        {
            var cutoff = _time.GetUtcNow() - StaleStagingAge;
            foreach (var dir in new DirectoryInfo(_updatesRoot).EnumerateDirectories())
            {
                if (dir.LinkTarget == null && dir.LastWriteTimeUtc < cutoff.UtcDateTime)
                {
                    TryDeleteDirectory(dir.FullName);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not clean up old update folders.");
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete {Path}.", path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
