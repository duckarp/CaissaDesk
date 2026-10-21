namespace CaissaDesk.Data;

public interface IChessResultsScoutingService
{
    Task<List<ChessResultsTournamentEntry>> SearchPlayerTournamentsAsync(string? fideId, string? lastName, int limit = 25, CancellationToken cancellationToken = default);
    Task<string?> DownloadPlayerPgnsAsync(string? fideId, string? lastName, CancellationToken cancellationToken = default);
}
