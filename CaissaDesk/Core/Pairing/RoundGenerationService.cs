using Caissa.Shared.Models;

namespace CaissaDesk.Core.PairingEngine;

/// <summary>
/// Per-workbench pairing state: whether a pairing run is in progress, the cancellation that abandons it when
/// the workbench closes, and which tournament instance is still the live one (the tab may swap it while an
/// engine runs).
/// </summary>
public sealed class PairingSession : IDisposable
{
    private readonly Func<Tournament?> _currentTournament;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public PairingSession(Func<Tournament?> currentTournament)
    {
        _currentTournament = currentTournament;
    }

    /// <summary>True while a round is being generated, reset or deleted.</summary>
    public bool IsBusy { get; internal set; }

    public CancellationToken Token => _cts.Token;

    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    /// <summary>True when <paramref name="tournament"/> is still the instance the workbench shows.</summary>
    public bool IsCurrent(Tournament tournament) => ReferenceEquals(_currentTournament(), tournament);

    /// <summary>Abandons any pairing still running (kills an external engine process).</summary>
    public void Cancel()
    {
        if (!_disposed) _cts.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cts.Cancel();
        _cts.Dispose();
        _disposed = true;
    }
}

public enum RoundGenerationStatus
{
    /// <summary>A precondition failed (busy, too few players, ...): nothing was touched, messages stay as they were.</summary>
    Ignored,

    /// <summary>The tournament is unchanged; <see cref="RoundGenerationOutcome.ErrorMessage"/> may explain why.</summary>
    NoChange,

    /// <summary>The tournament was changed and persisted.</summary>
    Applied,

    /// <summary>The next round cannot be paired because the latest round still has games without a result.</summary>
    BlockedByUnfinishedGames
}

/// <summary>Why round generation failed; the UI turns it into a localized message.</summary>
public enum RoundGenerationError
{
    None,

    /// <summary>The Berger tables produced no rounds.</summary>
    RoundRobinFailed,

    /// <summary>The engine failed without explaining why.</summary>
    PairingFailed,

    /// <summary>The engine failed to re-pair <see cref="RoundGenerationOutcome.ErrorRoundNumber"/> without explaining why.</summary>
    RegenerateFailed,

    /// <summary>The rounds changed while <see cref="RoundGenerationOutcome.ErrorRoundNumber"/> was being paired.</summary>
    RoundsChangedDuringPairing
}

public sealed class RoundGenerationOutcome
{
    public RoundGenerationStatus Status { get; init; }

    /// <summary>Error detail reported by the pairing engine (or its exception), shown verbatim when present.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Failure reason for which the UI shows its own (localized) text, used when <see cref="ErrorMessage"/> is null.</summary>
    public RoundGenerationError Error { get; init; }

    /// <summary>Round number the <see cref="Error"/> refers to.</summary>
    public int ErrorRoundNumber { get; init; }

    public string? Warning { get; init; }

    /// <summary>True when there is an error to show (engine detail or a known failure reason).</summary>
    public bool HasError => ErrorMessage != null || Error != RoundGenerationError.None;

    /// <summary>Round the workbench should show after an applied change.</summary>
    public int? ActiveRoundNumber { get; init; }

    public int UnfinishedRoundNumber { get; init; }
    public IReadOnlyList<Pairing> UnfinishedPairings { get; init; } = [];

    /// <summary>False only for <see cref="RoundGenerationStatus.Ignored"/>: every other outcome replaces the shown messages.</summary>
    public bool UpdatesMessages => Status != RoundGenerationStatus.Ignored;

    internal static readonly RoundGenerationOutcome Ignored = new() { Status = RoundGenerationStatus.Ignored };
    internal static RoundGenerationOutcome NoChange(string? error = null, string? warning = null) =>
        new() { Status = RoundGenerationStatus.NoChange, ErrorMessage = error, Warning = warning };

    /// <summary>Unchanged tournament; the engine detail wins over the generic <paramref name="fallback"/> reason.</summary>
    internal static RoundGenerationOutcome Failed(string? engineMessage, RoundGenerationError fallback, int roundNumber = 0, string? warning = null) =>
        new()
        {
            Status = RoundGenerationStatus.NoChange,
            ErrorMessage = engineMessage,
            Error = engineMessage == null ? fallback : RoundGenerationError.None,
            ErrorRoundNumber = roundNumber,
            Warning = warning
        };
    internal static RoundGenerationOutcome Applied(int? activeRound, string? warning = null) =>
        new() { Status = RoundGenerationStatus.Applied, ActiveRoundNumber = activeRound, Warning = warning };
}

/// <summary>
/// Orchestrates round generation for the workbench. Every operation pairs against a snapshot (never the live
/// rounds), swaps the result into the live tournament only after the engine succeeded, persists it through the
/// supplied callback and rolls the in-memory change back when persisting fails.
/// </summary>
public interface IRoundGenerationService
{
    /// <summary>Pairs round 1 (Swiss) or all Berger rounds (round robin) from scratch, replacing existing rounds on success.</summary>
    Task<RoundGenerationOutcome> GenerateInitialAsync(PairingSession session, Tournament tournament, Func<Tournament, Task<bool>> persist);

    /// <summary>Pairs the next Swiss round, refusing while the latest round has unfinished games.</summary>
    Task<RoundGenerationOutcome> GenerateNextRoundAsync(PairingSession session, Tournament tournament, Func<Tournament, Task<bool>> persist);

    /// <summary>Round robin: clears the round's results. Swiss: re-pairs the round against the other rounds.</summary>
    Task<RoundGenerationOutcome> ResetRoundAsync(PairingSession session, Tournament tournament, int roundNumber, Func<Tournament, Task<bool>> persist);

    Task<RoundGenerationOutcome> DeleteRoundAsync(PairingSession session, Tournament tournament, int roundNumber, Func<Tournament, Task<bool>> persist);
}

public class RoundGenerationService : IRoundGenerationService
{
    private readonly IPairingEngineRegistry _engineRegistry;

    public RoundGenerationService(IPairingEngineRegistry engineRegistry)
    {
        _engineRegistry = engineRegistry;
    }

    public async Task<RoundGenerationOutcome> GenerateInitialAsync(PairingSession session, Tournament tournament, Func<Tournament, Task<bool>> persist)
    {
        if (tournament.Players.Count < 2 || session.IsBusy) return RoundGenerationOutcome.Ignored;

        session.IsBusy = true;
        try
        {
            // Pair against an empty history; the live rounds are only replaced once pairing succeeded
            List<Round> newRounds;
            string? warning = null;
            if (tournament.System == TournamentSystem.RoundRobin)
            {
                try
                {
                    // Berger tables are a cheap closed-form lookup, so this stays synchronous
                    newRounds = new BergerPairingEngine().GenerateAllRounds(PairingSnapshot.WithRounds(tournament, []));
                }
                catch (Exception ex)
                {
                    return RoundGenerationOutcome.NoChange(ex.Message);
                }

                if (newRounds.Count == 0)
                {
                    return RoundGenerationOutcome.Failed(null, RoundGenerationError.RoundRobinFailed);
                }
            }
            else
            {
                var result = await PairAgainstHistoryAsync(session, tournament, [], 1);
                if (result == null || !session.IsCurrent(tournament)) return RoundGenerationOutcome.NoChange();
                warning = result.Warning;
                if (!result.Success)
                {
                    return RoundGenerationOutcome.Failed(result.ErrorMessage, RoundGenerationError.PairingFailed, 1, warning);
                }
                newRounds = [new Round { RoundNumber = 1, Pairings = result.Pairings }];
            }

            var previousRounds = tournament.Rounds;
            int previousTotalRounds = tournament.TotalRounds;

            tournament.Rounds = newRounds;
            if (tournament.System == TournamentSystem.RoundRobin)
            {
                tournament.TotalRounds = newRounds.Count;
            }

            if (!await persist(tournament))
            {
                tournament.Rounds = previousRounds;
                tournament.TotalRounds = previousTotalRounds;
                return RoundGenerationOutcome.NoChange(warning: warning);
            }

            return RoundGenerationOutcome.Applied(1, warning);
        }
        finally
        {
            session.IsBusy = false;
        }
    }

    public async Task<RoundGenerationOutcome> GenerateNextRoundAsync(PairingSession session, Tournament tournament, Func<Tournament, Task<bool>> persist)
    {
        if (session.IsBusy) return RoundGenerationOutcome.Ignored;

        int nextRoundNumber = tournament.Rounds.Count + 1;
        if (nextRoundNumber > tournament.TotalRounds) return RoundGenerationOutcome.NoChange();

        var latestRound = tournament.Rounds.OrderBy(r => r.RoundNumber).LastOrDefault();
        if (latestRound != null)
        {
            var pending = latestRound.Pairings.Where(p => p.Result == MatchResult.NotPlayed).ToList();
            if (pending.Count > 0)
            {
                return new RoundGenerationOutcome
                {
                    Status = RoundGenerationStatus.BlockedByUnfinishedGames,
                    UnfinishedRoundNumber = latestRound.RoundNumber,
                    UnfinishedPairings = pending
                };
            }
        }

        session.IsBusy = true;
        try
        {
            var result = await PairAgainstHistoryAsync(session, tournament, tournament.Rounds, nextRoundNumber);
            if (result == null || !session.IsCurrent(tournament)) return RoundGenerationOutcome.NoChange();

            if (!result.Success)
            {
                return RoundGenerationOutcome.Failed(result.ErrorMessage, RoundGenerationError.PairingFailed, nextRoundNumber, result.Warning);
            }

            // The round list must still be the one that was paired against
            if (tournament.Rounds.Count + 1 != nextRoundNumber)
            {
                return RoundGenerationOutcome.Failed(null, RoundGenerationError.RoundsChangedDuringPairing, nextRoundNumber, result.Warning);
            }

            var round = new Round { RoundNumber = nextRoundNumber, Pairings = result.Pairings };
            tournament.Rounds.Add(round);
            if (!await persist(tournament))
            {
                tournament.Rounds.Remove(round);
                return RoundGenerationOutcome.NoChange(warning: result.Warning);
            }

            return RoundGenerationOutcome.Applied(nextRoundNumber, result.Warning);
        }
        finally
        {
            session.IsBusy = false;
        }
    }

    public async Task<RoundGenerationOutcome> ResetRoundAsync(PairingSession session, Tournament tournament, int roundNumber, Func<Tournament, Task<bool>> persist)
    {
        if (session.IsBusy) return RoundGenerationOutcome.Ignored;

        session.IsBusy = true;
        try
        {
            if (tournament.System == TournamentSystem.RoundRobin)
            {
                var targetRound = tournament.Rounds.FirstOrDefault(r => r.RoundNumber == roundNumber);
                var previous = targetRound?.Pairings.Select(p => (Pairing: p, p.Result, p.IsManualOverride)).ToList() ?? [];
                foreach (var (p, _, _) in previous)
                {
                    p.Result = MatchResult.NotPlayed;
                    p.IsManualOverride = false;
                }

                if (!await persist(tournament))
                {
                    foreach (var (p, result, manual) in previous)
                    {
                        p.Result = result;
                        p.IsManualOverride = manual;
                    }
                    return RoundGenerationOutcome.NoChange();
                }

                return RoundGenerationOutcome.Applied(null);
            }

            // Pair against the history without this round; keep the existing round if the engine fails
            var history = tournament.Rounds.Where(r => r.RoundNumber != roundNumber).ToList();
            var pairing = await PairAgainstHistoryAsync(session, tournament, history, roundNumber);
            if (pairing == null || !session.IsCurrent(tournament)) return RoundGenerationOutcome.NoChange();

            if (!pairing.Success)
            {
                return RoundGenerationOutcome.Failed(pairing.ErrorMessage, RoundGenerationError.RegenerateFailed, roundNumber, pairing.Warning);
            }

            var before = tournament.Rounds.ToList();
            tournament.Rounds.RemoveAll(r => r.RoundNumber == roundNumber);
            tournament.Rounds.Add(new Round { RoundNumber = roundNumber, Pairings = pairing.Pairings });

            if (!await persist(tournament))
            {
                RestoreRounds(tournament, before);
                return RoundGenerationOutcome.NoChange(warning: pairing.Warning);
            }

            return RoundGenerationOutcome.Applied(roundNumber, pairing.Warning);
        }
        finally
        {
            session.IsBusy = false;
        }
    }

    public async Task<RoundGenerationOutcome> DeleteRoundAsync(PairingSession session, Tournament tournament, int roundNumber, Func<Tournament, Task<bool>> persist)
    {
        if (session.IsBusy) return RoundGenerationOutcome.Ignored;

        session.IsBusy = true;
        try
        {
            var before = tournament.Rounds.ToList();
            tournament.Rounds.RemoveAll(r => r.RoundNumber == roundNumber);

            if (!await persist(tournament))
            {
                RestoreRounds(tournament, before);
                return RoundGenerationOutcome.NoChange();
            }

            return RoundGenerationOutcome.Applied(Math.Max(1, tournament.Rounds.Count));
        }
        finally
        {
            session.IsBusy = false;
        }
    }

    private static void RestoreRounds(Tournament tournament, List<Round> rounds)
    {
        tournament.Rounds.Clear();
        tournament.Rounds.AddRange(rounds);
    }

    /// <summary>
    /// Pairs <paramref name="roundNumber"/> against a hypothetical <paramref name="history"/> without ever
    /// mutating the live tournament: the engine works on a shallow copy whose Rounds list is the history, so
    /// rendering and other handlers keep seeing the real rounds while the (possibly slow) engine runs.
    /// Returns null when pairing was cancelled because the workbench was closed.
    /// </summary>
    private async Task<PairingResult?> PairAgainstHistoryAsync(PairingSession session, Tournament live, IEnumerable<Round> history, int roundNumber)
    {
        try
        {
            var engine = _engineRegistry.GetEngine(live.PairingEngineId);
            var snapshot = PairingSnapshot.WithRounds(live, history);
            return await engine.GenerateRoundPairingsAsync(snapshot, roundNumber, session.Token);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            return PairingResult.Fail(ex.Message);
        }
    }
}
