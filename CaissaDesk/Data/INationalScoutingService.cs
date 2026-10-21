namespace CaissaDesk.Data;

public class NationalPlayerCard
{
    public string? NationalId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Club { get; set; }
    public int? NationalRating { get; set; }
    public string? FideId { get; set; }
    public string Federation { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
}

public interface INationalScoutingService
{
    Task<NationalPlayerCard?> LookupPlayerAsync(
        string? nationalId,
        string? fideId,
        string? lastName,
        string? firstName,
        string? federation,
        CancellationToken cancellationToken = default);
}
