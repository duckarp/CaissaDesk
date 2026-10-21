namespace CaissaDesk.Core.Roster;

/// <summary>
/// Editable values of the register / edit player form. The live <see cref="Player"/> is only touched by
/// <see cref="ApplyTo"/> (edit) or <see cref="ToNewPlayer"/> (register), which also normalise the input.
/// </summary>
public sealed class PlayerFormModel
{
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Federation { get; set; } = "SVK";
    public int FideRating { get; set; }
    public int NationalRating { get; set; }
    public string Club { get; set; } = "";
    public int? BirthYear { get; set; }
    public string Gender { get; set; } = "M";
    public string NationalId { get; set; } = "";
    public string FideId { get; set; } = "";
    public PlayerStatus Status { get; set; } = PlayerStatus.Active;

    /// <summary>A name is required (either part is enough).</summary>
    public bool HasName => !string.IsNullOrWhiteSpace(LastName) || !string.IsNullOrWhiteSpace(FirstName);

    /// <summary>The lookup button is enabled once there is a FIDE ID or a name to search for.</summary>
    public bool CanLookup => !string.IsNullOrWhiteSpace(FideId) || HasName;

    /// <summary>Anything at all to look up (the national database can also be queried by national ID).</summary>
    public bool HasLookupQuery => CanLookup || !string.IsNullOrWhiteSpace(NationalId);

    /// <summary>An empty registration form, defaulting the federation to the tournament's.</summary>
    public static PlayerFormModel ForNewPlayer(Tournament? tournament) => new()
    {
        Federation = tournament?.Federation ?? "SVK"
    };

    public static PlayerFormModel FromPlayer(Player player)
    {
        var model = new PlayerFormModel
        {
            LastName = player.LastName,
            FirstName = player.FirstName,
            Title = player.Title ?? "",
            Federation = player.Federation ?? "FID",
            FideRating = player.FideRating,
            NationalRating = player.NationalRating,
            Club = player.Club ?? "",
            BirthYear = player.BirthYear,
            Gender = string.IsNullOrEmpty(player.Gender) ? "M" : player.Gender,
            NationalId = player.NationalId ?? "",
            FideId = player.FideId ?? "",
            Status = player.Status
        };

        if (string.IsNullOrWhiteSpace(model.LastName) && string.IsNullOrWhiteSpace(model.FirstName) && !string.IsNullOrWhiteSpace(player.FullName))
        {
            var (last, first) = Player.SplitFullName(player.FullName);
            model.LastName = last;
            model.FirstName = first;
        }

        return model;
    }

    /// <summary>The FIDE code for the entered federation, or the trimmed upper-cased input ("FID" when empty).</summary>
    public string NormalizedFederation =>
        FideCountryHelper.ToFideCode(Federation) ?? (string.IsNullOrWhiteSpace(Federation) ? "FID" : Federation.Trim().ToUpperInvariant());

    public Player ToNewPlayer(int startRank) => new()
    {
        StartRank = startRank,
        LastName = LastName.Trim(),
        FirstName = FirstName.Trim(),
        Title = NullIfBlank(Title),
        Federation = NormalizedFederation,
        FideRating = FideRating,
        NationalRating = NationalRating,
        Club = NullIfBlank(Club),
        BirthYear = BirthYear,
        Gender = string.IsNullOrWhiteSpace(Gender) ? "M" : Gender,
        NationalId = NullIfBlank(NationalId),
        FideId = NullIfBlank(FideId)
    };

    /// <summary>Writes the form into an existing player (including <see cref="Status"/>).</summary>
    public void ApplyTo(Player player)
    {
        player.LastName = LastName.Trim();
        player.FirstName = FirstName.Trim();
        player.Title = NullIfBlank(Title);
        player.Federation = NormalizedFederation;
        player.FideRating = FideRating;
        player.NationalRating = NationalRating;
        player.Club = NullIfBlank(Club);
        player.BirthYear = BirthYear;
        player.Gender = string.IsNullOrWhiteSpace(Gender) ? "M" : Gender;
        player.NationalId = NullIfBlank(NationalId);
        player.FideId = NullIfBlank(FideId);
        player.Status = Status;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
