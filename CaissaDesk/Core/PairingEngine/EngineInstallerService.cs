using System.Formats.Tar;
using System.IO.Compression;
using CaissaDesk.Core.Settings;
using CaissaDesk.Data;

namespace CaissaDesk.Core.PairingEngine;

public class EngineInstallProgress
{
    public string Message { get; set; } = string.Empty;
    public int? Percent { get; set; }
}

public class EngineInstallResult
{
    public bool Success { get; set; }
    public string? InstalledPath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Note { get; set; }

    public static EngineInstallResult Ok(string path, string? note = null) => new()
    {
        Success = true,
        InstalledPath = path,
        Note = note
    };

    public static EngineInstallResult Fail(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}

public interface IEngineInstallerService
{
    Task<EngineInstallResult> InstallEngineAsync(string engineId, IProgress<EngineInstallProgress>? progress = null, CancellationToken cancellationToken = default);
}

public class EngineInstallerService : IEngineInstallerService
{
    private readonly HttpClient _httpClient;
    private readonly IPairingEngineRegistry _engineRegistry;
    private readonly IAppSettingsService _appSettings;

    public EngineInstallerService(
        HttpClient httpClient,
        IPairingEngineRegistry engineRegistry,
        IAppSettingsService appSettings)
    {
        _httpClient = httpClient;
        _engineRegistry = engineRegistry;
        _appSettings = appSettings;
    }

    public async Task<EngineInstallResult> InstallEngineAsync(
        string engineId,
        IProgress<EngineInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string normId = engineId.Trim().ToLowerInvariant();

        return normId switch
        {
            "bbp" => await InstallBbpAsync(progress, cancellationToken),
            "javafo" => await InstallJaVaFoAsync(progress, cancellationToken),
            _ => EngineInstallResult.Fail($"Automatic installation is not supported for engine '{engineId}'.")
        };
    }

    private async Task<EngineInstallResult> InstallBbpAsync(
        IProgress<EngineInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        string enginesDir = GetEnginesDirectory();
        bool isWindows = OperatingSystem.IsWindows();

        string downloadUrl = isWindows
            ? "https://github.com/BieremaBoyzProgramming/bbpPairings/releases/download/v6.0.0/bbpPairings-v6.0.0-x86_64-pc-windows.zip"
            : "https://github.com/BieremaBoyzProgramming/bbpPairings/releases/download/v6.0.0/bbpPairings-v6.0.0-x86_64-pc-linux.tar.gz";

        string targetFileName = isWindows ? "bbpPairings.exe" : "bbpPairings";
        string targetFilePath = Path.Combine(enginesDir, targetFileName);

        try
        {
            progress?.Report(new EngineInstallProgress { Message = "Downloading bbpPairings v6.0.0 package...", Percent = 15 });

            string tempArchive = Path.Combine(Path.GetTempPath(), $"bbp_dl_{Guid.NewGuid():N}.tmp");

            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(tempArchive, FileMode.Create, FileAccess.Write, FileShare.None);
                await contentStream.CopyToAsync(fileStream, cancellationToken);
            }

            progress?.Report(new EngineInstallProgress { Message = "Extracting binary...", Percent = 70 });

            if (isWindows)
            {
                using var zip = ZipFile.OpenRead(tempArchive);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("bbpPairings.exe", StringComparison.OrdinalIgnoreCase) ||
                                                           e.Name.Equals("bbpPairings", StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    return EngineInstallResult.Fail("Could not locate bbpPairings executable inside downloaded archive.");
                }

                entry.ExtractToFile(targetFilePath, overwrite: true);
            }
            else
            {
                await using var fs = File.OpenRead(tempArchive);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                using var tar = new TarReader(gz);

                TarEntry? entry;
                bool found = false;
                while ((entry = await tar.GetNextEntryAsync(cancellationToken: cancellationToken)) != null)
                {
                    if (entry.Name.EndsWith("bbpPairings.exe", StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.EndsWith("bbpPairings", StringComparison.OrdinalIgnoreCase))
                    {
                        await entry.ExtractToFileAsync(targetFilePath, overwrite: true, cancellationToken);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return EngineInstallResult.Fail("Could not locate bbpPairings binary inside downloaded tar.gz archive.");
                }

                // Make binary executable on Linux/Unix
                File.SetUnixFileMode(targetFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            try { File.Delete(tempArchive); } catch { }

            progress?.Report(new EngineInstallProgress { Message = "Configuring engine registry...", Percent = 90 });

            // Update app settings and engine registry
            var settings = _appSettings.Settings.Clone();
            settings.CustomBbpPath = targetFilePath;
            await _appSettings.SaveSettingsAsync(settings);

            // Pass every saved path: reloading only one would reset the other engines to auto-detection
            _engineRegistry.ReloadEngines(settings.CustomBbpPath, settings.CustomJavaPath, settings.CustomJavafoJarPath);

            progress?.Report(new EngineInstallProgress { Message = "bbpPairings installed and ready!", Percent = 100 });
            return EngineInstallResult.Ok(targetFilePath);
        }
        catch (Exception ex)
        {
            return EngineInstallResult.Fail($"Failed to download/install bbpPairings: {ex.Message}");
        }
    }

    private async Task<EngineInstallResult> InstallJaVaFoAsync(
        IProgress<EngineInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        string enginesDir = GetEnginesDirectory();
        string targetFilePath = Path.Combine(enginesDir, "javafo.jar");
        string downloadUrl = "https://www.rrweb.org/javafo/current/javafo.jar";

        try
        {
            progress?.Report(new EngineInstallProgress { Message = "Downloading javafo.jar (FIDE reference)...", Percent = 25 });

            string tempJar = Path.Combine(Path.GetTempPath(), $"javafo_dl_{Guid.NewGuid():N}.tmp");

            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(tempJar, FileMode.Create, FileAccess.Write, FileShare.None);
                await contentStream.CopyToAsync(fileStream, cancellationToken);
            }

            progress?.Report(new EngineInstallProgress { Message = "Installing javafo.jar...", Percent = 75 });
            File.Move(tempJar, targetFilePath, overwrite: true);

            var settings = _appSettings.Settings.Clone();
            settings.CustomJavafoJarPath = targetFilePath;
            await _appSettings.SaveSettingsAsync(settings);

            _engineRegistry.ReloadEngines(settings.CustomBbpPath, settings.CustomJavaPath, settings.CustomJavafoJarPath);

            // Check if Java runtime is available
            var javafoEngine = _engineRegistry.AllEngines.OfType<JaVaFoPairingEngine>().FirstOrDefault();
            bool hasJava = !string.IsNullOrEmpty(javafoEngine?.JavaPath);

            string? note = hasJava
                ? null
                : "javafo.jar was installed, but Java (JRE 8+) was not detected on your system. Please install Java to enable JaVaFo.";

            progress?.Report(new EngineInstallProgress { Message = "javafo.jar installed!", Percent = 100 });
            return EngineInstallResult.Ok(targetFilePath, note);
        }
        catch (Exception ex)
        {
            return EngineInstallResult.Fail($"Failed to download/install JaVaFo: {ex.Message}");
        }
    }

    private static string GetEnginesDirectory()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaissaDesk",
            "engines");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
