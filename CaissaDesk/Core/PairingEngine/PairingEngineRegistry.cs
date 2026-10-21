using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

public interface IPairingEngineRegistry
{
    IReadOnlyList<IPairingEngine> AllEngines { get; }
    IReadOnlyList<IPairingEngine> SwissEngines { get; }
    IPairingEngine GetEngine(string id);
    IPairingEngine GetDefaultEngine(TournamentSystem system);
    void ReloadEngines(string? bbpPath = null, string? javaPath = null, string? javafoJarPath = null);
    PairingResult TestEngine(string engineId);
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

    public PairingResult TestEngine(string engineId)
    {
        var engine = _engines.FirstOrDefault(e => e.Id.Equals(engineId, StringComparison.OrdinalIgnoreCase));
        if (engine == null)
        {
            return PairingResult.Fail($"Engine '{engineId}' not found in registry.");
        }

        if (!engine.IsAvailable)
        {
            return PairingResult.Fail($"Engine '{engine.DisplayName}' is not available (executable or dependencies missing).");
        }

        var testTournament = new Tournament
        {
            Name = "Self-Test Event",
            System = engineId == "berger" ? TournamentSystem.RoundRobin : TournamentSystem.SwissDutch,
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = engine.GenerateRoundPairings(testTournament, 1);
            sw.Stop();

            if (result.Success && result.Pairings.Count > 0)
            {
                return PairingResult.Ok(result.Pairings);
            }

            return result.Success
                ? PairingResult.Fail("Engine returned 0 pairings.")
                : result;
        }
        catch (Exception ex)
        {
            return PairingResult.Fail($"Execution failed: {ex.Message}");
        }
    }

    public IPairingEngine GetEngine(string id)
    {
        var target = _engines.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (target != null && target.IsAvailable) return target;

        // Fallback to native
        return _engines.First(e => e.Id == "native_dutch");
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
