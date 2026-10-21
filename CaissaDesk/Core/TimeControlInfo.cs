using System.Globalization;
using System.Text.RegularExpressions;

namespace CaissaDesk.Core;

/// <summary>
/// Extracts the PGN TimeControl tag and maps it to a time-control class.
/// Categories follow the FIDE-style classification on the total (base) time:
/// bullet (&lt; 3 min), blitz (3-10 min), rapid (10-60 min, e.g. up to 45+10),
/// standard (classical, &gt;= 60 min).
/// </summary>
public static class TimeControlInfo
{
    /// <summary>
    /// Matches a "[TimeControl "..."]" header at the start of a line, anywhere in the PGN text.
    /// The header block always comes first, so the first match is the game's own tag.
    /// </summary>
    private static readonly Regex TimeControlTagRegex =
        new(@"^\[TimeControl\s+""([^""]*)""\]", RegexOptions.Compiled | RegexOptions.Multiline);

    // FIDE-style "moves/periodValue" segments, e.g. "40/850400 36/873840".
    private static readonly Regex FidePeriodRegex = new(@"(\d+)/(\d+)", RegexOptions.Compiled);

    // Category boundaries, in seconds of base/total time.
    public const int BulletMaxSeconds = 3 * 60;
    public const int BlitzMaxSeconds = 10 * 60;
    public const int RapidMaxSeconds = 60 * 60;

    /// <summary>
    /// Returns the raw TimeControl value from the PGN text, or null when the tag is
    /// absent, empty, or the PGN "?" convention (no time-control information).
    /// </summary>
    public static string? ExtractRaw(string? pgn)
    {
        if (string.IsNullOrEmpty(pgn))
        {
            return null;
        }

        var match = TimeControlTagRegex.Match(pgn);
        if (!match.Success)
        {
            return null;
        }

        string raw = match.Groups[1].Value.Trim();
        return raw.Length == 0 || raw == "?" || raw == "*" ? null : raw;
    }

    /// <summary>
    /// Parses the base/total time in seconds from a raw TimeControl value.
    /// Handles "180+2" (seconds + increment), "5:30" / "1:00:00" (m:ss / h:mm:ss)
    /// and the FIDE multi-period form "40/850400 36/873840" (per-period total, summed).
    /// Returns null when the value cannot be interpreted.
    /// </summary>
    public static int? BaseSeconds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();

        // FIDE-style multi-period tag: "40/850400 36/873840" (40 moves in 850.4 s, etc.).
        var periods = FidePeriodRegex.Matches(raw);
        if (periods.Count > 0)
        {
            double total = 0;
            foreach (Match p in periods)
            {
                int moves = int.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture);
                long value = long.Parse(p.Groups[2].Value, CultureInfo.InvariantCulture);
                total += PeriodSeconds(moves, value);
            }
            return total > 0 ? (int)Math.Round(total) : null;
        }

        // Strip the increment part: "180+2" -> "180", "300-2" -> "300".
        string basePart = raw.Split('+', '-')[0].Trim();
        if (basePart.Length == 0)
        {
            return null;
        }

        // Clock formats: "h:mm:ss" or "m:ss".
        if (basePart.Contains(':'))
        {
            var parts = basePart.Split(':');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int h) &&
                int.TryParse(parts[1], out int m) &&
                int.TryParse(parts[2], out int s))
            {
                return h * 3600 + m * 60 + s;
            }

            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int m2) &&
                int.TryParse(parts[1], out int s2))
            {
                return m2 * 60 + s2;
            }

            return null;
        }

        // Plain seconds (the PGN convention, e.g. Lichess "180+2" or "300").
        return double.TryParse(basePart, NumberStyles.Float, CultureInfo.InvariantCulture, out double sec) && sec > 0
            ? (int)Math.Round(sec)
            : null;
    }

    /// <summary>
    /// Decides whether a FIDE period value is milliseconds or seconds by checking
    /// the implied seconds-per-move (5-360 s/move is the plausible range), falling
    /// back to magnitude for move-less periods like "0/1800000".
    /// </summary>
    private static double PeriodSeconds(int moves, long value)
    {
        if (moves > 0)
        {
            double asMs = value / 1000.0;
            double secondsPerMove = asMs / moves;
            if (secondsPerMove >= 5 && secondsPerMove <= 360)
            {
                return asMs;
            }
            return value;
        }

        return value >= 1000 ? value / 1000.0 : value;
    }

    /// <summary>
    /// Classifies the game directly from PGN text. Returns one of "bullet",
    /// "blitz", "rapid", "standard", or "" when there is no time-control information.
    /// </summary>
    public static string ClassifyFromPgn(string? pgn)
    {
        string? raw = ExtractRaw(pgn);
        return ClassifyFromRaw(raw);
    }

    /// <summary>
    /// Classifies a raw TimeControl string into a category key: "bullet", "blitz", "rapid", or "standard".
    /// </summary>
    public static string ClassifyFromRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string lower = raw.Trim().ToLowerInvariant();
        if (lower is "bullet" or "blitz" or "rapid" or "standard")
        {
            return lower;
        }
        if (lower is "classical")
        {
            return "standard";
        }

        return Classify(BaseSeconds(raw));
    }

    /// <summary>
    /// Maps base/total seconds to a category key: "bullet", "blitz", "rapid" or "standard".
    /// Returns "" when the base time is unknown.
    /// </summary>
    public static string Classify(int? baseSeconds)
    {
        if (!baseSeconds.HasValue)
        {
            return string.Empty;
        }

        int s = baseSeconds.Value;
        if (s < BulletMaxSeconds)
        {
            return "bullet";
        }
        if (s < BlitzMaxSeconds)
        {
            return "blitz";
        }
        if (s < RapidMaxSeconds)
        {
            return "rapid";
        }
        return "standard";
    }
}
