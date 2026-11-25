using CinePosto.Connectors.Clarici;
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

    private string DatabasePath => Path.Combine(_directory, "scraper.db");

    [Fact]
    public async Task Run_InFixturesMode_PopulatesTheDatabase()
    {
        (int exit, string output, string error) = await RunAsync("settings_valid");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error);
        Assert.Contains("cinema=clarici-foligno mode=fixtures films=2 showings=2 errors=0", output);

        DatasetInfo info = await ReadInfoAsync();
        Assert.Equal(1, info.Cinemas);
        Assert.Equal(2, info.Films);
        Assert.Equal(2, info.Showings);
    }

    [Fact]
    public async Task Run_Twice_LeavesTheCatalogUnchanged()
    {
        await RunAsync("settings_valid");
        (int exit, _, _) = await RunAsync("settings_valid");

        Assert.Equal(0, exit);
        DatasetInfo info = await ReadInfoAsync();
        Assert.Equal(new DatasetInfo { Cinemas = 1, Films = 2, Showings = 2 }, info);
    }

    [Fact]
    public async Task Run_WhenTheSourceFails_ReturnsOneAndKeepsTheLastKnownShowings()
    {
        await RunAsync("settings_valid");

        (int exit, string output, _) = await RunAsync("movies_http500");

        Assert.Equal(1, exit);
        Assert.Contains("showings_replaced=false", output);
        Assert.Contains("error cinema=clarici-foligno phase=scrape", output);
        Assert.Equal(2, (await ReadInfoAsync()).Showings);
    }

    [Fact]
    public async Task Run_WhenThePayloadIsNotJson_ReturnsOneWithoutStoringFilms()
    {
        (int exit, string output, _) = await RunAsync("movies_body_not_json");

        Assert.Equal(1, exit);
        Assert.Contains("films=0", output);
        Assert.Equal(0, (await ReadInfoAsync()).Films);
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
            ScraperSettingsTests.Env(("SCRAPER_MODE", "fixtures"), ("SCRAPER_FIXTURES_DIR", Path.Combine(_directory, "nope")), ("CINEPOSTO_DB_PATH", DatabasePath)),
            new StringWriter(),
            error,
            TimeProvider.System,
            CancellationToken.None);

        Assert.Equal(ScraperApp.ExitConfiguration, exit);
        Assert.Contains("fixtures", error.ToString());
    }

    [Fact]
    public void Registry_ListsConnectorsThatMatchTheCatalog()
    {
        using HttpClient client = new();

        IReadOnlyList<ConnectorRegistration> registrations = ConnectorRegistry.Create(client, TimeProvider.System, new ClariciOptions());

        ConnectorRegistration clarici = Assert.Single(registrations);
        Assert.Equal(CinemaCatalog.Clarici.Slug, clarici.Connector.CinemaSlug);
        Assert.NotNull(CinemaCatalog.Find(clarici.Connector.CinemaSlug));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<(int Exit, string Output, string Error)> RunAsync(string fixture)
    {
        StringWriter output = new();
        StringWriter error = new();
        int exit = await ScraperApp.RunAsync(
            ScraperSettingsTests.Env(
                ("SCRAPER_MODE", "fixtures"),
                ("SCRAPER_FIXTURES_DIR", FixtureTests.FixtureDirectory(fixture)),
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
