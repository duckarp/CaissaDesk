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

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
