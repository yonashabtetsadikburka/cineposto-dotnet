using CinePosto.Scraper;

namespace CinePosto.Scraper.Tests;

public sealed class ScraperSettingsTests
{
    [Fact]
    public void TryParse_Fixtures_ReadsTheVariables()
    {
        bool ok = ScraperSettings.TryParse(
            Env(
                ("SCRAPER_MODE", "fixtures"),
                ("SCRAPER_CLARICI_FIXTURES_DIR", "/fixtures/x"),
                ("SCRAPER_UCI_FIXTURES_DIR", "/fixtures/uci/y"),
                ("SCRAPER_THESPACE_FIXTURES_DIR", "/fixtures/thespace/z"),
                ("SCRAPER_TMDB_FIXTURES_DIR", "/fixtures/tmdb_fixtures/w"),
                ("CINEPOSTO_DB_PATH", "/data/a.db"),
                ("SCRAPER_HTTP_TIMEOUT_SECONDS", "12.5")),
            out ScraperSettings? settings,
            out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(ScraperMode.Fixtures, settings!.Mode);
        Assert.Equal("/fixtures/x", settings.ClariciFixturesDirectory);
        Assert.Equal("/fixtures/uci/y", settings.UciFixturesDirectory);
        Assert.Equal("/fixtures/thespace/z", settings.TheSpaceFixturesDirectory);
        Assert.Equal("/fixtures/tmdb_fixtures/w", settings.TmdbFixturesDirectory);
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
        Assert.Contains("SCRAPER_CLARICI_FIXTURES_DIR", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_FixturesWithoutUciDirectory_IsRejected(string? uciDirectory)
    {
        bool ok = ScraperSettings.TryParse(
            Env(("SCRAPER_MODE", "fixtures"), ("SCRAPER_CLARICI_FIXTURES_DIR", "/fixtures/x"), ("SCRAPER_UCI_FIXTURES_DIR", uciDirectory)),
            out ScraperSettings? settings,
            out string? error);

        Assert.False(ok);
        Assert.Null(settings);
        Assert.Contains("SCRAPER_UCI_FIXTURES_DIR", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_FixturesWithoutTheSpaceDirectory_IsRejected(string? theSpaceDirectory)
    {
        bool ok = ScraperSettings.TryParse(
            Env(
                ("SCRAPER_MODE", "fixtures"),
                ("SCRAPER_CLARICI_FIXTURES_DIR", "/fixtures/x"),
                ("SCRAPER_UCI_FIXTURES_DIR", "/fixtures/uci/y"),
                ("SCRAPER_THESPACE_FIXTURES_DIR", theSpaceDirectory)),
            out ScraperSettings? settings,
            out string? error);

        Assert.False(ok);
        Assert.Null(settings);
        Assert.Contains("SCRAPER_THESPACE_FIXTURES_DIR", error);
    }

    [Fact]
    public void TryParse_FixturesWithoutTheTmdbDirectory_IsRejected()
    {
        bool ok = ScraperSettings.TryParse(
            Env(
                ("SCRAPER_MODE", "fixtures"),
                ("SCRAPER_CLARICI_FIXTURES_DIR", "/fixtures/x"),
                ("SCRAPER_UCI_FIXTURES_DIR", "/fixtures/uci/y"),
                ("SCRAPER_THESPACE_FIXTURES_DIR", "/fixtures/thespace/z")),
            out ScraperSettings? settings,
            out string? error);

        Assert.False(ok);
        Assert.Null(settings);
        Assert.Contains("SCRAPER_TMDB_FIXTURES_DIR", error);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  placeholder.not-a_token  ", "placeholder.not-a_token")]
    public void TryParse_Live_ReadsTheOptionalTmdbToken(string? value, string? expected)
    {
        bool ok = ScraperSettings.TryParse(Env(("SCRAPER_MODE", "live"), ("TMDB_READ_TOKEN", value)), out ScraperSettings? settings, out _);

        Assert.True(ok);
        Assert.Equal(expected, settings!.TmdbReadToken);
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("line\nbreak")]
    [InlineData("\"quoted\"")]
    public void TryParse_RejectsATokenThatCannotBeOne_WithoutRepeatingIt(string value)
    {
        bool ok = ScraperSettings.TryParse(Env(("SCRAPER_MODE", "live"), ("TMDB_READ_TOKEN", value)), out _, out string? error);

        Assert.False(ok);
        Assert.Contains("TMDB_READ_TOKEN", error);
        Assert.DoesNotContain(value, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_NeverContainsTheToken()
    {
        ScraperSettings.TryParse(Env(("SCRAPER_MODE", "live"), ("TMDB_READ_TOKEN", "placeholder-not-a-token")), out ScraperSettings? settings, out _);

        Assert.DoesNotContain("placeholder-not-a-token", settings!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder-not-a-token", $"{settings}", StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_Fixtures_DoesNotReadTheToken()
    {
        List<string> asked = [];
        Func<string, string?> values = Env(
            ("SCRAPER_MODE", "fixtures"),
            ("SCRAPER_CLARICI_FIXTURES_DIR", "/fixtures/x"),
            ("SCRAPER_UCI_FIXTURES_DIR", "/fixtures/uci/y"),
            ("SCRAPER_THESPACE_FIXTURES_DIR", "/fixtures/thespace/z"),
            ("SCRAPER_TMDB_FIXTURES_DIR", "/fixtures/tmdb_fixtures/w"),
            ("TMDB_READ_TOKEN", "placeholder-not-a-token"));

        ScraperSettings.TryParse(
            name =>
            {
                asked.Add(name);
                return values(name);
            },
            out ScraperSettings? settings,
            out _);

        Assert.Null(settings!.TmdbReadToken);
        Assert.DoesNotContain("TMDB_READ_TOKEN", asked);
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
