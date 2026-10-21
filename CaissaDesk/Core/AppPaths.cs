namespace CaissaDesk.Core;

/// <summary>Resolves the per-user local data folder (database, settings, logs, engines, updates).</summary>
public static class AppPaths
{
    private static readonly Lazy<string> LocalAppDataRootLazy = new(() => ResolveLocalAppDataRoot());

    /// <summary>
    /// The platform's local application data folder (e.g. %LOCALAPPDATA% or ~/.local/share), always absolute.
    /// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> returns "" when that folder does not
    /// exist yet (fresh Linux profile), which would silently make every data path relative to the working directory.
    /// </summary>
    public static string LocalAppDataRoot => LocalAppDataRootLazy.Value;

    public static string ResolveLocalAppDataRoot(
        Func<Environment.SpecialFolder, Environment.SpecialFolderOption, string>? getFolderPath = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        getFolderPath ??= Environment.GetFolderPath;
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        string? candidate = getFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (IsUsable(candidate)) return candidate!;

        if (!OperatingSystem.IsWindows())
        {
            candidate = getEnvironmentVariable("XDG_DATA_HOME");
            if (IsUsable(candidate)) return candidate!;

            string? home = getEnvironmentVariable("HOME");
            if (IsUsable(home)) return Path.Combine(home!, ".local", "share");
        }

        string profile = getFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        if (IsUsable(profile)) return Path.Combine(profile, OperatingSystem.IsWindows() ? "AppData\\Local" : ".local/share");

        return Path.GetTempPath();
    }

    private static bool IsUsable(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);
}
