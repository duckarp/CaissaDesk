using Caissa.Shared.Models;
using Caissa.Shared.Tiebreak;

namespace Caissa.Shared.Sync;

public class TournamentSyncPayload
{
    public Tournament Tournament { get; set; } = default!;
    public string? SyncKey { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public string? SourceClient { get; set; } = "CaissaDesk";
    public bool IsDeltaUpdate { get; set; }
    public int? DeltaRoundNumber { get; set; }
}

public class TournamentSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Organizer { get; set; }
    public string? ChiefArbiter { get; set; }
    public string? Venue { get; set; }
    public string Federation { get; set; } = "FID";
    public TournamentSystem System { get; set; }
    public TimeControlCategory TimeControl { get; set; }
    public string TimeControlDetails { get; set; } = string.Empty;
    public int TotalRounds { get; set; }
    public int CurrentRound { get; set; }
    public int PlayerCount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsFinished { get; set; }
    public DateTime? FinishedAt { get; set; }
    public bool FideRated { get; set; }
    public int FinishedRoundsCount { get; set; }
}

public class SyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TournamentId { get; set; }
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
    public int RoundsReceived { get; set; }
    public int PlayersReceived { get; set; }
}

public class LiveRoundUpdate
{
    public string TournamentId { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UpdateType { get; set; } = "result_updated"; // "result_updated", "round_paired", "tournament_sync"
    public string? Message { get; set; }
}

public class TournamentFilterQuery
{
    public string? SearchText { get; set; }
    public string? Federation { get; set; }
    public TimeControlCategory? TimeControl { get; set; }
    public TournamentSystem? System { get; set; }
    public bool? IsFinished { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class PlayerPerformanceDto
{
    public Player Player { get; set; } = default!;
    public int FinalRank { get; set; }
    public double TotalPoints { get; set; }
    public int PerformanceRating { get; set; }
    public double RatingChange { get; set; }
    public List<PlayerRoundHistoryItem> Rounds { get; set; } = [];
}

public class PlayerRoundHistoryItem
{
    public int RoundNumber { get; set; }
    public int BoardNumber { get; set; }
    public bool IsWhite { get; set; }
    public bool IsBye { get; set; }
    public string? OpponentId { get; set; }
    public string? OpponentName { get; set; }
    public string? OpponentTitle { get; set; }
    public int OpponentRating { get; set; }
    public string? OpponentFederation { get; set; }
    public MatchResult Result { get; set; }
    public string ResultDisplay { get; set; } = string.Empty;
    public double PointsEarned { get; set; }
    public double ProgressiveScore { get; set; }
}
