using System.Globalization;
using System.Text.RegularExpressions;

namespace CinePosto.Connectors.TheSpace;

/// <summary>
/// The part of Python's <c>datetime.fromisoformat</c> that the legacy connector relies on: the day and the time are read
/// as written, and an offset is checked but never applied.
/// </summary>
/// <remarks>
/// Accepted: a date as <c>yyyy-MM-dd</c> or <c>yyyyMMdd</c>, optionally followed by one separator character and a time
/// (<c>HH</c>, <c>HH:mm</c> or <c>HH:mm:ss</c> with an optional fraction, with or without colons) and an offset.
/// ISO week dates (<c>2026-W39-2</c>), which Python also accepts, are not.
/// </remarks>
internal static partial class TheSpaceDateTime
{
    public static bool TryParse(string text, out DateOnly date, out TimeOnly time)
    {
        date = default;
        time = default;

        Match match = IsoDateTime().Match(text);
        if (!match.Success)
        {
            return false;
        }

        int year = Number(match, "year");
        int month = Number(match, "month");
        int day = Number(match, "day");
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        int hour = Number(match, "hour");
        int minute = Number(match, "minute");
        if (hour > 23 || minute > 59 || Number(match, "second") > 59
            || Number(match, "offsetHour") > 23 || Number(match, "offsetMinute") > 59 || Number(match, "offsetSecond") > 59)
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        time = new TimeOnly(hour, minute);
        return true;
    }

    // A part that is not in the text counts as zero.
    private static int Number(Match match, string group) =>
        match.Groups[group].Success ? int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;

    [GeneratedRegex(
        @"\A(?<year>[0-9]{4})(?<dash>-?)(?<month>[0-9]{2})\k<dash>(?<day>[0-9]{2})"
        + @"(?:[\s\S](?<hour>[0-9]{2})(?:(?<colon>:?)(?<minute>[0-9]{2})(?:\k<colon>(?<second>[0-9]{2})(?:[.,][0-9]+)?)?)?"
        + @"(?:[+-](?<offsetHour>[0-9]{2})(?::?(?<offsetMinute>[0-9]{2})(?::?(?<offsetSecond>[0-9]{2})(?:[.,][0-9]+)?)?)?)?)?\z")]
    private static partial Regex IsoDateTime();
}
