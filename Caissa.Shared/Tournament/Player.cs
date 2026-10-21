using System.Globalization;
using System.Text;

namespace Caissa.Shared.Models;

public class Player
{
    private string _lastName = string.Empty;
    private string _firstName = string.Empty;
    private string? _legacyFullName;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int StartRank { get; set; } = 1;

    public string LastName
    {
        get => _lastName;
        set
        {
            _lastName = value ?? string.Empty;
            _legacyFullName = null;
        }
    }

    public string FirstName
    {
        get => _firstName;
        set
        {
            _firstName = value ?? string.Empty;
            _legacyFullName = null;
        }
    }

    public string FullName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_legacyFullName))
            {
                return _legacyFullName;
            }
            if (!string.IsNullOrWhiteSpace(_lastName) && !string.IsNullOrWhiteSpace(_firstName))
            {
                return $"{_lastName}, {_firstName}";
            }
            if (!string.IsNullOrWhiteSpace(_lastName))
            {
                return _lastName;
            }
            if (!string.IsNullOrWhiteSpace(_firstName))
            {
                return _firstName;
            }
            return string.Empty;
        }
        set
        {
            _legacyFullName = value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                var (last, first) = SplitFullName(value);
                _lastName = last;
                _firstName = first;
            }
            else
            {
                _lastName = string.Empty;
                _firstName = string.Empty;
            }
        }
    }

    public string? FideId { get; set; }
    public string? NationalId { get; set; }
    public string? Title { get; set; }
    public string Federation { get; set; } = "FID";
    public string? Club { get; set; }
    public int FideRating { get; set; }
    public int NationalRating { get; set; }
    public int? BirthYear { get; set; }
    public string Gender { get; set; } = "M"; // "M" or "F"
    public PlayerStatus Status { get; set; } = PlayerStatus.Active;
    public Dictionary<int, ByeType> ByeRequests { get; set; } = [];

    public int EffectiveRating(SeedingPrecedence precedence) => precedence switch
    {
        SeedingPrecedence.FideFirst => FideRating > 0 ? FideRating : NationalRating,
        SeedingPrecedence.NationalFirst => NationalRating > 0 ? NationalRating : FideRating,
        _ => FideRating
    };

    public static string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static (string LastName, string FirstName) SplitFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (string.Empty, string.Empty);
        }

        var trimmed = fullName.Trim();

        // 1. Check for standard "LastName, FirstName" format
        if (trimmed.Contains(','))
        {
            var parts = trimmed.Split(',', 2);
            return (parts[0].Trim(), parts[1].Trim());
        }

        // 2. Check for "FirstName LastName" or "LastName FirstName" with space
        int lastSpace = trimmed.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            return (trimmed[(lastSpace + 1)..].Trim(), trimmed[..lastSpace].Trim());
        }

        return (trimmed, string.Empty);
    }

    public static int GetTitleWeight(string? title) => title?.Trim().ToUpperInvariant() switch
    {
        "GM" => 12,
        "WGM" => 11,
        "IM" => 10,
        "WIM" => 9,
        "FM" => 8,
        "WFM" => 7,
        "CM" => 6,
        "WCM" => 5,
        "AGM" => 4,
        "AIM" => 3,
        "AFM" => 2,
        "ACM" => 1,
        _ => 0
    };
}
