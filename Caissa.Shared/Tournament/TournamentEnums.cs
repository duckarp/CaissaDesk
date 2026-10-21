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
    ByeFull = 7,        // 1 point bye (pairing bye or requested)
    ByeHalf = 8,        // ½ point bye
    ByeZero = 9         // 0 point bye
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
        MatchResult.ByeHalf => "½ (Bye)",
        MatchResult.ByeZero => "0 (Bye)",
        _ => "-"
    };

    public static (double WhitePoints, double BlackPoints) ToPoints(this MatchResult result) => result switch
    {
        MatchResult.WhiteWin or MatchResult.WhiteForfeit or MatchResult.ByeFull => (1.0, 0.0),
        MatchResult.BlackWin or MatchResult.BlackForfeit => (0.0, 1.0),
        MatchResult.Draw => (0.5, 0.5),
        MatchResult.ByeHalf => (0.5, 0.0),
        MatchResult.ByeZero or MatchResult.DoubleForfeit or MatchResult.NotPlayed => (0.0, 0.0),
        _ => (0.0, 0.0)
    };
}
