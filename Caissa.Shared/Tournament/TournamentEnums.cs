namespace Caissa.Shared.Models;

public enum TournamentSystem
{
    SwissDutch = 0,
    RoundRobin = 1
}

public enum TimeControlCategory
{
    Standard = 0,
    Rapid = 1,
    Blitz = 2
}

public enum SeedingPrecedence
{
    FideFirst = 0,
    NationalFirst = 1
}

public enum PlayerStatus
{
    Active = 0,
    Withdrawn = 1,
    Paused = 2
}

public enum ByeType
{
    None = 0,
    HalfPoint = 1,
    ZeroPoint = 2,
    FullPoint = 3,
    PairingBye = 4
}

public enum MatchResult
{
    NotPlayed = 0,
    WhiteWin = 1,       // 1 - 0
    BlackWin = 2,       // 0 - 1
    Draw = 3,           // ½ - ½
    WhiteForfeit = 4,   // 1F - 0
    BlackForfeit = 5,   // 0 - 1F
    DoubleForfeit = 6,  // 0F - 0F
    ByeFull = 7,        // 1 point pairing-allocated bye (TRF 'U')
    ByeHalf = 8,        // ½ point bye (requested, TRF 'H')
    ByeZero = 9,        // 0 point bye / absence (TRF 'Z')
    ByeFullRequested = 10 // 1 point requested bye, not allocated by the pairing (TRF 'F')
}

public static class MatchResultExtensions
{
    public static string ToDisplayString(this MatchResult result) => result switch
    {
        MatchResult.NotPlayed => "vs",
        MatchResult.WhiteWin => "1 - 0",
        MatchResult.BlackWin => "0 - 1",
        MatchResult.Draw => "½ - ½",
        MatchResult.WhiteForfeit => "1F - 0",
        MatchResult.BlackForfeit => "0 - 1F",
        MatchResult.DoubleForfeit => "0F - 0F",
        MatchResult.ByeFull => "1 - 0 (Bye)",
        MatchResult.ByeFullRequested => "1 (Bye)",
        MatchResult.ByeHalf => "½ (Bye)",
        MatchResult.ByeZero => "0 (Bye)",
        _ => "-"
    };

    public static (double WhitePoints, double BlackPoints) ToPoints(this MatchResult result) => result switch
    {
        MatchResult.WhiteWin or MatchResult.WhiteForfeit or MatchResult.ByeFull or MatchResult.ByeFullRequested => (1.0, 0.0),
        MatchResult.BlackWin or MatchResult.BlackForfeit => (0.0, 1.0),
        MatchResult.Draw => (0.5, 0.5),
        MatchResult.ByeHalf => (0.5, 0.0),
        MatchResult.ByeZero or MatchResult.DoubleForfeit or MatchResult.NotPlayed => (0.0, 0.0),
        _ => (0.0, 0.0)
    };

    /// <summary>True for games actually played over the board (forfeits and byes are unplayed).</summary>
    public static bool IsPlayedOverBoard(this MatchResult result) =>
        result is MatchResult.WhiteWin or MatchResult.BlackWin or MatchResult.Draw;

    public static bool IsForfeit(this MatchResult result) =>
        result is MatchResult.WhiteForfeit or MatchResult.BlackForfeit or MatchResult.DoubleForfeit;

    public static bool IsByeResult(this MatchResult result) =>
        result is MatchResult.ByeFull or MatchResult.ByeFullRequested or MatchResult.ByeHalf or MatchResult.ByeZero;

    /// <summary>Points awarded to the single player of a bye board.</summary>
    public static double ByePoints(this MatchResult result) => result switch
    {
        MatchResult.ByeFull or MatchResult.ByeFullRequested => 1.0,
        MatchResult.ByeHalf => 0.5,
        _ => 0.0
    };

    /// <summary>The same result seen from the other side of the board (used when colours are swapped).</summary>
    public static MatchResult WithColorsSwapped(this MatchResult result) => result switch
    {
        MatchResult.WhiteWin => MatchResult.BlackWin,
        MatchResult.BlackWin => MatchResult.WhiteWin,
        MatchResult.WhiteForfeit => MatchResult.BlackForfeit,
        MatchResult.BlackForfeit => MatchResult.WhiteForfeit,
        _ => result
    };

    /// <summary>Bye result recorded for a player's advance bye request.</summary>
    public static MatchResult ToMatchResult(this ByeType byeType) => byeType switch
    {
        ByeType.HalfPoint => MatchResult.ByeHalf,
        ByeType.FullPoint => MatchResult.ByeFullRequested,
        ByeType.PairingBye => MatchResult.ByeFull,
        _ => MatchResult.ByeZero
    };
}
