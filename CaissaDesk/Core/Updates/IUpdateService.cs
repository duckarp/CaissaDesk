namespace CaissaDesk.Core.Updates;

public interface IUpdateService
{
    string CurrentVersion { get; }

    /// <summary>Result of the most recent check (null until one has finished).</summary>
    UpdateCheckResult? LastResult { get; }

    bool IsChecking { get; }

    /// <summary>Raised (on a background thread) when a check starts or finishes.</summary>
    event Action? StateChanged;

    /// <summary>Checks GitHub now, regardless of the startup setting and the minimum interval.</summary>
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Automatic check: honors "check on startup" and the minimum interval; null when skipped.</summary>
    Task<UpdateCheckResult?> CheckIfDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads, verifies (SHA256SUMS) and extracts the update into a private staging folder.</summary>
    Task<StagedUpdate> DownloadAsync(UpdateCheckResult update, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Starts the helper script that swaps the executable, then closes the app.</summary>
    Task ApplyAndRestartAsync(StagedUpdate staged, CancellationToken cancellationToken = default);
}
