using System.Globalization;
using CaissaDesk.Core.Updates;

namespace CaissaDesk.Localization;

/// <summary>Display texts for the updater (keys stay literal so the key-coverage test sees them).</summary>
public static class UpdateLocalization
{
    public static string CategoryLabel(this ILocalizationService loc, ChangelogCategory category) => category switch
    {
        ChangelogCategory.Feature => loc["Update_Category_Feature"],
        ChangelogCategory.Fix => loc["Update_Category_Fix"],
        ChangelogCategory.Performance => loc["Update_Category_Performance"],
        ChangelogCategory.Refactor => loc["Update_Category_Refactor"],
        ChangelogCategory.Docs => loc["Update_Category_Docs"],
        ChangelogCategory.Tests => loc["Update_Category_Tests"],
        ChangelogCategory.Build => loc["Update_Category_Build"],
        _ => loc["Update_Category_Update"]
    };

    public static string BlockerText(this ILocalizationService loc, UpdateInstallBlocker blocker) => blocker switch
    {
        UpdateInstallBlocker.UnsupportedPlatform => loc["Update_Blocker_UnsupportedPlatform"],
        UpdateInstallBlocker.DevelopmentBuild => loc["Update_Blocker_DevelopmentBuild"],
        UpdateInstallBlocker.AppImage => loc["Update_Blocker_AppImage"],
        UpdateInstallBlocker.UnknownExecutable => loc["Update_Blocker_UnknownExecutable"],
        UpdateInstallBlocker.DirectoryNotWritable => loc["Update_Blocker_DirectoryNotWritable"],
        UpdateInstallBlocker.NoAssetForPlatform => loc["Update_Blocker_NoAssetForPlatform"],
        UpdateInstallBlocker.NoChecksums => loc["Update_Blocker_NoChecksums"],
        _ => ""
    };

    public static string FailureText(this ILocalizationService loc, UpdateFailure failure) => failure switch
    {
        UpdateFailure.InstallNotSupported => loc["Update_Error_InstallNotSupported"],
        UpdateFailure.UntrustedUrl => loc["Update_Error_UntrustedUrl"],
        UpdateFailure.ChecksumsMissing => loc["Update_Error_ChecksumsMissing"],
        UpdateFailure.ChecksumEntryMissing => loc["Update_Error_ChecksumEntryMissing"],
        UpdateFailure.ChecksumMismatch => loc["Update_Error_ChecksumMismatch"],
        UpdateFailure.TooLarge => loc["Update_Error_TooLarge"],
        UpdateFailure.SizeMismatch => loc["Update_Error_SizeMismatch"],
        UpdateFailure.InvalidArchive => loc["Update_Error_InvalidArchive"],
        UpdateFailure.ApplyFailed => loc["Update_Error_ApplyFailed"],
        _ => loc["Update_Error_DownloadFailed"]
    };

    /// <summary>One-line summary of a check result for the settings page and toasts.</summary>
    public static string StatusText(this ILocalizationService loc, UpdateCheckResult? result)
    {
        if (result == null) return loc["Update_Status_Never"];
        return result.Status switch
        {
            UpdateCheckStatus.UpToDate => loc["Update_Status_UpToDate", result.CurrentVersion],
            UpdateCheckStatus.UpdateAvailable => loc["Update_Status_Available", result.Release?.Version.ToString() ?? ""],
            UpdateCheckStatus.NoReleases => loc["Update_Status_NoReleases"],
            UpdateCheckStatus.RateLimited => result.RateLimitResetAt is { } reset
                ? loc["Update_Status_RateLimited", reset.ToLocalTime().ToString("t", loc.Culture())]
                : loc["Update_Status_RateLimitedNoTime"],
            UpdateCheckStatus.InvalidRepository => loc["Update_Status_InvalidRepository"],
            _ => loc["Update_Status_Failed", result.ErrorDetail ?? ""]
        };
    }

    public static string FormatBytes(long bytes, CultureInfo culture)
    {
        if (bytes < 1024) return bytes.ToString(culture) + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", culture) + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("0.0", culture) + " MB";
    }
}
