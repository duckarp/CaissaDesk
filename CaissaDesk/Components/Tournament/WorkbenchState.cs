using CaissaDesk.Components.Common;
using CaissaDesk.Core;
using CaissaDesk.Core.PairingEngine;
using CaissaDesk.Core.Standings;
using TournamentModel = Caissa.Shared.Models.Tournament;

namespace CaissaDesk.Components.Tournament;

/// <summary>
/// State shared by the tournament workbench and its sub-views (roster, pairings, standings, settings).
/// It outlives the individual views, so switching views keeps the selected round, arbiter mode, running
/// pairing or FIDE sync, and the settings draft exactly as before the workbench was split up.
/// </summary>
public sealed class WorkbenchState : IDisposable
{
    private readonly Func<TournamentModel, Task<bool>> _save;
    private TournamentDetailsDraft? _detailsDraft;
    private TournamentModel? _detailsDraftSource;

    public WorkbenchState(TournamentTab tab, ITiebreakService tiebreakService, Func<TournamentModel, Task<bool>> save)
    {
        Tab = tab;
        _save = save;
        Standings = new StandingsCache(tiebreakService);
        Pairing = new PairingSession(() => Tab.Tournament);
    }

    public TournamentTab Tab { get; }

    /// <summary>Always read through the tab: the dashboard edit modal and WorkspaceState.OpenTournament replace
    /// Tab.Tournament with a fresh instance, and a cached copy would overwrite those edits on next save.</summary>
    public TournamentModel? Tournament => Tab.Tournament;

    /// <summary>Raised when a view changed shared state outside of its own render cycle (e.g. after an await).</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>
    /// Persists the tournament and notifies the tab. Returns false when saving failed; the failure has already
    /// been logged and shown to the user.
    /// </summary>
    public Task<bool> SaveAsync(TournamentModel tournament) => _save(tournament);

    public ConfirmDialogState Confirm { get; } = new();

    // ---------------------------------------------------------------- pairings

    public PairingSession Pairing { get; }
    public int ActiveRoundNumber { get; set; } = 1;
    public bool ArbiterMode { get; set; }
    public string? PairingErrorMessage { get; set; }
    public string? PairingWarningMessage { get; set; }

    // ---------------------------------------------------------------- standings

    public StandingsCache Standings { get; }
    public bool ShowCrossTable { get; set; }

    // ---------------------------------------------------------------- roster

    public bool IsSyncingAllFide { get; set; }
    public int SyncProgressCurrent { get; set; }
    public int SyncProgressTotal { get; set; }
    public string? SyncResultMessage { get; set; }

    // ---------------------------------------------------------------- settings

    public string? SettingsErrorMessage { get; set; }
    public string? SettingsSuccessMessage { get; set; }
    public bool IsDownloadingEngine { get; set; }
    public string? DownloadingEngineId { get; set; }
    public string? EngineDownloadMessage { get; set; }

    /// <summary>
    /// The settings form edits a draft; the live tournament only changes on a validated save.
    /// The draft is rebuilt whenever the tab's tournament instance is replaced.
    /// </summary>
    public TournamentDetailsDraft Details
    {
        get
        {
            if (_detailsDraft == null || !ReferenceEquals(_detailsDraftSource, Tournament))
            {
                _detailsDraft = TournamentDetailsDraft.From(Tournament!);
                _detailsDraftSource = Tournament;
            }
            return _detailsDraft;
        }
    }

    public void DiscardDetailsDraft()
    {
        _detailsDraft = null;
        _detailsDraftSource = null;
    }

    public void Dispose()
    {
        // Abandon any pairing still running for this workbench (kills an external engine process)
        Pairing.Dispose();
    }
}
