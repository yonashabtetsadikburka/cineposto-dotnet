using System.Globalization;
using System.Text.RegularExpressions;

namespace CinePosto.Domain.Text;

/// <summary>Faithful port of the legacy Python duration parser (parity first, see ADR 0002).</summary>
/// <remarks>Deliberate type difference: the legacy code returns an "N min" string, this returns minutes as an integer.</remarks>
public static partial class DurationParser
{
    /// <summary>Parses a raw duration to total minutes, or null when it cannot be parsed.</summary>
    public static int? ParseMinutes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string s = raw.Trim();

        Match hms = HmsFormat().Match(s);
        if (hms.Success)
        {
            int hours = int.Parse(hms.Groups[1].Value, CultureInfo.InvariantCulture);
            int minutes = int.Parse(hms.Groups[2].Value, CultureInfo.InvariantCulture);
            int seconds = hms.Groups[3].Success ? int.Parse(hms.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            return hours * 60 + minutes + (seconds >= 30 ? 1 : 0);
        }

        Match hoursMinutes = HoursMinutes().Match(s);
        if (hoursMinutes.Success)
        {
            int hours = int.Parse(hoursMinutes.Groups[1].Value, CultureInfo.InvariantCulture);
            int minutes = hoursMinutes.Groups[2].Success ? int.Parse(hoursMinutes.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            return hours * 60 + minutes;
        }

        MatchCollection numbers = FirstNumber().Matches(s);
        if (numbers.Count > 0)
        {
            return int.Parse(numbers[0].Value, CultureInfo.InvariantCulture);
        }

        return null;
    }

    // Original Python: _HMS_RE = re.compile(r"^(\d{1,2}):(\d{2})(?::(\d{2}))?$")
    private static Regex HmsFormat() => new(@"^(\d{1,2}):(\d{2})(?::(\d{2}))?$", RegexOptions.CultureInvariant);

    // Original Python: _HOURS_MIN_RE = re.compile(r"(\d+)\s*h\s*(\d+)?\s*m?", re.IGNORECASE)
    [GeneratedRegex(@"(\d+)\s*h\s*(\d+)?\s*m?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HoursMinutes();

    // Original Python: re.findall(r"\d+", s)
    [GeneratedRegex(@"\d+")]
    private static partial Regex FirstNumber();
}
