using Caissa.Shared.Models;
using Caissa.Shared.Sync;

namespace CaissaDesk.Data;

public interface ICaissaResultsSyncService
{
    bool IsSyncConfigured { get; }
    Task<SyncResult> PublishTournamentAsync(Tournament tournament);
    Task<SyncResult> SyncRoundDeltaAsync(string tournamentId, int roundNumber, List<Pairing> pairings, string? tournamentSyncKey = null);
    Task<bool> DeleteTournamentAsync(string tournamentId, string? tournamentSyncKey = null);
    Task<bool> PingServerAsync(string? serverUrl = null);
    string GetPortalUrl(string tournamentId);
}
