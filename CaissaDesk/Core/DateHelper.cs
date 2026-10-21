namespace CaissaDesk.Core;

public static class DateHelper
{
    /// <summary>
    /// Formats any date string (PGN YYYY.MM.DD, ISO YYYY-MM-DD, partial YYYY.??.??, or DD.MM.YYYY)
    /// into Slovak format: dd.MM.yyyy.
    /// </summary>
    public static string Format(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return "";

        dateStr = dateStr.Trim();

        char separator = dateStr.Contains('.') ? '.' : (dateStr.Contains('-') ? '-' : (dateStr.Contains('/') ? '/' : '\0'));
        if (separator != '\0')
        {
            var parts = dateStr.Split(separator);
            if (parts.Length == 3)
            {
                // Format: YYYY.MM.DD (standard PGN)
                if (parts[0].Length == 4)
                {
                    string y = parts[0];
                    string m = parts[1];
                    string d = parts[2];

                    if (int.TryParse(d, out int day) && int.TryParse(m, out int month) && int.TryParse(y, out int year))
                    {
                        return $"{day:D2}.{month:D2}.{year:D4}";
                    }

                    // Partial dates such as 1999.??.?? or 1999.01.??
                    string formattedMonth = int.TryParse(m, out int parsedM) ? $"{parsedM:D2}" : m;
                    string formattedDay = int.TryParse(d, out int parsedD) ? $"{parsedD:D2}" : d;
                    return $"{formattedDay}.{formattedMonth}.{y}";
                }

                // Format: DD.MM.YYYY
                if (parts[2].Length == 4)
                {
                    string d = parts[0];
                    string m = parts[1];
                    string y = parts[2];

                    if (int.TryParse(d, out int day) && int.TryParse(m, out int month) && int.TryParse(y, out int year))
                    {
                        return $"{day:D2}.{month:D2}.{year:D4}";
                    }

                    return $"{d}.{m}.{y}";
                }
            }
        }

        if (DateTime.TryParse(dateStr, out var parsed))
        {
            return parsed.ToString("dd.MM.yyyy");
        }

        return dateStr;
    }

    /// <summary>
    /// Formats a DateTime directly into Slovak format: dd.MM.yyyy.
    /// </summary>
    public static string Format(DateTime dt) => dt.ToString("dd.MM.yyyy");

    /// <summary>
    /// Converts a Slovak format (dd.MM.yyyy) or general date string into standard PGN YYYY.MM.DD.
    /// </summary>
    public static string ToPgnDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return "????.??.??";

        dateStr = dateStr.Trim();

        char separator = dateStr.Contains('.') ? '.' : (dateStr.Contains('-') ? '-' : (dateStr.Contains('/') ? '/' : '\0'));
        if (separator != '\0')
        {
            var parts = dateStr.Split(separator);
            if (parts.Length == 3)
            {
                // Already YYYY.MM.DD
                if (parts[0].Length == 4)
                {
                    if (int.TryParse(parts[0], out int y) && int.TryParse(parts[1], out int m) && int.TryParse(parts[2], out int d))
                    {
                        return $"{y:D4}.{m:D2}.{d:D2}";
                    }
                    return $"{parts[0]}.{parts[1]}.{parts[2]}";
                }

                // DD.MM.YYYY -> convert to YYYY.MM.DD
                if (parts[2].Length == 4)
                {
                    if (int.TryParse(parts[2], out int y) && int.TryParse(parts[1], out int m) && int.TryParse(parts[0], out int d))
                    {
                        return $"{y:D4}.{m:D2}.{d:D2}";
                    }
                    return $"{parts[2]}.{parts[1]}.{parts[0]}";
                }
            }
        }

        if (DateTime.TryParse(dateStr, out var parsed))
        {
            return parsed.ToString("yyyy.MM.dd");
        }

        return dateStr;
    }
}
