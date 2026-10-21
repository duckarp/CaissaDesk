using Caissa.Shared.Models;
using Caissa.Shared.Tiebreak;

namespace CaissaDesk.Localization;

/// <summary>Display texts for enum values shown in several views (keys stay literal so the key-coverage test sees them).</summary>
public static class LocalizationExtensions
{
    public static string PlayerStatusLabel(this ILocalizationService loc, PlayerStatus status) => status switch
    {
        PlayerStatus.Withdrawn => loc["PlayerStatus_Withdrawn"],
        PlayerStatus.Paused => loc["PlayerStatus_Paused"],
        _ => loc["PlayerStatus_Active"]
    };

    /// <summary>
    /// Full name of a tiebreak criterion. Built from the enum name ("Tiebreak_" + value): this is the one
    /// place that constructs localization keys dynamically; untranslated criteria fall back to the FIDE name.
    /// </summary>
    public static string TiebreakLabel(this ILocalizationService loc, TiebreakType type)
    {
        string key = TiebreakKeyPrefix + type;
        string text = loc[key];
        return text != key ? text : type.ToFullName();
    }

    public const string TiebreakKeyPrefix = "Tiebreak_";

    /// <summary>Culture of the UI language, for dates and numbers formatted in the views.</summary>
    public static System.Globalization.CultureInfo Culture(this ILocalizationService loc)
    {
        try
        {
            return System.Globalization.CultureInfo.GetCultureInfo(loc.CurrentLanguage);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return System.Globalization.CultureInfo.InvariantCulture;
        }
    }

    public static string TimeControlLabel(this ILocalizationService loc, TimeControlCategory category) => category switch
    {
        TimeControlCategory.Blitz => loc["TimeControl_Blitz"],
        TimeControlCategory.Standard => loc["TimeControl_Standard"],
        _ => loc["TimeControl_Rapid"]
    };

    /// <summary>Score as shown on the board table ("1 - 0", "½ (bye)"); "vs" while not played.</summary>
    public static string ResultLabel(this ILocalizationService loc, MatchResult result) => result switch
    {
        MatchResult.NotPlayed => loc["Common_Versus"],
        MatchResult.ByeFull => loc["Result_ByeFullScore"],
        MatchResult.ByeFullRequested => loc["Result_ByeFullRequestedScore"],
        MatchResult.ByeHalf => loc["Result_ByeHalfScore"],
        MatchResult.ByeZero => loc["Result_ByeZeroScore"],
        _ => result.ToDisplayString()
    };

    /// <summary>Short label of a requested or allocated bye ("½ bye"); "-" for none.</summary>
    public static string ByeShortLabel(this ILocalizationService loc, ByeType bye) => bye switch
    {
        ByeType.HalfPoint => loc["Bye_HalfShort"],
        ByeType.ZeroPoint => loc["Bye_ZeroShort"],
        ByeType.FullPoint or ByeType.PairingBye => loc["Bye_FullShort"],
        _ => "-"
    };

    /// <summary>Option text for a FIDE title code in the player forms ("GM (Grandmaster)").</summary>
    public static string TitleOptionLabel(this ILocalizationService loc, string code) => code switch
    {
        "GM" => loc["Title_GM"],
        "IM" => loc["Title_IM"],
        "FM" => loc["Title_FM"],
        "CM" => loc["Title_CM"],
        "WGM" => loc["Title_WGM"],
        "WIM" => loc["Title_WIM"],
        "WFM" => loc["Title_WFM"],
        "WCM" => loc["Title_WCM"],
        "AGM" => loc["Title_AGM"],
        "AIM" => loc["Title_AIM"],
        "AFM" => loc["Title_AFM"],
        "ACM" => loc["Title_ACM"],
        _ => code
    };

    /// <summary>FIDE title codes offered in the player forms, strongest first.</summary>
    public static readonly IReadOnlyList<string> TitleCodes =
        ["GM", "IM", "FM", "CM", "WGM", "WIM", "WFM", "WCM", "AGM", "AIM", "AFM", "ACM"];
}
