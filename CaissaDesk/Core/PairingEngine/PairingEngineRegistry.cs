using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public interface IPairingEngineRegistry
{
    IReadOnlyList<IPairingEngine> AllEngines { get; }
    IReadOnlyList<IPairingEngine> SwissEngines { get; }
    /// <summary>
    /// Returns the requested engine, or the native engine when it is unavailable. A fallback engine has
    /// a <see cref="FallbackPairingEngine"/> wrapper: its display name and every result carry a warning.
    /// </summary>
    IPairingEngine GetEngine(string id);
    EngineResolution ResolveEngine(string id);
    IPairingEngine GetDefaultEngine(TournamentSystem system);
    void ReloadEngines(string? bbpPath = null, string? javaPath = null, string? javafoJarPath = null);
    PairingResult TestEngine(string engineId);
    Task<PairingResult> TestEngineAsync(string engineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Self-tests a temporary engine instance configured with the given (possibly unsaved) paths; the
    /// registered engines are left untouched.
    /// </summary>
    Task<PairingResult> TestEngineWithPathsAsync(string engineId, string? bbpPath, string? javaPath, string? javafoJarPath, CancellationToken cancellationToken = default);
}

public class PairingEngineRegistry : IPairingEngineRegistry
{
    private readonly List<IPairingEngine> _engines = [];

    public IReadOnlyList<IPairingEngine> AllEngines => _engines.AsReadOnly();

    public IReadOnlyList<IPairingEngine> SwissEngines =>
        _engines.Where(e => e.Id != "berger").ToList().AsReadOnly();

    public PairingEngineRegistry()
    {
        // Register the 4 providers
        _engines.Add(new NativeDutchPairingEngine());
        _engines.Add(new BbpPairingEngine());
        _engines.Add(new JaVaFoPairingEngine());
        _engines.Add(new BergerPairingEngine());
    }

    public void ReloadEngines(string? bbpPath = null, string? javaPath = null, string? javafoJarPath = null)
    {
        var bbp = _engines.OfType<BbpPairingEngine>().FirstOrDefault();
        bbp?.SetCustomPath(bbpPath);

        var javafo = _engines.OfType<JaVaFoPairingEngine>().FirstOrDefault();
        javafo?.SetCustomPaths(javaPath, javafoJarPath);
    }

    public PairingResult TestEngine(string engineId) =>
        TestEngineAsync(engineId).GetAwaiter().GetResult();

    public Task<PairingResult> TestEngineAsync(string engineId, CancellationToken cancellationToken = default)
    {
        var engine = _engines.FirstOrDefault(e => e.Id.Equals(engineId, StringComparison.OrdinalIgnoreCase));
        return engine == null
            ? Task.FromResult(PairingResult.Fail($"Engine '{engineId}' not found in registry."))
            : RunSelfTestAsync(engine, cancellationToken);
    }

    public Task<PairingResult> TestEngineWithPathsAsync(
        string engineId,
        string? bbpPath,
        string? javaPath,
        string? javafoJarPath,
        CancellationToken cancellationToken = default)
    {
        // A throw-away instance: unsaved paths must never leak into the engines the tournaments use
        IPairingEngine? engine = engineId.Trim().ToLowerInvariant() switch
        {
            "native_dutch" => new NativeDutchPairingEngine(),
            "bbp" => new BbpPairingEngine(bbpPath),
            "javafo" => new JaVaFoPairingEngine(javaPath, javafoJarPath),
            "berger" => new BergerPairingEngine(),
            _ => null
        };

        return engine == null
            ? Task.FromResult(PairingResult.Fail($"Engine '{engineId}' not found in registry."))
            : RunSelfTestAsync(engine, cancellationToken);
    }

    /// <summary>Pairs round 1 of a small synthetic event to prove the engine runs end to end.</summary>
    public static async Task<PairingResult> RunSelfTestAsync(IPairingEngine engine, CancellationToken cancellationToken = default)
    {
        if (!engine.IsAvailable)
        {
            return PairingResult.Fail($"Engine '{engine.DisplayName}' is not available (executable or dependencies missing).");
        }

        var testTournament = new Tournament
        {
            Name = "Self-Test Event",
            System = engine.Id == "berger" ? TournamentSystem.RoundRobin : TournamentSystem.SwissDutch,
            TotalRounds = 3,
            Players =
            [
                new Player { Id = "test_p1", StartRank = 1, FullName = "Grandmaster Alpha", FideRating = 2600 },
                new Player { Id = "test_p2", StartRank = 2, FullName = "Grandmaster Beta", FideRating = 2550 },
                new Player { Id = "test_p3", StartRank = 3, FullName = "Master Gamma", FideRating = 2400 },
                new Player { Id = "test_p4", StartRank = 4, FullName = "Master Delta", FideRating = 2350 }
            ]
        };

        try
        {
            var result = await engine.GenerateRoundPairingsAsync(testTournament, 1, cancellationToken).ConfigureAwait(false);

            if (result.Success && result.Pairings.Count > 0)
            {
                return PairingResult.Ok(result.Pairings);
            }

            return result.Success
                ? PairingResult.Fail("Engine returned 0 pairings.")
                : result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PairingResult.Fail($"Execution failed: {ex.Message}");
        }
    }

    public IPairingEngine GetEngine(string id) => ResolveEngine(id).Engine;

    public EngineResolution ResolveEngine(string id)
    {
        var target = _engines.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (target != null && target.IsAvailable) return new EngineResolution(target, null);

        // Fall back to native, but make the substitution visible to the caller and in every result
        var native = _engines.First(e => e.Id == "native_dutch");
        string requested = target?.DisplayName ?? id;
        string warning = target == null
            ? $"Pairing engine '{id}' is unknown; pairings were generated by {native.DisplayName} instead."
            : $"{requested} is not available (executable or dependencies missing); pairings were generated by {native.DisplayName} instead.";
        return new EngineResolution(new FallbackPairingEngine(native, warning), warning);
    }

    public IPairingEngine GetDefaultEngine(TournamentSystem system)
    {
        if (system == TournamentSystem.RoundRobin)
        {
            return _engines.First(e => e.Id == "berger");
        }

        // Return first available Swiss engine (native is always available)
        return _engines.First(e => e.Id == "native_dutch");
    }
}

public sealed record EngineResolution(IPairingEngine Engine, string? FallbackWarning)
{
    public bool IsFallback => FallbackWarning != null;
}

/// <summary>Wraps the engine used in place of an unavailable one and attaches a warning to its results.</summary>
public sealed class FallbackPairingEngine(IPairingEngine inner, string warning) : IPairingEngine
{
    public IPairingEngine Inner => inner;
    public string Warning => warning;

    public string Id => inner.Id;
    public string DisplayName => $"{inner.DisplayName} (fallback)";
    public bool IsAvailable => inner.IsAvailable;

    public PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber) =>
        AttachWarning(inner.GenerateRoundPairings(tournament, roundNumber));

    public async Task<PairingResult> GenerateRoundPairingsAsync(Tournament tournament, int roundNumber, CancellationToken cancellationToken = default) =>
        AttachWarning(await inner.GenerateRoundPairingsAsync(tournament, roundNumber, cancellationToken).ConfigureAwait(false));

    private PairingResult AttachWarning(PairingResult result)
    {
        result.Warning = result.Warning == null ? warning : $"{warning} {result.Warning}";
        return result;
    }
}
