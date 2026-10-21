using Caissa.Shared.Models;

namespace CaissaDesk.Data;

public interface ITournamentRepository
{
    Task InitializeAsync();
    Task<IReadOnlyList<TournamentSummary>> GetAllSummariesAsync(bool? isFinished = null);
    Task<Tournament?> GetByIdAsync(string id);
    Task SaveAsync(Tournament tournament);
    Task SetFinishedAsync(string id, bool isFinished);
    Task DeleteAsync(string id);
    Task<string> ExportJsonAsync(string id);
    Task<Tournament> ImportJsonAsync(string jsonContent);
    Task<Tournament> CreateDemoTournamentAsync();
}
