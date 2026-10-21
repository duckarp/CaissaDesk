namespace Caissa.Shared.Models;

public class Pairing
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int RoundNumber { get; set; } = 1;
    public int BoardNumber { get; set; } = 1;
    public string? WhitePlayerId { get; set; }
    public string? BlackPlayerId { get; set; }
    public MatchResult Result { get; set; } = MatchResult.NotPlayed;
    public bool IsManualOverride { get; set; }
    public string? Remarks { get; set; }

    public bool IsBye => string.IsNullOrEmpty(BlackPlayerId) || string.IsNullOrEmpty(WhitePlayerId);
}
