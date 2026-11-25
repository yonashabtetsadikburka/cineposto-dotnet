using CinePosto.Scraper;

namespace CinePosto.Scraper.Tests;

public sealed class ScraperSettingsTests
{
    [Fact]
    public void TryParse_Fixtures_ReadsTheVariables()
    {
        bool ok = ScraperSettings.TryParse(
            Env(("SCRAPER_MODE", "fixtures"), ("SCRAPER_FIXTURES_DIR", "/fixtures/x"), ("CINEPOSTO_DB_PATH", "/data/a.db"), ("SCRAPER_HTTP_TIMEOUT_SECONDS", "12.5")),
            out ScraperSettings? settings,
            out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ScraperMode.Fixtures, settings!.Mode);
        Assert.Equal("/fixtures/x", settings.FixturesDirectory);
        Assert.Equal("/data/a.db", settings.DatabasePath);
        Assert.Equal(TimeSpan.FromSeconds(12.5), settings.HttpTimeout);
        Assert.Equal(ScraperSettings.DefaultUserAgent, settings.UserAgent);
    }

    [Fact]
    public void TryParse_Live_NeedsNoFixtures_AndUsesDefaults()
    {
        bool ok = ScraperSettings.TryParse(Env(("SCRAPER_MODE", "LIVE")), out ScraperSettings? settings, out _);

        Assert.True(ok);
        Assert.Equal(ScraperMode.Live, settings!.Mode);
        Assert.Equal("cineposto.db", settings.DatabasePath);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.HttpTimeout);
    }

    [Fact]
    public void TryParse_UsesTheConfiguredUserAgent()
    {
        ScraperSettings.TryParse(Env(("SCRAPER_MODE", "live"), ("SCRAPER_USER_AGENT", "Custom/1.0")), out ScraperSettings? settings, out _);

        Assert.Equal("Custom/1.0", settings!.UserAgent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("production")]
    public void TryParse_RequiresAnExplicitMode_SoTheNetworkIsNeverUsedByAccident(string? mode)
    {
        bool ok = ScraperSettings.TryParse(Env(("SCRAPER_MODE", mode)), out ScraperSettings? settings, out string? error);

        Assert.False(ok);
        Assert.Null(settings);
        Assert.Contains("SCRAPER_MODE", error);
    }

    [Fact]
    public void TryParse_FixturesWithoutDirectory_IsRejected()
    {
        bool ok = ScraperSettings.TryParse(Env(("SCRAPER_MODE", "fixtures")), out _, out string? error);

        Assert.False(ok);
        Assert.Contains("SCRAPER_FIXTURES_DIR", error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("301")]
    [InlineData("abc")]
    public void TryParse_RejectsAnInvalidTimeout(string timeout)
    {
        bool ok = ScraperSettings.TryParse(Env(("SCRAPER_MODE", "live"), ("SCRAPER_HTTP_TIMEOUT_SECONDS", timeout)), out _, out string? error);

        Assert.False(ok);
        Assert.Contains("SCRAPER_HTTP_TIMEOUT_SECONDS", error);
    }

    internal static Func<string, string?> Env(params (string Name, string? Value)[] values)
    {
        Dictionary<string, string?> map = values.ToDictionary(v => v.Name, v => v.Value);
        return name => map.GetValueOrDefault(name);
    }
}
