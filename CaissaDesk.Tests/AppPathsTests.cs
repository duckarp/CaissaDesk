using CaissaDesk.Core;

namespace CaissaDesk.Tests;

public class AppPathsTests
{
    [Fact]
    public void UsesPlatformFolder_WhenAvailable()
    {
        string root = OperatingSystem.IsWindows() ? @"C:\Users\a\AppData\Local" : "/home/a/.local/share";

        var result = AppPaths.ResolveLocalAppDataRoot((_, _) => root, _ => null);

        Assert.Equal(root, result);
    }

    [Fact]
    public void FreshProfile_EmptyFolderPath_NeverFallsBackToRelativePath()
    {
        // GetFolderPath returns "" for a folder that doesn't exist yet; that must not become a relative path
        var result = AppPaths.ResolveLocalAppDataRoot(
            (folder, _) => folder == Environment.SpecialFolder.UserProfile
                ? (OperatingSystem.IsWindows() ? @"C:\Users\a" : "/home/a")
                : "",
            name => name == "HOME" ? "/home/a" : null);

        Assert.True(Path.IsPathRooted(result));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("/home/a/.local/share", result);
        }
    }

    [Fact]
    public void PrefersXdgDataHome_OnUnix()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = AppPaths.ResolveLocalAppDataRoot((_, _) => "", name => name == "XDG_DATA_HOME" ? "/data/xdg" : "/home/a");

        Assert.Equal("/data/xdg", result);
    }

    [Fact]
    public void IgnoresRelativeValues_AndEndsAbsolute()
    {
        var result = AppPaths.ResolveLocalAppDataRoot((_, _) => "relative/dir", _ => "also/relative");

        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void LocalAppDataRoot_IsAbsolute()
    {
        Assert.True(Path.IsPathRooted(AppPaths.LocalAppDataRoot));
    }
}
