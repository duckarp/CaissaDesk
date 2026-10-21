using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CaissaDesk.Core.Settings;
using CaissaDesk.Data;
using CaissaDesk.Core;

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

/// <summary>A pinned download: exact release URL plus the SHA-256 of the file published there.</summary>
public sealed record EngineAsset(string Version, string Url, string Sha256);

public class EngineInstallerService : IEngineInstallerService
{
    // Pinned release assets. Bumping a version means updating the URL *and* the hash together; a download
    // whose SHA-256 differs is rejected (fail closed) and nothing is installed.
    // The bbpPairings hashes equal the SHA-256 digests GitHub publishes for the v6.0.0 release assets.
    public static readonly EngineAsset BbpWindowsX64 = new(
        "6.0.0",
        "https://github.com/BieremaBoyzProgramming/bbpPairings/releases/download/v6.0.0/bbpPairings-v6.0.0-x86_64-pc-windows.zip",
        "d2bfc61cbd291a5458f18adccb43b19b6f1be40a9c0cc86a5142b605298dc0d7");

    public static readonly EngineAsset BbpLinuxX64 = new(
        "6.0.0",
        "https://github.com/BieremaBoyzProgramming/bbpPairings/releases/download/v6.0.0/bbpPairings-v6.0.0-x86_64-pc-linux.tar.gz",
        "bffd2d5a4dc9d86eb3d9886339e8ca446d88683f77559f0889ea0d2040e7d827");

    // JaVaFo 2.2 from the versioned directory on the author's site. rrweb.org publishes no checksum: this
    // hash was computed from that file (identical to what /current/ served at the time it was pinned).
    public static readonly EngineAsset JaVaFo = new(
        "2.2",
        "https://www.rrweb.org/javafo/2.2/javafo.jar",
        "6d1eef8f83d433a5d6386a0a3bba8326d8d6ab654991e2be00e0c53ba49b247c");

    /// <summary>Upper bound on a download; the real assets are well under 1 MB.</summary>
    public const long MaxDownloadBytes = 32L * 1024 * 1024;

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

    /// <summary>
    /// Streams <paramref name="asset"/> to <paramref name="destinationPath"/> while hashing it. Throws
    /// <see cref="InvalidDataException"/> (after deleting the partial file) when no valid hash is pinned, the
    /// SHA-256 does not match, or the download exceeds <see cref="MaxDownloadBytes"/>.
    /// </summary>
    public static async Task DownloadVerifiedAsync(HttpClient httpClient, EngineAsset asset, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!IsValidSha256Hex(asset.Sha256))
        {
            throw new InvalidDataException($"No valid SHA-256 is pinned for {asset.Url}; refusing to download it.");
        }

        bool verified = false;
        try
        {
            using var response = await httpClient.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long declared && declared > MaxDownloadBytes)
            {
                throw new InvalidDataException($"Download of {asset.Url} exceeds the {MaxDownloadBytes / (1024 * 1024)} MB limit.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                    {
                        throw new InvalidDataException($"Download of {asset.Url} exceeds the {MaxDownloadBytes / (1024 * 1024)} MB limit.");
                    }
                    hash.AppendData(buffer, 0, read);
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Integrity check failed for {asset.Url}: expected SHA-256 {asset.Sha256.ToLowerInvariant()}, got {actual}. The file was not installed.");
            }

            verified = true;
        }
        finally
        {
            if (!verified)
            {
                TryDelete(destinationPath);
            }
        }
    }

    private static bool IsValidSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static EngineAsset? GetBbpAssetForPlatform()
    {
        var arch = RuntimeInformation.OSArchitecture;
        // Windows on Arm runs the x64 build under emulation
        if (OperatingSystem.IsWindows() && arch is Architecture.X64 or Architecture.Arm64) return BbpWindowsX64;
        if (OperatingSystem.IsLinux() && arch == Architecture.X64) return BbpLinuxX64;
        return null;
    }

    private async Task<EngineInstallResult> InstallBbpAsync(
        IProgress<EngineInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var asset = GetBbpAssetForPlatform();
        if (asset == null)
        {
            return EngineInstallResult.Fail(
                $"No prebuilt bbpPairings binary is available for {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}). Build it from source and set its path manually.");
        }

        string enginesDir = GetEnginesDirectory();
        bool isWindows = OperatingSystem.IsWindows();
        string targetFileName = isWindows ? "bbpPairings.exe" : "bbpPairings";
        string targetFilePath = Path.Combine(enginesDir, targetFileName);

        string tempArchive = Path.Combine(Path.GetTempPath(), $"bbp_dl_{Guid.NewGuid():N}.tmp");
        string tempBinary = Path.Combine(enginesDir, $".bbp_extract_{Guid.NewGuid():N}.tmp");

        try
        {
            progress?.Report(new EngineInstallProgress { Message = $"Downloading bbpPairings v{asset.Version} package...", Percent = 15 });
            await DownloadVerifiedAsync(_httpClient, asset, tempArchive, cancellationToken);

            progress?.Report(new EngineInstallProgress { Message = "Extracting binary...", Percent = 70 });

            bool found = asset.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? ExtractFromZip(tempArchive, tempBinary)
                : await ExtractFromTarGzAsync(tempArchive, tempBinary, cancellationToken);

            if (!found)
            {
                return EngineInstallResult.Fail("Could not locate the bbpPairings executable inside the downloaded archive.");
            }

            if (!isWindows)
            {
                File.SetUnixFileMode(tempBinary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            File.Move(tempBinary, targetFilePath, overwrite: true);

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return EngineInstallResult.Fail($"Failed to download/install bbpPairings: {ex.Message}");
        }
        finally
        {
            TryDelete(tempArchive);
            TryDelete(tempBinary);
        }
    }

    private static bool IsBbpExecutableName(string entryPath)
    {
        string name = Path.GetFileName(entryPath.Replace('\\', '/').TrimEnd('/'));
        return name.Equals("bbpPairings.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("bbpPairings", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExtractFromZip(string archivePath, string destinationPath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        // Directory entries have an empty Name; the entry is written only to our own temp path
        var entry = zip.Entries.FirstOrDefault(e => e.Name.Length > 0 && IsBbpExecutableName(e.FullName));
        if (entry == null) return false;

        entry.ExtractToFile(destinationPath, overwrite: true);
        return true;
    }

    /// <summary>
    /// Copies the first regular-file entry named bbpPairings(.exe) to <paramref name="destinationPath"/>.
    /// Symbolic/hard links, devices and other entry types are skipped.
    /// </summary>
    public static async Task<bool> ExtractFromTarGzAsync(string archivePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        await using var fs = File.OpenRead(archivePath);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);
        await using var tar = new TarReader(gz);

        TarEntry? entry;
        while ((entry = await tar.GetNextEntryAsync(cancellationToken: cancellationToken)) != null)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
            if (!IsBbpExecutableName(entry.Name) || entry.DataStream == null) continue;

            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await entry.DataStream.CopyToAsync(output, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<EngineInstallResult> InstallJaVaFoAsync(
        IProgress<EngineInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        string enginesDir = GetEnginesDirectory();
        string targetFilePath = Path.Combine(enginesDir, "javafo.jar");
        string tempJar = Path.Combine(Path.GetTempPath(), $"javafo_dl_{Guid.NewGuid():N}.tmp");

        try
        {
            progress?.Report(new EngineInstallProgress { Message = $"Downloading javafo.jar {JaVaFo.Version} (FIDE reference)...", Percent = 25 });
            await DownloadVerifiedAsync(_httpClient, JaVaFo, tempJar, cancellationToken);

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return EngineInstallResult.Fail($"Failed to download/install JaVaFo: {ex.Message}");
        }
        finally
        {
            TryDelete(tempJar);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string GetEnginesDirectory()
    {
        string dir = Path.Combine(
            AppPaths.LocalAppDataRoot,
            "CaissaDesk",
            "engines");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
