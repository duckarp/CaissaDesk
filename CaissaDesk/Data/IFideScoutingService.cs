namespace CaissaDesk.Data;

public interface IFideScoutingService
{
    Task<FidePlayerCard?> GetPlayerCardAsync(string fideId, CancellationToken cancellationToken = default);
    Task<List<FideSearchResult>> SearchPlayersByNameAsync(string query, int limit = 15, CancellationToken cancellationToken = default);
}
