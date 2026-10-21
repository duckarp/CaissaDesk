using Microsoft.Extensions.Logging;

namespace CaissaDesk.Core.PairingEngine;

/// <summary>
/// The install flow shared by every "Download &amp; install" button (tournament workbench and the settings
/// control center): unexpected exceptions become a failed <see cref="EngineInstallResult"/> and are logged,
/// so callers only have to branch on <see cref="EngineInstallResult.Success"/>.
/// </summary>
public static class EngineInstallerExtensions
{
    public static async Task<EngineInstallResult> TryInstallEngineAsync(
        this IEngineInstallerService installer,
        string engineId,
        ILogger logger,
        IProgress<EngineInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await installer.InstallEngineAsync(engineId, progress, cancellationToken);
            if (!result.Success)
            {
                logger.LogWarning("Installing pairing engine {EngineId} failed: {Error}", engineId, result.ErrorMessage);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Installing pairing engine {EngineId} threw.", engineId);
            return EngineInstallResult.Fail(ex.Message);
        }
    }
}
