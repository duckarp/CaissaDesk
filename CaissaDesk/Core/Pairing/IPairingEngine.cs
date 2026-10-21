using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public class PairingResult
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public List<Pairing> Pairings { get; set; } = [];

    /// <summary>Non-fatal notice for the arbiter, e.g. that a fallback engine produced these pairings.</summary>
    public string? Warning { get; set; }

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

    /// <summary>Synchronous pairing. Blocks the caller; UI code must use <see cref="GenerateRoundPairingsAsync"/>.</summary>
    PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber);

    /// <summary>
    /// Pairs the round without blocking the caller: CPU-bound engines run on the thread pool and external
    /// engines await their process. Throws <see cref="OperationCanceledException"/> when cancelled.
    /// The engine only reads <paramref name="tournament"/>; pass a <see cref="PairingSnapshot"/> copy when
    /// the live model may be rendered or changed while pairing is in progress.
    /// </summary>
    Task<PairingResult> GenerateRoundPairingsAsync(Tournament tournament, int roundNumber, CancellationToken cancellationToken = default);
}
