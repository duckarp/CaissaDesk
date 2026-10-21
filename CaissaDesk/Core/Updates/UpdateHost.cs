namespace CaissaDesk.Core.Updates;

/// <summary>Facts about the running installation that decide whether the in-app installer may be used.</summary>
public sealed record UpdateHostInfo(string? ProcessPath, string? AppImagePath, UpdatePlatform? Platform, bool IsWindows)
{
    public static UpdateHostInfo Current() => new(
        Environment.ProcessPath,
        Environment.GetEnvironmentVariable("APPIMAGE"),
        ReleaseParser.GetCurrentPlatform(),
        OperatingSystem.IsWindows());
}

public static class UpdateInstallability
{
    /// <summary>
    /// Only a published single-file build in a writable directory is replaced in place. Everything else
    /// (dotnet run / framework-dependent output, AppImage, unknown platform, read-only install such as
    /// /usr/bin or Program Files) gets the "open release page" fallback instead.
    /// </summary>
    public static UpdateInstallBlocker Evaluate(UpdateHostInfo host, Func<string, bool> fileExists, Func<string, bool> isDirectoryWritable)
    {
        if (host.Platform == null) return UpdateInstallBlocker.UnsupportedPlatform;

        // Inside an AppImage ProcessPath points into a read-only squashfs mount; replacing $APPIMAGE itself
        // would need a different package format, so AppImage installs update through the release page.
        if (!string.IsNullOrWhiteSpace(host.AppImagePath)) return UpdateInstallBlocker.AppImage;

        if (string.IsNullOrWhiteSpace(host.ProcessPath) || !Path.IsPathFullyQualified(host.ProcessPath))
        {
            return UpdateInstallBlocker.UnknownExecutable;
        }

        var comparison = host.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string fileName = Path.GetFileName(host.ProcessPath);
        // "dotnet" host (dotnet CaissaDesk.dll) or a renamed binary: not the published single-file layout
        if (!string.Equals(fileName, host.Platform.ExecutableName, comparison))
        {
            return UpdateInstallBlocker.DevelopmentBuild;
        }

        string? directory = Path.GetDirectoryName(host.ProcessPath);
        if (string.IsNullOrEmpty(directory)) return UpdateInstallBlocker.UnknownExecutable;

        // dotnet run / build output: apphost next to CaissaDesk.dll. The single-file build bundles the dll.
        if (fileExists(Path.Combine(directory, "CaissaDesk.dll"))) return UpdateInstallBlocker.DevelopmentBuild;

        if (!isDirectoryWritable(directory)) return UpdateInstallBlocker.DirectoryNotWritable;

        return UpdateInstallBlocker.None;
    }

    /// <summary>Probes by creating (and immediately deleting) a uniquely named file.</summary>
    public static bool IsDirectoryWritable(string directory)
    {
        try
        {
            string probe = Path.Combine(directory, $".caissadesk-write-test-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
