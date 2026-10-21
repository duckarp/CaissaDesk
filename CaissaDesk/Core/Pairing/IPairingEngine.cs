using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public class PairingResult
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public List<Pairing> Pairings { get; set; } = [];

    public static PairingResult Ok(List<Pairing> pairings) => new()
    {
        Success = true,
        Pairings = pairings
    };

    public static PairingResult Fail(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}

public interface IPairingEngine
{
    string Id { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }
    PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber);
}
