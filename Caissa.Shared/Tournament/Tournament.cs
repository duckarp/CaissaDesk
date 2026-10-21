namespace Caissa.Shared.Models;

public class Tournament
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SyncKey { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Tournament";
    public string? Organizer { get; set; }
    public string? ChiefArbiter { get; set; }
    public string? DeputyArbiter { get; set; }
    public string? Venue { get; set; }
    public string Federation { get; set; } = "SVK";

    public TournamentSystem System { get; set; } = TournamentSystem.SwissDutch;
    public TimeControlCategory TimeControl { get; set; } = TimeControlCategory.Rapid;
    public string TimeControlDetails { get; set; } = "15m + 5s";
    public SeedingPrecedence SeedingPrecedence { get; set; } = SeedingPrecedence.FideFirst;
    public int TotalRounds { get; set; } = 7;
    public bool FideRated { get; set; }

    /// <summary>
    /// Colour of the top seed on board 1 in round 1 (the "initial colour", TRF XXC). Defaults to White,
    /// which is also what tournaments saved before this property existed deserialize to.
    /// </summary>
    public FirstRoundColor TopSeedFirstRoundColor { get; set; } = FirstRoundColor.White;
    public string PairingEngineId { get; set; } = "native_dutch";
    public List<Caissa.Shared.Tiebreak.TiebreakType> TiebreakOrder { get; set; } =
        Caissa.Shared.Tiebreak.TiebreakExtensions.GetDefaultList(Caissa.Shared.Tiebreak.TiebreakPreset.FideStandardSwiss);

    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsFinished { get; set; }
    public DateTime? FinishedAt { get; set; }

    public List<Player> Players { get; set; } = [];
    public List<Round> Rounds { get; set; } = [];

    public int CurrentRoundNumber => Rounds.Count > 0 ? Rounds.Max(r => r.RoundNumber) : 0;

    public int FinishedRoundsCount =>
        Rounds.Count(r => r.IsFinished || (r.Pairings.Count > 0 && r.Pairings.All(p => p.Result != MatchResult.NotPlayed)));

    public int MinAllowedRounds
    {
        get
        {
            int min = 1;
            foreach (var r in Rounds)
            {
                bool isFinished = r.IsFinished || (r.Pairings.Count > 0 && r.Pairings.All(p => p.Result != MatchResult.NotPlayed));
                bool hasResults = r.Pairings.Any(p => p.Result != MatchResult.NotPlayed);
                if ((isFinished || hasResults) && r.RoundNumber > min)
                {
                    min = r.RoundNumber;
                }
            }
            return min;
        }
    }

    public bool TrySetTotalRounds(int newTotalRounds, out string? errorMessage)
    {
        if (newTotalRounds < MinAllowedRounds)
        {
            errorMessage = $"Cannot set total rounds to {newTotalRounds}. At least {MinAllowedRounds} round(s) are already finished or have recorded game results.";
            return false;
        }

        TotalRounds = newTotalRounds;
        // Clean up any unplayed rounds beyond the new TotalRounds
        Rounds.RemoveAll(r => r.RoundNumber > TotalRounds && r.Pairings.All(p => p.Result == MatchResult.NotPlayed));
        errorMessage = null;
        return true;
    }

    public Player? GetPlayer(string? id) =>
        string.IsNullOrEmpty(id) ? null : Players.FirstOrDefault(p => p.Id == id);
}

public class TournamentSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TournamentSystem System { get; set; }
    public TimeControlCategory TimeControl { get; set; }
    public int TotalRounds { get; set; }
    public int CurrentRound { get; set; }
    public int PlayerCount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ChiefArbiter { get; set; }
    public string? Venue { get; set; }
    public bool FideRated { get; set; }
    public bool IsFinished { get; set; }
    public DateTime? FinishedAt { get; set; }
}
