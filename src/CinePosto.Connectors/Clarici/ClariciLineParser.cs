using System.Globalization;
using System.Text.RegularExpressions;

namespace CinePosto.Connectors.Clarici;

/// <summary>Parsed showtimes of a single schedule line.</summary>
internal record struct ParsedLine(DateOnly Date, IReadOnlyList<TimeOnly> Times);

/// <summary>Faithful port of the legacy Clarici line parser (parity first, see ADR 0002).</summary>
internal static partial class ClariciLineParser
{
    private static readonly string[] WeekdaysAbbr = ["lun", "mar", "mer", "gio", "ven", "sab", "dom"];

    /// <summary>Parses one schedule line to its date and showtimes, or null when it carries none.</summary>
    internal static ParsedLine? Parse(string line, DateOnly reference, IReadOnlySet<DateOnly>? wanted)
    {
        Match iso = IsoDate().Match(line);
        if (iso.Success)
        {
            int year = int.Parse(iso.Groups[1].Value, CultureInfo.InvariantCulture);
            int month = int.Parse(iso.Groups[2].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(iso.Groups[3].Value, CultureInfo.InvariantCulture);
            if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                return null;
            }

            DateOnly date = new(year, month, day);
            if (wanted is not null && !wanted.Contains(date))
            {
                return null;
            }

            List<TimeOnly> isoTimes = ExtractTimes(line);
            return isoTimes.Count > 0 ? new ParsedLine(date, isoTimes) : null;
        }

        Match dayMatch = DayGroup().Match(line);
        if (!dayMatch.Success)
        {
            return null;
        }

        string weekday = dayMatch.Groups[1].Value.ToLowerInvariant();
        int targetDay = int.Parse(dayMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        int targetMonth = int.Parse(dayMatch.Groups[3].Value, CultureInfo.InvariantCulture);

        DateOnly? resolved = null;
        foreach (int delta in Deltas())
        {
            DateOnly candidate = reference.AddDays(delta);
            if (candidate.Day == targetDay && candidate.Month == targetMonth)
            {
                // The Python evaluates _WEEKDAYS_ABBR.index(wd) lazily: the 'and'
                // chain only reaches it when a candidate matches day and month,
                // so unknown abbreviations throw here rather than up front.
                int weekdayIndex = Array.IndexOf(WeekdaysAbbr, weekday);
                if (weekdayIndex < 0)
                {
                    throw new ArgumentException($"Unknown weekday abbreviation '{dayMatch.Groups[1].Value}'.", nameof(line));
                }

                if (ToPythonWeekday(candidate.DayOfWeek) == weekdayIndex)
                {
                    resolved = candidate;
                    break;
                }
            }
        }

        if (resolved is null)
        {
            foreach (int delta in Deltas())
            {
                DateOnly candidate = reference.AddDays(delta);
                if (candidate.Day == targetDay && candidate.Month == targetMonth)
                {
                    resolved = candidate;
                    break;
                }
            }
        }

        if (resolved is null)
        {
            return null;
        }

        if (wanted is not null && !wanted.Contains(resolved.Value))
        {
            return null;
        }

        List<TimeOnly> dayTimes = ExtractTimes(line);
        return dayTimes.Count > 0 ? new ParsedLine(resolved.Value, dayTimes) : null;
    }

    private static IEnumerable<int> Deltas()
    {
        for (int delta = 0; delta <= 75; delta++)
        {
            yield return delta;
        }

        for (int delta = -14; delta <= -1; delta++)
        {
            yield return delta;
        }
    }

    private static int ToPythonWeekday(DayOfWeek dayOfWeek) => (int)dayOfWeek % 7;

    private static List<TimeOnly> ExtractTimes(string line)
    {
        List<TimeOnly> times = new();
        foreach (Match match in TimeCapture().Matches(line))
        {
            int hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (hours > 23 || minutes > 59)
            {
                continue;
            }

            TimeOnly time = new(hours, minutes);
            if (!times.Contains(time))
            {
                times.Add(time);
            }
        }

        times.Sort();
        return times;
    }

    // Original Python (inline in _parse_line): re.search(r"((?:19|20)\d{2})-(\d{2})-(\d{2})\s*:", line)
    [GeneratedRegex(@"((?:19|20)\d{2})-(\d{2})-(\d{2})\s*:")]
    private static partial Regex IsoDate();

    // Original Python: _DAY_RE = re.compile(r"([A-Za-z]{3})\s+(\d{1,2})/(\d{1,2})\s*:")
    [GeneratedRegex(@"([A-Za-z]{3})\s+(\d{1,2})/(\d{1,2})\s*:")]
    private static partial Regex DayGroup();

    // Original Python: _TIME_RE = re.compile(r"(\d{1,2}):(\d{2})")
    [GeneratedRegex(@"(\d{1,2}):(\d{2})")]
    private static partial Regex TimeCapture();
}
