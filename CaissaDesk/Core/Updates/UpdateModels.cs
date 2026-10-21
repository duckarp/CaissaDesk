namespace CaissaDesk.Core.Updates;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>A GitHub release whose tag parsed as a semantic version.</summary>
public sealed record ReleaseInfo(
    string TagName,
    SemVersion Version,
    string Title,
    string Body,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    IReadOnlyList<ReleaseAsset> Assets);

public enum ArchiveKind
{
    TarGz,
    Zip
}

/// <summary>What the release workflow publishes for one OS/architecture (see .github/workflows/release.yml).</summary>
public sealed record UpdatePlatform(string ArchiveName, string ExecutableName, ArchiveKind Kind);

public enum ChangelogCategory
{
    Feature,
    Fix,
    Performance,
    Update,
    Refactor,
    Docs,
    Tests,
    Build
}

public sealed record ChangelogEntry(ChangelogCategory Category, string Scope, string Description, string Sha);

public sealed record ChangelogGroup(ChangelogCategory Category, IReadOnlyList<ChangelogEntry> Entries);

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NoReleases,
    RateLimited,
    InvalidRepository,
    Failed
}

/// <summary>Why the in-app installer cannot be used; the dialog then offers the release page instead.</summary>
public enum UpdateInstallBlocker
{
    None,
    UnsupportedPlatform,
    DevelopmentBuild,
    AppImage,
    UnknownExecutable,
    DirectoryNotWritable,
    NoAssetForPlatform,
    NoChecksums
}

public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string Repository { get; init; } = "";
    public DateTimeOffset CheckedAt { get; init; }

    public ReleaseInfo? Release { get; init; }
    public ReleaseAsset? Asset { get; init; }
    public ReleaseAsset? ChecksumsAsset { get; init; }
    public UpdatePlatform? Platform { get; init; }
    public UpdateInstallBlocker InstallBlocker { get; init; }

    /// <summary>Release body without the GitHub "Full Changelog" link lines.</summary>
    public string AuthorNotes { get; init; } = "";
    public IReadOnlyList<ChangelogGroup> Changelog { get; init; } = [];
    public string? CompareUrl { get; init; }

    /// <summary>Release page on github.com (built from repository + tag, never taken from the API response).</summary>
    public string? ReleasePageUrl { get; init; }

    public DateTimeOffset? RateLimitResetAt { get; init; }

    /// <summary>Short, sanitized technical detail for failures (already logged in full).</summary>
    public string? ErrorDetail { get; init; }

    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable && Release != null;
    public bool CanInstall => IsUpdateAvailable && InstallBlocker == UpdateInstallBlocker.None && Asset != null && Platform != null;
}

public readonly record struct UpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public int? Percent => TotalBytes is > 0 ? (int)Math.Clamp(BytesReceived * 100 / TotalBytes.Value, 0, 100) : null;
}

/// <summary>A verified, extracted update waiting to be applied.</summary>
public sealed record StagedUpdate(
    string Version,
    string StagingDirectory,
    string StagedExecutablePath,
    string TargetExecutablePath);

public enum UpdateFailure
{
    InstallNotSupported,
    UntrustedUrl,
    ChecksumsMissing,
    ChecksumEntryMissing,
    ChecksumMismatch,
    TooLarge,
    SizeMismatch,
    InvalidArchive,
    DownloadFailed,
    ApplyFailed
}

/// <summary>Update failure with a machine-readable reason the UI localizes; Message carries the technical detail.</summary>
public sealed class UpdateException(UpdateFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public UpdateFailure Failure { get; } = failure;
}
