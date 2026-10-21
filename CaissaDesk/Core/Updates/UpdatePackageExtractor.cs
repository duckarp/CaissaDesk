using System.Formats.Tar;
using System.IO.Compression;

namespace CaissaDesk.Core.Updates;

/// <summary>
/// Extracts the single application executable from a release archive into a private staging folder.
/// Strict whitelist: the archive may contain only that one regular file (plus harmless directory and
/// metadata entries). Path traversal, absolute paths, links, devices, extra files, duplicates and
/// oversized payloads reject the whole archive; nothing is ever extracted over the install directory.
/// </summary>
public static class UpdatePackageExtractor
{
    /// <summary>Upper bound on the extracted executable (the self-contained single-file build is ~100 MB).</summary>
    public const long MaxExtractedBytes = 400L * 1024 * 1024;

    public static Task<string> ExtractAsync(string archivePath, UpdatePlatform platform, string destinationDirectory, CancellationToken cancellationToken = default) =>
        platform.Kind == ArchiveKind.Zip
            ? ExtractZipAsync(archivePath, platform.ExecutableName, destinationDirectory, MaxExtractedBytes, cancellationToken)
            : ExtractTarGzAsync(archivePath, platform.ExecutableName, destinationDirectory, MaxExtractedBytes, cancellationToken);

    public static async Task<string> ExtractTarGzAsync(string archivePath, string executableName, string destinationDirectory, long maxBytes, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        string destination = Path.Combine(destinationDirectory, executableName);
        bool found = false;

        try
        {
            await using var fs = File.OpenRead(archivePath);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            await using var tar = new TarReader(gz);

            TarEntry? entry;
            while ((entry = await tar.GetNextEntryAsync(cancellationToken: cancellationToken)) != null)
            {
                string name = NormalizeEntryName(entry.Name);
                switch (entry.EntryType)
                {
                    case TarEntryType.GlobalExtendedAttributes:
                    case TarEntryType.ExtendedAttributes:
                        continue;
                    case TarEntryType.Directory:
                        EnsureSafeDirectoryName(name, entry.Name);
                        continue;
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                        break;
                    default:
                        throw Invalid($"entry '{entry.Name}' has unsupported type {entry.EntryType}");
                }

                if (!string.Equals(name, executableName, StringComparison.Ordinal))
                {
                    throw Invalid($"unexpected file '{entry.Name}'");
                }
                if (found) throw Invalid($"duplicate entry '{entry.Name}'");
                if (entry.Length > maxBytes) throw Invalid($"'{entry.Name}' is larger than the {maxBytes / (1024 * 1024)} MB limit");
                if (entry.DataStream == null) throw Invalid($"'{entry.Name}' has no content");

                await CopyCappedAsync(entry.DataStream, destination, maxBytes, cancellationToken);
                found = true;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or EndOfStreamException)
        {
            TryDelete(destination);
            throw Invalid($"the archive is corrupt ({ex.Message})", ex);
        }
        catch
        {
            TryDelete(destination);
            throw;
        }

        return Finish(found, destination, executableName);
    }

    public static async Task<string> ExtractZipAsync(string archivePath, string executableName, string destinationDirectory, long maxBytes, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        string destination = Path.Combine(destinationDirectory, executableName);
        bool found = false;

        try
        {
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries)
            {
                string name = NormalizeEntryName(entry.FullName);

                // Unix mode lives in the high 16 bits of the external attributes (zips made on Unix)
                int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixType is not (0 or 0x8000 or 0x4000))
                {
                    throw Invalid($"entry '{entry.FullName}' is a link or special file");
                }

                bool isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || unixType == 0x4000;
                if (isDirectory)
                {
                    EnsureSafeDirectoryName(name, entry.FullName);
                    continue;
                }

                // Windows file names are case-insensitive; the comparison still requires the exact name
                if (!string.Equals(name, executableName, StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid($"unexpected file '{entry.FullName}'");
                }
                if (found) throw Invalid($"duplicate entry '{entry.FullName}'");
                if (entry.Length > maxBytes) throw Invalid($"'{entry.FullName}' is larger than the {maxBytes / (1024 * 1024)} MB limit");

                await using var source = entry.Open();
                await CopyCappedAsync(source, destination, maxBytes, cancellationToken);
                found = true;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or EndOfStreamException)
        {
            TryDelete(destination);
            throw Invalid($"the archive is corrupt ({ex.Message})", ex);
        }
        catch
        {
            TryDelete(destination);
            throw;
        }

        return Finish(found, destination, executableName);
    }

    /// <summary>Strips "./" prefixes and trailing slashes; rejects traversal, rooted and odd names.</summary>
    internal static string NormalizeEntryName(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Contains('\0')) throw Invalid("entry with an empty or invalid name");
        if (raw.Contains('\\')) throw Invalid($"entry '{raw}' uses backslashes");
        if (raw.StartsWith('/') || (raw.Length >= 2 && raw[1] == ':')) throw Invalid($"entry '{raw}' has an absolute path");

        var segments = raw.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".").ToList();
        if (segments.Any(s => s == "..")) throw Invalid($"entry '{raw}' escapes the archive root");
        return string.Join('/', segments);
    }

    private static void EnsureSafeDirectoryName(string normalized, string raw)
    {
        // Normalization already rejected traversal; the root directory ("./") is the only common one
        if (normalized.Length > 0 && normalized.Split('/').Length > 8) throw Invalid($"directory '{raw}' is nested too deeply");
    }

    private static async Task CopyCappedAsync(Stream source, string destination, long maxBytes, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes) throw Invalid($"the executable is larger than the {maxBytes / (1024 * 1024)} MB limit");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string Finish(bool found, string destination, string executableName)
    {
        if (!found) throw Invalid($"'{executableName}' was not found in the archive");
        if (new FileInfo(destination).Length == 0)
        {
            TryDelete(destination);
            throw Invalid($"'{executableName}' is empty");
        }
        return destination;
    }

    private static UpdateException Invalid(string detail, Exception? inner = null) =>
        new(UpdateFailure.InvalidArchive, $"Rejected update archive: {detail}.", inner);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
