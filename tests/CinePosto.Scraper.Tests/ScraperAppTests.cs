using CinePosto.Connectors.Clarici;
using CinePosto.Connectors.TheSpace;
using CinePosto.Connectors.Uci;
using CinePosto.Domain.Models;
using CinePosto.Infrastructure.Catalog;
using CinePosto.Infrastructure.Persistence;
using CinePosto.Scraper;
using Microsoft.Data.Sqlite;

namespace CinePosto.Scraper.Tests;

/// <summary>Runs the whole scraper against saved fixtures and a real SQLite file, without any network.</summary>
public sealed class ScraperAppTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// What the saved responses of all four cinemas give. "Nebbia sul Lago" is in all of them and
    /// "La Mappa di Vetro" in both venues of The Space, so there are 4 films.
    /// </summary>
    private static readonly DatasetInfo AllCinemas = new() { Cinemas = 4, Films = 4, Showings = 10 };

    private string DatabasePath => Path.Combine(_directory, "scraper.db");

    /// <summary>The summary line the scraper prints for one cinema.</summary>
    private static string LineOf(string output, string slug)
    {
        return output.Split('\n').Single(line => line.StartsWith($"cinema={slug} ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_InFixturesMode_PopulatesTheDatabase()
    {
        (int exit, string output, string error) = await RunAsync("settings_valid");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error);
        Assert.Contains("cinema=clarici-foligno mode=fixtures films=2 showings=2 errors=0", output);
        Assert.Contains("cinema=uci-perugia mode=fixtures films=2 showings=3 errors=0", output);
        Assert.Contains("cinema=the-space-corciano mode=fixtures films=2 showings=3 errors=0", output);
        Assert.Contains("cinema=the-space-terni mode=fixtures films=2 showings=2 errors=0", output);

        // A film shown in several cinemas is stored once.
        Assert.Equal(AllCinemas, await ReadInfoAsync());
    }

    [Fact]
    public async Task Run_Twice_LeavesTheCatalogUnchanged()
    {
        await RunAsync("settings_valid");
        (int exit, _, _) = await RunAsync("settings_valid");

        Assert.Equal(0, exit);
        Assert.Equal(AllCinemas, await ReadInfoAsync());
    }

    [Fact]
    public async Task Run_WhenTheSourceFails_ReturnsOneAndKeepsTheLastKnownShowings()
    {
        await RunAsync("settings_valid");

        (int exit, string output, _) = await RunAsync("movies_http500");

        Assert.Equal(1, exit);
        // A failed cinema reports the showings it kept, not the ones it found.
        Assert.Contains("cinema=clarici-foligno mode=fixtures films=0 showings=2 errors=1 showings_replaced=false", output);
        Assert.Contains("error cinema=clarici-foligno phase=scrape", output);
        Assert.Contains("showings_replaced=true", LineOf(output, "uci-perugia"));
        Assert.Equal(AllCinemas, await ReadInfoAsync());
    }

    [Fact]
    public async Task Run_WhenUciFails_ReturnsOneAndKeepsItsLastKnownShowings()
    {
        await RunAsync("settings_valid");

        (int exit, string output, _) = await RunAsync("settings_valid", "eight_day_window_http500");

        Assert.Equal(1, exit);
        Assert.Contains("cinema=uci-perugia mode=fixtures films=0 showings=3 errors=8 showings_replaced=false", output);
        Assert.Contains("error cinema=uci-perugia phase=scrape type=HttpRequestException", output);
        Assert.Contains("showings_replaced=true", LineOf(output, "clarici-foligno"));
        Assert.Equal(AllCinemas, await ReadInfoAsync());
    }

    [Fact]
    public async Task Run_WhenEverySourceFails_ReturnsOneAndStoresNothing()
    {
        (int exit, string output, _) = await RunAsync("movies_http500", "eight_day_window_http500", "eight_day_window_blocked");

        Assert.Equal(1, exit);
        Assert.Contains("cinema=clarici-foligno mode=fixtures films=0", output);
        Assert.Contains("cinema=uci-perugia mode=fixtures films=0", output);
        Assert.Contains("cinema=the-space-corciano mode=fixtures films=0", output);
        Assert.Contains("cinema=the-space-terni mode=fixtures films=0", output);
        Assert.Equal(0, (await ReadInfoAsync()).Films);
    }

    [Fact]
    public async Task Run_WhenTheSpaceRefusesTheRequests_KeepsItsShowingsAndStoresTheOthers()
    {
        await RunAsync("settings_valid");

        (int exit, string output, _) = await RunAsync("settings_valid", theSpaceFixture: "eight_day_window_blocked");

        Assert.Equal(1, exit);
        // One error per venue, not one per day: nothing else is asked after the refusal.
        Assert.Contains("cinema=the-space-corciano mode=fixtures films=0 showings=3 errors=1 showings_replaced=false", output);
        Assert.Contains("cinema=the-space-terni mode=fixtures films=0 showings=2 errors=1 showings_replaced=false", output);
        Assert.Contains("showings_replaced=true", LineOf(output, "clarici-foligno"));
        Assert.Contains("showings_replaced=true", LineOf(output, "uci-perugia"));
        Assert.Equal(AllCinemas, await ReadInfoAsync());
    }

    [Fact]
    public async Task Run_WhenThePayloadIsNotJson_ReturnsOneAndStillStoresTheOtherCinema()
    {
        (int exit, string output, _) = await RunAsync("movies_body_not_json");

        Assert.Equal(1, exit);
        Assert.Contains("cinema=clarici-foligno mode=fixtures films=0", output);
        Assert.Equal(new DatasetInfo { Cinemas = 4, Films = 3, Showings = 8 }, await ReadInfoAsync());
    }

    [Fact]
    public async Task Run_WithoutAMode_RefusesToStart()
    {
        StringWriter error = new();

        int exit = await ScraperApp.RunAsync(ScraperSettingsTests.Env(("CINEPOSTO_DB_PATH", DatabasePath)), new StringWriter(), error, TimeProvider.System, CancellationToken.None);

        Assert.Equal(ScraperApp.ExitConfiguration, exit);
        Assert.Contains("SCRAPER_MODE", error.ToString());
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Run_WithAMissingFixturesFolder_ReturnsAConfigurationError()
    {
        StringWriter error = new();

        int exit = await ScraperApp.RunAsync(
            ScraperSettingsTests.Env(
                ("SCRAPER_MODE", "fixtures"),
                ("SCRAPER_CLARICI_FIXTURES_DIR", Path.Combine(_directory, "nope")),
                ("SCRAPER_UCI_FIXTURES_DIR", FixtureTests.UciFixtureDirectory("eight_day_window")),
                ("SCRAPER_THESPACE_FIXTURES_DIR", FixtureTests.TheSpaceFixtureDirectory("eight_day_window")),
                ("CINEPOSTO_DB_PATH", DatabasePath)),
            new StringWriter(),
            error,
            TimeProvider.System,
            CancellationToken.None);

        Assert.Equal(ScraperApp.ExitConfiguration, exit);
        Assert.Contains("fixtures", error.ToString());
    }

    [Fact]
    public async Task Run_WithAMissingUciFixturesFolder_ReturnsAConfigurationError()
    {
        StringWriter error = new();

        int exit = await ScraperApp.RunAsync(
            ScraperSettingsTests.Env(
                ("SCRAPER_MODE", "fixtures"),
                ("SCRAPER_CLARICI_FIXTURES_DIR", FixtureTests.FixtureDirectory("settings_valid")),
                ("SCRAPER_UCI_FIXTURES_DIR", Path.Combine(_directory, "nope")),
                ("SCRAPER_THESPACE_FIXTURES_DIR", FixtureTests.TheSpaceFixtureDirectory("eight_day_window")),
                ("CINEPOSTO_DB_PATH", DatabasePath)),
            new StringWriter(),
            error,
            TimeProvider.System,
            CancellationToken.None);

        Assert.Equal(ScraperApp.ExitConfiguration, exit);
        Assert.Contains("UCI", error.ToString());
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public void Registry_ListsEveryCatalogCinemaWithItsOwnConnector()
    {
        using HttpClient client = new();

        IReadOnlyList<ConnectorRegistration> registrations = ConnectorRegistry.Create(
            client, TimeProvider.System, new ClariciOptions(), new UciOptions(), TheSpaceOptions.Corciano, TheSpaceOptions.Terni);

        string[] slugs = [.. registrations.Select(r => r.Connector.CinemaSlug)];
        Assert.Equal(new[] { "clarici-foligno", "uci-perugia", "the-space-corciano", "the-space-terni" }, slugs);
        Assert.Equal(slugs.Length, slugs.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(CinemaCatalog.All.Select(c => c.Slug).Order(StringComparer.Ordinal), slugs.Order(StringComparer.Ordinal));
        Assert.All(registrations, r => Assert.Same(CinemaCatalog.Find(r.Connector.CinemaSlug), r.Cinema));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<(int Exit, string Output, string Error)> RunAsync(
        string fixture, string uciFixture = "eight_day_window", string theSpaceFixture = "eight_day_window")
    {
        StringWriter output = new();
        StringWriter error = new();
        int exit = await ScraperApp.RunAsync(
            ScraperSettingsTests.Env(
                ("SCRAPER_MODE", "fixtures"),
                ("SCRAPER_CLARICI_FIXTURES_DIR", FixtureTests.FixtureDirectory(fixture)),
                ("SCRAPER_UCI_FIXTURES_DIR", FixtureTests.UciFixtureDirectory(uciFixture)),
                ("SCRAPER_THESPACE_FIXTURES_DIR", FixtureTests.TheSpaceFixtureDirectory(theSpaceFixture)),
                ("CINEPOSTO_DB_PATH", DatabasePath)),
            output,
            error,
            TimeProvider.System,
            CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }

    private async Task<DatasetInfo> ReadInfoAsync()
    {
        await using CinePostoDbContext db = CinePostoDatabase.CreateContext(DatabasePath);
        await DatabaseInitializer.MigrateAsync(db, CancellationToken.None);
        return await new SqliteShowtimeStore(db).GetDatasetInfoAsync(CancellationToken.None);
    }
}
