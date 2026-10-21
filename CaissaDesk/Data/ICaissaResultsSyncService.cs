using Caissa.Shared.Models;
using Caissa.Shared.Sync;

namespace CaissaDesk.Data;

public interface ICaissaResultsSyncService
{
    bool IsSyncConfigured { get; }
    Task<SyncResult> PublishTournamentAsync(Tournament tournament, CancellationToken cancellationToken = default);
    Task<SyncResult> SyncRoundDeltaAsync(string tournamentId, int roundNumber, List<Pairing> pairings, string? tournamentSyncKey = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteTournamentAsync(string tournamentId, string? tournamentSyncKey = null, CancellationToken cancellationToken = default);
    Task<bool> PingServerAsync(string? serverUrl = null, CancellationToken cancellationToken = default);
    string GetPortalUrl(string tournamentId);
}
