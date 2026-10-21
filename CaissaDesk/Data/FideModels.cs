namespace CaissaDesk.Data;

public class FidePlayerCard
{
    public string FideId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string TitleAbbreviation => Title switch
    {
        "Grandmaster" => "GM",
        "International Master" => "IM",
        "FIDE Master" => "FM",
        "Candidate Master" => "CM",
        "Woman Grandmaster" => "WGM",
        "Woman International Master" => "WIM",
        "Woman FIDE Master" => "WFM",
        "Woman Candidate Master" => "WCM",
        "Arena Grandmaster" => "AGM",
        "Arena International Master" => "AIM",
        "Arena FIDE Master" => "AFM",
        "Arena Candidate Master" => "ACM",
        _ => Title ?? string.Empty
    };
    public string? Federation { get; set; }
    public string? FederationCode { get; set; }
    public string? FlagUrl { get; set; }
    public int? BirthYear { get; set; }
    public string? Gender { get; set; }
    public int? StandardElo { get; set; }
    public int? RapidElo { get; set; }
    public int? BlitzElo { get; set; }
    public int? WorldRankActive { get; set; }
    public int? WorldRankAll { get; set; }
    public int? NationalRank { get; set; }
    public string ProfileUrl => $"https://ratings.fide.com/profile/{FideId}";
}

public class FideSearchResult
{
    public string FideId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Federation { get; set; } = string.Empty;
    public int? StandardElo { get; set; }
    public int? RapidElo { get; set; }
    public int? BlitzElo { get; set; }
    public int? BirthYear { get; set; }
}

public class ChessResultsTournamentEntry
{
    public string TournamentName { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string ScoreOrRank { get; set; } = string.Empty;
    public string Rounds { get; set; } = string.Empty;
    public string TotalPlayers { get; set; } = string.Empty;
    public string Club { get; set; } = string.Empty;
    public string Federation { get; set; } = string.Empty;
    public string TournamentUrl { get; set; } = string.Empty;
    public string PlayerCardUrl { get; set; } = string.Empty;
}
