namespace CaissaDesk.Core.Settings;

using Caissa.Shared.Models;

public class AppSettings
{
    // General Preferences
    public string Language { get; set; } = "en";

    // Tournament Defaults
    public string DefaultChiefArbiter { get; set; } = "";
    public string DefaultDeputyArbiter { get; set; } = string.Empty;
    public string DefaultOrganizer { get; set; } = string.Empty;
    public string DefaultVenue { get; set; } = "Chess Club";
    public string DefaultFederation { get; set; } = "SVK";
    public int DefaultTotalRounds { get; set; } = 7;
    public TournamentSystem DefaultSystem { get; set; } = TournamentSystem.SwissDutch;
    public TimeControlCategory DefaultTimeControl { get; set; } = TimeControlCategory.Rapid;
    public string DefaultTimeControlDetails { get; set; } = "15m + 5s";
    public bool DefaultFideRated { get; set; } = true;
    public SeedingPrecedence DefaultSeedingPrecedence { get; set; } = SeedingPrecedence.FideFirst;

    // Pairing Engine Configuration
    public string DefaultSwissEngineId { get; set; } = "native_dutch";
    public string? CustomBbpPath { get; set; }
    public string? CustomJavaPath { get; set; }
    public string? CustomJavafoJarPath { get; set; }

    // CaissaResults Web Sync Configuration
    public string CaissaResultsUrl { get; set; } = "http://localhost:5200";
    public string? CaissaResultsApiKey { get; set; }
    public bool AutoSyncOnResultChange { get; set; } = false;

    /// <summary>
    /// When true, signed sync requests may use unencrypted http:// for private/link-local LAN
    /// addresses (RFC 1918, 169.254/16, fc00::/7, fe80::/10, single-label and .local hostnames).
    /// Public hosts always require https://. Off by default.
    /// </summary>
    public bool AllowHttpOnPrivateNetworks { get; set; } = false;

    // Application updates (GitHub Releases)
    /// <summary>GitHub "owner/repo" whose releases are checked for updates.</summary>
    public string UpdateRepository { get; set; } = "duckarp/CaissaDesk";
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public bool IncludePrereleaseUpdates { get; set; } = false;
    /// <summary>When the release API last answered; automatic checks are skipped for a while after it.</summary>
    public DateTimeOffset? UpdateLastCheckedAt { get; set; }
    /// <summary>Newest version seen by the last check, so a pending update is shown again after a restart.</summary>
    public string? UpdateLatestKnownVersion { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
