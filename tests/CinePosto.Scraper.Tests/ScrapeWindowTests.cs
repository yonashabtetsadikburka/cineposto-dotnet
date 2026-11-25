using CinePosto.Scraper;

namespace CinePosto.Scraper.Tests;

public sealed class ScrapeWindowTests
{
    [Fact]
    public void FromToday_CoversTodayAndTheNextSevenDays()
    {
        ScrapeWindow window = ScrapeWindow.FromToday(new DateOnly(2026, 11, 25));

        Assert.Equal(new DateOnly(2026, 11, 25), window.Today);
        Assert.Equal(8, window.Dates!.Count);
        Assert.Equal(new DateOnly(2026, 11, 25), window.Dates[0]);
        Assert.Equal(new DateOnly(2026, 12, 2), window.Dates[^1]);
    }

    [Fact]
    public void ForNow_UsesTheDayOfTheGivenZone_NotOfUtc()
    {
        TimeZoneInfo rome = FindRome();
        FixedClock clock = new(new DateTimeOffset(2026, 11, 24, 23, 30, 0, TimeSpan.Zero));

        ScrapeWindow window = ScrapeWindow.ForNow(clock, rome);

        // 23:30 UTC is 00:30 the next day in Rome (CET, UTC+1).
        Assert.Equal(new DateOnly(2026, 11, 25), window.Today);
    }

    [Fact]
    public void ForNow_FollowsSummerTime()
    {
        FixedClock clock = new(new DateTimeOffset(2026, 7, 14, 22, 30, 0, TimeSpan.Zero));

        ScrapeWindow window = ScrapeWindow.ForNow(clock, FindRome());

        // 22:30 UTC is 00:30 the next day in Rome (CEST, UTC+2).
        Assert.Equal(new DateOnly(2026, 7, 15), window.Today);
    }

    private static TimeZoneInfo FindRome()
    {
        Assert.True(TimeZoneInfo.TryFindSystemTimeZoneById(ScrapeWindow.TimeZoneId, out TimeZoneInfo? zone), "Europe/Rome is not available on this system.");
        return zone!;
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
