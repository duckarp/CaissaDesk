using Caissa.Shared.Models;

namespace CaissaDesk.Core;

public enum TournamentDetailsValidation
{
    Valid,
    NameRequired,
    TooFewRounds
}

/// <summary>
/// Editable copy of a tournament's general details. The settings form binds to this instead of the
/// live model, so nothing reaches the tournament (or gets persisted by an unrelated save) until the
/// user explicitly saves and the values pass validation.
/// </summary>
public sealed class TournamentDetailsDraft
{
    public string Name { get; set; } = "";
    public int TotalRounds { get; set; }
    public string? Venue { get; set; }
    public string? ChiefArbiter { get; set; }
    public string? DeputyArbiter { get; set; }
    public string? Organizer { get; set; }
    public string Federation { get; set; } = "";
    public TimeControlCategory TimeControl { get; set; }
    public string TimeControlDetails { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public SeedingPrecedence SeedingPrecedence { get; set; }
    public bool FideRated { get; set; }

    public static TournamentDetailsDraft From(Tournament t) => new()
    {
        Name = t.Name,
        TotalRounds = t.TotalRounds,
        Venue = t.Venue,
        ChiefArbiter = t.ChiefArbiter,
        DeputyArbiter = t.DeputyArbiter,
        Organizer = t.Organizer,
        Federation = t.Federation,
        TimeControl = t.TimeControl,
        TimeControlDetails = t.TimeControlDetails,
        StartDate = t.StartDate,
        EndDate = t.EndDate,
        SeedingPrecedence = t.SeedingPrecedence,
        FideRated = t.FideRated
    };

    public TournamentDetailsValidation Validate(Tournament target)
    {
        if (string.IsNullOrWhiteSpace(Name)) return TournamentDetailsValidation.NameRequired;
        if (TotalRounds < target.MinAllowedRounds) return TournamentDetailsValidation.TooFewRounds;
        return TournamentDetailsValidation.Valid;
    }

    /// <summary>Validates and, only if valid, copies the draft onto <paramref name="target"/>.</summary>
    public TournamentDetailsValidation TryApplyTo(Tournament target)
    {
        var validation = Validate(target);
        if (validation != TournamentDetailsValidation.Valid) return validation;

        if (!target.TrySetTotalRounds(TotalRounds, out _)) return TournamentDetailsValidation.TooFewRounds;

        target.Name = Name.Trim();
        target.Venue = Venue;
        target.ChiefArbiter = ChiefArbiter;
        target.DeputyArbiter = DeputyArbiter;
        target.Organizer = Organizer;
        target.Federation = string.IsNullOrWhiteSpace(Federation) ? "SVK" : Federation.Trim().ToUpperInvariant();
        target.TimeControl = TimeControl;
        target.TimeControlDetails = TimeControlDetails;
        target.StartDate = StartDate;
        target.EndDate = EndDate;
        target.SeedingPrecedence = SeedingPrecedence;
        target.FideRated = FideRated;
        return TournamentDetailsValidation.Valid;
    }
}
