using CaissaDesk.Core.PairingEngine;

namespace CaissaDesk.Tests;

public class RoundGenerationServiceTests
{
    private sealed class FakeEngine : IPairingEngine
    {
        public Func<Tournament, int, CancellationToken, Task<PairingResult>> Pair { get; set; } =
            (t, r, _) => Task.FromResult(PairingResult.Ok(DefaultPairings(t, r)));

        public List<(int RoundNumber, int HistoryCount, bool WasLiveInstance)> Calls { get; } = [];
        public Tournament? Live { get; set; }

        public string Id => "fake";
        public string DisplayName => "Fake";
        public bool IsAvailable => true;

        public PairingResult GenerateRoundPairings(Tournament tournament, int roundNumber) =>
            GenerateRoundPairingsAsync(tournament, roundNumber).GetAwaiter().GetResult();

        public Task<PairingResult> GenerateRoundPairingsAsync(Tournament tournament, int roundNumber, CancellationToken cancellationToken = default)
        {
            Calls.Add((roundNumber, tournament.Rounds.Count, ReferenceEquals(tournament, Live)));
            return Pair(tournament, roundNumber, cancellationToken);
        }

        public static List<Pairing> DefaultPairings(Tournament t, int roundNumber)
        {
            var ids = t.Players.Select(p => p.Id).ToList();
            var list = new List<Pairing>();
            for (int i = 0; i + 1 < ids.Count; i += 2)
            {
                list.Add(new Pairing { RoundNumber = roundNumber, BoardNumber = i / 2 + 1, WhitePlayerId = ids[i], BlackPlayerId = ids[i + 1] });
            }
            return list;
        }
    }

    private sealed class FakeRegistry(IPairingEngine engine) : IPairingEngineRegistry
    {
        public IReadOnlyList<IPairingEngine> AllEngines => [engine];
        public IReadOnlyList<IPairingEngine> SwissEngines => [engine];
        public IPairingEngine GetEngine(string id) => engine;
        public EngineResolution ResolveEngine(string id) => throw new NotSupportedException();
        public IPairingEngine GetDefaultEngine(TournamentSystem system) => engine;
        public void ReloadEngines(string? bbpPath = null, string? javaPath = null, string? javafoJarPath = null) { }
        public PairingResult TestEngine(string engineId) => throw new NotSupportedException();
        public Task<PairingResult> TestEngineAsync(string engineId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PairingResult> TestEngineWithPathsAsync(string engineId, string? bbpPath, string? javaPath, string? javafoJarPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static Tournament Swiss(int players = 4, int totalRounds = 5) => new()
    {
        System = TournamentSystem.SwissDutch,
        TotalRounds = totalRounds,
        Players = Enumerable.Range(1, players).Select(i => new Player { Id = $"p{i}", StartRank = i, FullName = $"P{i}" }).ToList()
    };

    private static (RoundGenerationService Service, FakeEngine Engine) Create()
    {
        var engine = new FakeEngine();
        return (new RoundGenerationService(new FakeRegistry(engine)), engine);
    }

    private static Func<Tournament, Task<bool>> Persist(List<Tournament> saved, bool succeed = true) => t =>
    {
        saved.Add(t);
        return Task.FromResult(succeed);
    };

    private static void FinishAll(Round round)
    {
        foreach (var p in round.Pairings) p.Result = MatchResult.Draw;
    }

    // ------------------------------------------------------------ initial generation

    [Fact]
    public async Task GenerateInitial_Swiss_PairsAgainstEmptySnapshotAndPersists()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        engine.Live = t;
        using var session = new PairingSession(() => t);
        var saved = new List<Tournament>();

        var outcome = await svc.GenerateInitialAsync(session, t, Persist(saved));

        Assert.Equal(RoundGenerationStatus.Applied, outcome.Status);
        Assert.Equal(1, outcome.ActiveRoundNumber);
        Assert.Single(t.Rounds);
        Assert.Equal(2, t.Rounds[0].Pairings.Count);
        Assert.Single(saved);
        Assert.Equal((1, 0, false), engine.Calls.Single());
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task GenerateInitial_TooFewPlayers_IsIgnored()
    {
        var (svc, _) = Create();
        var t = Swiss(players: 1);
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateInitialAsync(session, t, Persist([]));

        Assert.Equal(RoundGenerationStatus.Ignored, outcome.Status);
        Assert.False(outcome.UpdatesMessages);
    }

    [Fact]
    public async Task GenerateInitial_EngineFailure_KeepsExistingRounds()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        var existing = new Round { RoundNumber = 1, Pairings = FakeEngine.DefaultPairings(t, 1) };
        t.Rounds.Add(existing);
        engine.Pair = (_, _, _) => Task.FromResult(new PairingResult { Success = false, ErrorMessage = "boom", Warning = "fallback" });
        using var session = new PairingSession(() => t);
        var saved = new List<Tournament>();

        var outcome = await svc.GenerateInitialAsync(session, t, Persist(saved));

        Assert.Equal(RoundGenerationStatus.NoChange, outcome.Status);
        Assert.Equal("boom", outcome.ErrorMessage);
        Assert.Equal(RoundGenerationError.None, outcome.Error);
        Assert.Equal("fallback", outcome.Warning);
        Assert.Same(existing, t.Rounds.Single());
        Assert.Empty(saved);
    }

    [Fact]
    public async Task GenerateInitial_EngineThrows_ReportsMessage()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        engine.Pair = (_, _, _) => throw new InvalidOperationException("engine crashed");
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateInitialAsync(session, t, Persist([]));

        Assert.Equal(RoundGenerationStatus.NoChange, outcome.Status);
        Assert.Equal("engine crashed", outcome.ErrorMessage);
        Assert.Empty(t.Rounds);
    }

    [Fact]
    public async Task GenerateInitial_FailureWithoutMessage_UsesDefaultMessage()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        engine.Pair = (_, _, _) => Task.FromResult(new PairingResult { Success = false });
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateInitialAsync(session, t, Persist([]));

        Assert.Null(outcome.ErrorMessage);
        Assert.Equal(RoundGenerationError.PairingFailed, outcome.Error);
        Assert.Equal(1, outcome.ErrorRoundNumber);
        Assert.True(outcome.HasError);
    }

    [Fact]
    public async Task GenerateInitial_RoundRobin_CreatesAllBergerRoundsAndSetsTotalRounds()
    {
        var (svc, engine) = Create();
        var t = Swiss(players: 6, totalRounds: 9);
        t.System = TournamentSystem.RoundRobin;
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateInitialAsync(session, t, Persist([]));

        Assert.Equal(RoundGenerationStatus.Applied, outcome.Status);
        Assert.Equal(5, t.Rounds.Count);
        Assert.Equal(5, t.TotalRounds);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public async Task GenerateInitial_PersistFails_RollsBackRoundsAndTotal()
    {
        var (svc, _) = Create();
        var t = Swiss(players: 6, totalRounds: 9);
        t.System = TournamentSystem.RoundRobin;
        var originalRounds = t.Rounds;
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateInitialAsync(session, t, Persist([], succeed: false));

        Assert.Equal(RoundGenerationStatus.NoChange, outcome.Status);
        Assert.Same(originalRounds, t.Rounds);
        Assert.Empty(t.Rounds);
        Assert.Equal(9, t.TotalRounds);
    }

    [Fact]
    public async Task GenerateInitial_TournamentReplacedWhilePairing_DiscardsResult()
    {
        var (svc, _) = Create();
        var t = Swiss();
        Tournament? current = t;
        using var session = new PairingSession(() => current);
        current = Swiss();

        var outcome = await svc.GenerateInitialAsync(session, t, Persist([]));

        Assert.Equal(RoundGenerationStatus.NoChange, outcome.Status);
        Assert.Null(outcome.ErrorMessage);
        Assert.Empty(t.Rounds);
    }

    // ------------------------------------------------------------ busy state and cancellation

    [Fact]
    public async Task WhileBusy_OtherOperationsAreIgnored_AndBusyClearsAfterwards()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        var gate = new TaskCompletionSource<PairingResult>();
        engine.Pair = (_, _, _) => gate.Task;
        using var session = new PairingSession(() => t);

        var running = svc.GenerateInitialAsync(session, t, Persist([]));
        Assert.True(session.IsBusy);

        Assert.Equal(RoundGenerationStatus.Ignored, (await svc.GenerateNextRoundAsync(session, t, Persist([]))).Status);
        Assert.Equal(RoundGenerationStatus.Ignored, (await svc.ResetRoundAsync(session, t, 1, Persist([]))).Status);
        Assert.Equal(RoundGenerationStatus.Ignored, (await svc.DeleteRoundAsync(session, t, 1, Persist([]))).Status);
        Assert.Equal(RoundGenerationStatus.Ignored, (await svc.GenerateInitialAsync(session, t, Persist([]))).Status);

        gate.SetResult(PairingResult.Ok(FakeEngine.DefaultPairings(t, 1)));
        Assert.Equal(RoundGenerationStatus.Applied, (await running).Status);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task Cancel_AbandonsRunningPairingWithoutChanges()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        engine.Pair = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return PairingResult.Fail("unreachable");
        };
        var session = new PairingSession(() => t);
        var saved = new List<Tournament>();

        var running = svc.GenerateInitialAsync(session, t, Persist(saved));
        session.Cancel();
        var outcome = await running;

        Assert.Equal(RoundGenerationStatus.NoChange, outcome.Status);
        Assert.Null(outcome.ErrorMessage);
        Assert.Empty(t.Rounds);
        Assert.Empty(saved);
        session.Dispose();
        session.Cancel(); // safe after dispose
    }

    // ------------------------------------------------------------ next round

    [Fact]
    public async Task GenerateNext_PairsAgainstCurrentHistory_AndAppendsRound()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        t.Rounds.Add(new Round { RoundNumber = 1, Pairings = FakeEngine.DefaultPairings(t, 1) });
        FinishAll(t.Rounds[0]);
        engine.Pair = (s, r, _) => Task.FromResult(new PairingResult { Success = true, Pairings = FakeEngine.DefaultPairings(s, r), Warning = "native fallback" });
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateNextRoundAsync(session, t, Persist([]));

        Assert.Equal(RoundGenerationStatus.Applied, outcome.Status);
        Assert.Equal(2, outcome.ActiveRoundNumber);
        Assert.Equal("native fallback", outcome.Warning);
        Assert.Equal([1, 2], t.Rounds.Select(r => r.RoundNumber));
        Assert.Equal((2, 1, false), engine.Calls.Single());
    }

    [Fact]
    public async Task GenerateNext_WithUnfinishedGames_IsBlockedAndListsThem()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        t.Rounds.Add(new Round { RoundNumber = 1, Pairings = FakeEngine.DefaultPairings(t, 1) });
        t.Rounds[0].Pairings[0].Result = MatchResult.WhiteWin;
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateNextRoundAsync(session, t, Persist([]));

        Assert.Equal(RoundGenerationStatus.BlockedByUnfinishedGames, outcome.Status);
        Assert.Equal(1, outcome.UnfinishedRoundNumber);
        Assert.Same(t.Rounds[0].Pairings[1], outcome.UnfinishedPairings.Single());
        Assert.True(outcome.UpdatesMessages);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public async Task GenerateNext_BeyondTotalRounds_DoesNothing()
    {
        var (svc, engine) = Create();
        var t = Swiss(totalRounds: 1);
        t.Rounds.Add(new Round { RoundNumber = 1, Pairings = FakeEngine.DefaultPairings(t, 1) });
        FinishAll(t.Rounds[0]);
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateNextRoundAsync(session, t, Persist([]));

        Assert.Equal(RoundGenerationStatus.NoChange, outcome.Status);
        Assert.Single(t.Rounds);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public async Task GenerateNext_RoundsChangedWhilePairing_IsRejected()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        engine.Pair = (s, r, _) =>
        {
            // Someone added a round while the engine was running
            t.Rounds.Add(new Round { RoundNumber = 1 });
            return Task.FromResult(PairingResult.Ok(FakeEngine.DefaultPairings(s, r)));
        };
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateNextRoundAsync(session, t, Persist([]));

        Assert.Equal(RoundGenerationStatus.NoChange, outcome.Status);
        Assert.Equal(RoundGenerationError.RoundsChangedDuringPairing, outcome.Error);
        Assert.Equal(1, outcome.ErrorRoundNumber);
        Assert.Single(t.Rounds);
    }

    [Fact]
    public async Task GenerateNext_PersistFails_RemovesAddedRound()
    {
        var (svc, _) = Create();
        var t = Swiss();
        using var session = new PairingSession(() => t);

        var outcome = await svc.GenerateNextRoundAsync(session, t, Persist([], succeed: false));

        Assert.Equal(RoundGenerationStatus.NoChange, outcome.Status);
        Assert.Empty(t.Rounds);
    }

    // ------------------------------------------------------------ reset / delete

    [Fact]
    public async Task ResetRound_RoundRobin_ClearsResultsAndOverrides()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        t.System = TournamentSystem.RoundRobin;
        t.Rounds.Add(new Round { RoundNumber = 1, Pairings = FakeEngine.DefaultPairings(t, 1) });
        FinishAll(t.Rounds[0]);
        t.Rounds[0].Pairings[0].IsManualOverride = true;
        using var session = new PairingSession(() => t);

        var outcome = await svc.ResetRoundAsync(session, t, 1, Persist([]));

        Assert.Equal(RoundGenerationStatus.Applied, outcome.Status);
        Assert.Null(outcome.ActiveRoundNumber);
        Assert.All(t.Rounds[0].Pairings, p => Assert.Equal(MatchResult.NotPlayed, p.Result));
        Assert.All(t.Rounds[0].Pairings, p => Assert.False(p.IsManualOverride));
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public async Task ResetRound_RoundRobin_PersistFails_RestoresResults()
    {
        var (svc, _) = Create();
        var t = Swiss();
        t.System = TournamentSystem.RoundRobin;
        t.Rounds.Add(new Round { RoundNumber = 1, Pairings = FakeEngine.DefaultPairings(t, 1) });
        FinishAll(t.Rounds[0]);
        t.Rounds[0].Pairings[0].IsManualOverride = true;
        using var session = new PairingSession(() => t);

        await svc.ResetRoundAsync(session, t, 1, Persist([], succeed: false));

        Assert.All(t.Rounds[0].Pairings, p => Assert.Equal(MatchResult.Draw, p.Result));
        Assert.True(t.Rounds[0].Pairings[0].IsManualOverride);
    }

    [Fact]
    public async Task ResetRound_Swiss_RepairsAgainstOtherRounds()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        t.Rounds.Add(new Round { RoundNumber = 1, Pairings = FakeEngine.DefaultPairings(t, 1) });
        t.Rounds.Add(new Round { RoundNumber = 2, Pairings = FakeEngine.DefaultPairings(t, 2) });
        var oldRound2 = t.Rounds[1];
        using var session = new PairingSession(() => t);

        var outcome = await svc.ResetRoundAsync(session, t, 2, Persist([]));

        Assert.Equal(RoundGenerationStatus.Applied, outcome.Status);
        Assert.Equal(2, outcome.ActiveRoundNumber);
        Assert.Equal((2, 1, false), engine.Calls.Single());
        Assert.NotSame(oldRound2, t.Rounds[1]);
        Assert.Equal([1, 2], t.Rounds.Select(r => r.RoundNumber));
    }

    [Fact]
    public async Task ResetRound_Swiss_EngineFailure_KeepsRoundAndUsesDefaultMessage()
    {
        var (svc, engine) = Create();
        var t = Swiss();
        t.Rounds.Add(new Round { RoundNumber = 1, Pairings = FakeEngine.DefaultPairings(t, 1) });
        var round = t.Rounds[0];
        engine.Pair = (_, _, _) => Task.FromResult(new PairingResult { Success = false });
        using var session = new PairingSession(() => t);

        var outcome = await svc.ResetRoundAsync(session, t, 1, Persist([]));

        Assert.Null(outcome.ErrorMessage);
        Assert.Equal(RoundGenerationError.RegenerateFailed, outcome.Error);
        Assert.Equal(1, outcome.ErrorRoundNumber);
        Assert.Same(round, t.Rounds.Single());
    }

    [Fact]
    public async Task ResetRound_Swiss_PersistFails_RestoresOriginalRound()
    {
        var (svc, _) = Create();
        var t = Swiss();
        t.Rounds.Add(new Round { RoundNumber = 1, Pairings = FakeEngine.DefaultPairings(t, 1) });
        var round = t.Rounds[0];
        using var session = new PairingSession(() => t);

        await svc.ResetRoundAsync(session, t, 1, Persist([], succeed: false));

        Assert.Same(round, t.Rounds.Single());
    }

    [Fact]
    public async Task DeleteRound_RemovesRoundAndSelectsLast()
    {
        var (svc, _) = Create();
        var t = Swiss();
        t.Rounds.Add(new Round { RoundNumber = 1 });
        t.Rounds.Add(new Round { RoundNumber = 2 });
        using var session = new PairingSession(() => t);

        var outcome = await svc.DeleteRoundAsync(session, t, 2, Persist([]));

        Assert.Equal(RoundGenerationStatus.Applied, outcome.Status);
        Assert.Equal(1, outcome.ActiveRoundNumber);
        Assert.Single(t.Rounds);
    }

    [Fact]
    public async Task DeleteRound_PersistFails_RestoresRound()
    {
        var (svc, _) = Create();
        var t = Swiss();
        t.Rounds.Add(new Round { RoundNumber = 1 });
        t.Rounds.Add(new Round { RoundNumber = 2 });
        using var session = new PairingSession(() => t);

        var outcome = await svc.DeleteRoundAsync(session, t, 2, Persist([], succeed: false));

        Assert.Equal(RoundGenerationStatus.NoChange, outcome.Status);
        Assert.Equal([1, 2], t.Rounds.Select(r => r.RoundNumber));
    }
}
