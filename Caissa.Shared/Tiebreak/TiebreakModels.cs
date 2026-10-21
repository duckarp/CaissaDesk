using Caissa.Shared.Models;

namespace Caissa.Shared.Tiebreak;

public enum TiebreakType
{
    DirectEncounter = 0,
    BuchholzCut1 = 1,
    Buchholz = 2,
    BuchholzCut2 = 3,
    MedianBuchholz = 4,
    SonnebornBerger = 5,
    MostWins = 6,
    MostBlack = 7,
    Aroc1 = 8,
    Koya = 9,
    ExtendedKoya = 10,
    Aroc = 11,
    /// <summary>C.07 7.2 (WON): games won over the board (forfeit wins and byes excluded).</summary>
    GamesWon = 12
}

public enum TiebreakPreset
{
    FideStandardSwiss = 0,
    RoundRobin = 1,
    FideChampionship = 2,
    Custom = 3
}

public static class TiebreakExtensions
{
    public static string ToShortName(this TiebreakType type) => type switch
    {
        TiebreakType.DirectEncounter => "DE",
        TiebreakType.BuchholzCut1 => "BH-1",
        TiebreakType.Buchholz => "BH",
        TiebreakType.BuchholzCut2 => "BH-2",
        TiebreakType.MedianBuchholz => "Med-BH",
        TiebreakType.SonnebornBerger => "SB",
        TiebreakType.MostWins => "Wins",
        TiebreakType.MostBlack => "Black",
        TiebreakType.Aroc1 => "AROC-1",
        TiebreakType.Koya => "Koya",
        TiebreakType.ExtendedKoya => "Ext-Koya",
        TiebreakType.Aroc => "AROC",
        TiebreakType.GamesWon => "WON",
        _ => type.ToString()
    };

    public static string ToFullName(this TiebreakType type) => type switch
    {
        TiebreakType.DirectEncounter => "Direct Encounter",
        TiebreakType.BuchholzCut1 => "Buchholz (Cut 1)",
        TiebreakType.Buchholz => "Buchholz (Full)",
        TiebreakType.BuchholzCut2 => "Buchholz (Cut 2)",
        TiebreakType.MedianBuchholz => "Median Buchholz",
        TiebreakType.SonnebornBerger => "Sonneborn-Berger",
        TiebreakType.MostWins => "Greater Number of Wins",
        TiebreakType.MostBlack => "Games with Black",
        TiebreakType.Aroc1 => "Average Rating of Opponents (Cut 1)",
        TiebreakType.Koya => "Koya System",
        TiebreakType.ExtendedKoya => "Extended Koya System",
        TiebreakType.Aroc => "Average Rating of Opponents (Full)",
        TiebreakType.GamesWon => "Games Won over the Board",
        _ => type.ToString()
    };

    public static List<TiebreakType> GetDefaultList(this TiebreakPreset preset) => preset switch
    {
        TiebreakPreset.FideStandardSwiss =>
        [
            TiebreakType.DirectEncounter,
            TiebreakType.BuchholzCut1,
            TiebreakType.Buchholz,
            TiebreakType.SonnebornBerger,
            TiebreakType.MostWins
        ],
        TiebreakPreset.RoundRobin =>
        [
            TiebreakType.DirectEncounter,
            TiebreakType.Koya,
            TiebreakType.SonnebornBerger,
            TiebreakType.MostWins
        ],
        TiebreakPreset.FideChampionship =>
        [
            TiebreakType.DirectEncounter,
            TiebreakType.MostWins,
            TiebreakType.SonnebornBerger,
            TiebreakType.Koya
        ],
        _ =>
        [
            TiebreakType.DirectEncounter,
            TiebreakType.BuchholzCut1,
            TiebreakType.Buchholz,
            TiebreakType.SonnebornBerger
        ]
    };
}

public class PlayerStanding
{
    public int Rank { get; set; } = 1;
    public string PlayerId { get; set; } = string.Empty;
    public Player Player { get; set; } = default!;
    public double Points { get; set; }
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int BlackGames { get; set; }
    public int GamesWon { get; set; }
    public Dictionary<TiebreakType, double> TiebreakValues { get; set; } = [];

    public string FormatTiebreak(TiebreakType type)
    {
        if (!TiebreakValues.TryGetValue(type, out double val)) return "-";

        return type switch
        {
            TiebreakType.MostWins or TiebreakType.MostBlack or TiebreakType.GamesWon => ((int)val).ToString(),
            TiebreakType.Aroc or TiebreakType.Aroc1 => val.ToString("F0"),
            TiebreakType.DirectEncounter or TiebreakType.Koya or TiebreakType.ExtendedKoya => val.ToString("0.0"),
            _ => val.ToString("F2")
        };
    }
}
