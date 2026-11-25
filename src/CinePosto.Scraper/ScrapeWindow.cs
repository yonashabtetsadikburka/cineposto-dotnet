namespace CinePosto.Scraper;

/// <summary>The reference day of a run and the days to collect.</summary>
internal sealed record ScrapeWindow(DateOnly Today, IReadOnlyList<DateOnly>? Dates)
{
    /// <summary>Number of days collected: today plus the next seven (NFR-02).</summary>
    public const int Days = 8;

    public const string TimeZoneId = "Europe/Rome";

    public static ScrapeWindow FromToday(DateOnly today)
    {
        return new ScrapeWindow(today, Enumerable.Range(0, Days).Select(today.AddDays).ToList());
    }

    /// <summary>Builds the window for the current day in the given time zone (NFR-01), whatever the server time zone is.</summary>
    public static ScrapeWindow ForNow(TimeProvider clock, TimeZoneInfo zone)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        return FromToday(DateOnly.FromDateTime(local.DateTime));
    }
}
