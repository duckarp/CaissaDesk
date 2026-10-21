using CaissaDesk.Core.PairingEngine;
using CaissaDesk.Localization;

namespace CaissaDesk.Data;

/// <summary>Save-dialog exports of a tournament (FIDE TRF and .cdesk), shared by the workbench, print sheets and dashboard.</summary>
public static class TournamentFileExports
{
    /// <summary>The tournament name with invalid file name characters and spaces replaced by underscores.</summary>
    public static string SafeFileName(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");

    public static string TrfFileName(Tournament tournament, DateTime date) =>
        $"{SafeFileName(tournament.Name)}_{date:yyyyMMdd}.trf";

    public static string CdeskFileName(string tournamentName) => $"{SafeFileName(tournamentName)}.cdesk";

    /// <summary>Asks for a location and writes the tournament's TRF; returns the saved path or null when cancelled.</summary>
    public static Task<string?> SaveTrfAsync(this IFileExportService export, Tournament tournament, ILocalizationService loc) =>
        export.SaveTextFileAsync(
            loc["Export_TrfDialogTitle"],
            TrfFileName(tournament, DateTime.Now),
            TrfHandler.GenerateTrf(tournament),
            (loc["Export_TrfFilterName"], new[] { ".trf" }));

    /// <summary>Asks for a location and writes an exported .cdesk JSON; returns the saved path or null when cancelled.</summary>
    public static Task<string?> SaveCdeskAsync(this IFileExportService export, string tournamentName, string json, ILocalizationService loc) =>
        export.SaveTextFileAsync(
            loc["Export_CdeskDialogTitle"],
            CdeskFileName(tournamentName),
            json,
            (loc["Export_CdeskFilterName"], new[] { ".cdesk" }));
}
