using System.Net;
using CinePosto.Connectors.TheSpace;
using CinePosto.Connectors.Tmdb;
using CinePosto.Connectors.Uci;
using CinePosto.Scraper.Fixtures;

namespace CinePosto.Scraper.Tests;

public sealed class FixtureTests
{
    [Fact]
    public void Load_ReadsTheGoldenCaseLayout()
    {
        FixtureCase fixture = FixtureCase.Load(FixtureDirectory("settings_valid"));

        Assert.Equal(new DateOnly(2026, 9, 22), fixture.Today);
        Assert.Equal(8, fixture.Dates!.Count);
        Assert.Equal(HttpStatusCode.OK, fixture.MoviesStatus);
        Assert.Contains("Nebbia sul Lago", fixture.MoviesBody);
        Assert.Contains("Festa Sintetica", fixture.SettingsBody);
    }

    [Fact]
    public void Load_KeepsTheStatusOfAFailingCase()
    {
        FixtureCase fixture = FixtureCase.Load(FixtureDirectory("movies_http500"));

        Assert.Equal(HttpStatusCode.InternalServerError, fixture.MoviesStatus);
        Assert.Null(fixture.SettingsBody);
    }

    [Fact]
    public void Load_AcceptsARawMoviesBody()
    {
        FixtureCase fixture = FixtureCase.Load(FixtureDirectory("movies_body_not_json"));

        Assert.NotEmpty(fixture.MoviesBody);
    }

    [Fact]
    public void Load_RejectsAMissingFolder()
    {
        Assert.Throws<InvalidOperationException>(() => FixtureCase.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "does-not-exist")));
    }

    [Fact]
    public void Load_RejectsAFolderWithoutParams()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "movies.json"), "[]");

            Assert.Throws<InvalidOperationException>(() => FixtureCase.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadUci_ReadsOneResponsePerDay()
    {
        UciFixtureCase fixture = UciFixtureCase.Load(UciFixtureDirectory("eight_day_window"));

        Assert.Equal(8, fixture.Days.Count);
        Assert.Equal(new DateOnly(2026, 9, 22), fixture.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 9, 29), fixture.Days[^1].Date);
        Assert.All(fixture.Days, day => Assert.Equal(HttpStatusCode.OK, day.Status));
        Assert.Contains("Il Gatto del Faro", fixture.Days[0].Body);
        Assert.Contains("\"data\"", fixture.Days[1].Body);
    }

    [Fact]
    public void LoadUci_KeepsTheStatusOfAFailingCase()
    {
        UciFixtureCase fixture = UciFixtureCase.Load(UciFixtureDirectory("eight_day_window_http500"));

        Assert.Equal(8, fixture.Days.Count);
        Assert.All(fixture.Days, day => Assert.Equal(HttpStatusCode.InternalServerError, day.Status));
    }

    [Fact]
    public void UciToResponses_UsesTheAddressesTheConnectorAsksFor()
    {
        UciOptions options = new();
        UciFixtureCase fixture = UciFixtureCase.Load(UciFixtureDirectory("eight_day_window"));

        Dictionary<string, FixtureResponse> responses = fixture.ToResponses(options);

        Assert.Equal(8, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[options.ProgrammingUrl(new DateOnly(2026, 9, 25))].Status);
        Assert.DoesNotContain(options.ProgrammingUrl(new DateOnly(2026, 9, 30)), responses.Keys);
    }

    [Fact]
    public void LoadUci_RejectsAMissingFolder()
    {
        Assert.Throws<InvalidOperationException>(() => UciFixtureCase.Load(UciFixtureDirectory("does-not-exist")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"responses\": 5}")]
    [InlineData("{\"responses\": [5]}")]
    [InlineData("{\"responses\": [{\"status\": 200, \"body\": \"a.json\"}]}")]
    [InlineData("{\"responses\": [{\"date\": \"2026-09-22\", \"status\": 200}]}")]
    [InlineData("{\"responses\": [{\"date\": \"2026-09-22\", \"status\": 200, \"body\": \"\"}]}")]
    [InlineData("{\"responses\": [{\"date\": \"2026-09-22\", \"status\": 200, \"body\": \"missing.json\"}]}")]
    [InlineData("{\"responses\": [{\"date\": \"2026-09-22\", \"status\": 200, \"body\": \"../a.json\"}]}")]
    [InlineData("{\"responses\": [{\"date\": \"2026-09-22\", \"status\": 200, \"body\": \"a.json\"}, {\"date\": \"2026-09-22\", \"status\": 200, \"body\": \"a.json\"}]}")]
    public void LoadUci_RejectsAnUnusableCase(string paramsJson)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "params.json"), paramsJson);
            File.WriteAllText(Path.Combine(directory, "a.json"), "{\"data\": []}");

            Assert.Throws<InvalidOperationException>(() => UciFixtureCase.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadUci_RejectsAFolderWithoutParams()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Throws<InvalidOperationException>(() => UciFixtureCase.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadUci_RejectsAnInvalidDate()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "params.json"), "{\"responses\": [{\"date\": \"22/09/2026\", \"status\": 200, \"body\": \"a.json\"}]}");
            File.WriteAllText(Path.Combine(directory, "a.json"), "{}");

            Assert.Throws<FormatException>(() => UciFixtureCase.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadTheSpace_ReadsOneCasePerVenue()
    {
        TheSpaceFixtureCase fixture = TheSpaceFixtureCase.Load(TheSpaceFixtureDirectory("eight_day_window"));

        Assert.Equal([1027, 1006], fixture.Venues.Select(v => v.CinemaId));
        Assert.All(fixture.Venues, venue => Assert.Equal(HttpStatusCode.OK, venue.Session.Status));
        Assert.All(fixture.Venues, venue => Assert.Equal(8, venue.Days.Count));
        Assert.Contains("La Mappa di Vetro", fixture.Venues[0].Days[0].Body);
    }

    [Fact]
    public void TheSpaceToResponses_UsesTheAddressesTheConnectorsAskFor()
    {
        TheSpaceFixtureCase fixture = TheSpaceFixtureCase.Load(TheSpaceFixtureDirectory("eight_day_window"));

        Dictionary<string, FixtureResponse> responses = fixture.ToResponses([TheSpaceOptions.Corciano, TheSpaceOptions.Terni]);

        // One address for the guest session and eight days for each venue.
        Assert.Equal(17, responses.Count);
        Assert.Equal(HttpStatusCode.OK, responses[TheSpaceOptions.Corciano.AuthUrl].Status);
        Assert.Contains("Nebbia sul Lago", responses[TheSpaceOptions.Terni.FilmsUrl(new DateOnly(2026, 9, 25))].Body);
    }

    [Fact]
    public void TheSpaceToResponses_NeedsExactlyTheVenuesOfTheConnectors()
    {
        TheSpaceFixtureCase fixture = TheSpaceFixtureCase.Load(TheSpaceFixtureDirectory("eight_day_window"));

        Assert.Throws<InvalidOperationException>(() => fixture.ToResponses([TheSpaceOptions.Corciano]));
        Assert.Throws<InvalidOperationException>(
            () => fixture.ToResponses([TheSpaceOptions.Corciano, TheSpaceOptions.Terni with { CinemaId = 1 }]));
    }

    [Fact]
    public void LoadTheSpace_RejectsAMissingFolder()
    {
        Assert.Throws<InvalidOperationException>(() => TheSpaceFixtureCase.Load(TheSpaceFixtureDirectory("does-not-exist")));
    }

    [Fact]
    public void LoadTheSpace_RejectsAFolderWithoutVenues()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Throws<InvalidOperationException>(() => TheSpaceFixtureCase.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"auth\": {\"body\": \"a.json\"}, \"responses\": []}")]
    [InlineData("{\"cinema_id\": \"1027\", \"auth\": {\"body\": \"a.json\"}, \"responses\": []}")]
    [InlineData("{\"cinema_id\": 1027, \"responses\": []}")]
    [InlineData("{\"cinema_id\": 1027, \"auth\": 5, \"responses\": []}")]
    [InlineData("{\"cinema_id\": 1027, \"auth\": {\"body\": \"../a.json\"}, \"responses\": []}")]
    [InlineData("{\"cinema_id\": 1027, \"auth\": {\"body\": \"a.json\"}}")]
    [InlineData("{\"cinema_id\": 1027, \"auth\": {\"body\": \"a.json\"}, \"responses\": []}", "{\"cinema_id\": 1027, \"auth\": {\"body\": \"a.json\"}, \"responses\": []}")]
    [InlineData("{\"cinema_id\": 1027, \"auth\": {\"body\": \"a.json\"}, \"responses\": []}", "{\"cinema_id\": 1006, \"auth\": {\"status\": 403, \"body\": \"a.json\"}, \"responses\": []}")]
    public void LoadTheSpace_RejectsAnUnusableCase(params string[] venueParams)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (int i = 0; i < venueParams.Length; i++)
            {
                string venue = Directory.CreateDirectory(Path.Combine(directory, $"venue{i}")).FullName;
                File.WriteAllText(Path.Combine(venue, "params.json"), venueParams[i]);
                File.WriteAllText(Path.Combine(venue, "a.json"), "{}");
            }

            Assert.Throws<InvalidOperationException>(() => TheSpaceFixtureCase.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadTmdb_ReadsSearchesAndImages_ByTheAddressesTheProviderAsksFor()
    {
        TmdbOptions options = new();

        Dictionary<string, FixtureResponse> responses = TmdbFixtureCase.Load(TmdbFixtureDirectory("window")).ToResponses(options);

        Assert.Equal(6, responses.Count);
        Assert.Contains(new Uri(options.SearchUrl("La Mappa di Vetro")).AbsoluteUri, responses.Keys);
        Assert.Contains(new Uri(options.ImagesUrl("101")).AbsoluteUri, responses.Keys);
    }

    [Fact]
    public void LoadTmdb_RejectsAMissingFolder()
    {
        Assert.Throws<InvalidOperationException>(() => TmdbFixtureCase.Load(TmdbFixtureDirectory("does-not-exist")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"searches\": []}")]
    [InlineData("{\"searches\": [5], \"images\": []}")]
    [InlineData("{\"searches\": [{\"body\": \"a.json\"}], \"images\": []}")]
    [InlineData("{\"searches\": [], \"images\": [{\"movie_id\": \"1\", \"body\": \"a.json\"}]}")]
    [InlineData("{\"searches\": [{\"title\": \"a\", \"body\": \"../a.json\"}], \"images\": []}")]
    [InlineData("{\"searches\": [{\"title\": \"a\", \"body\": \"a.json\"}, {\"title\": \"a\", \"body\": \"a.json\"}], \"images\": []}")]
    public void LoadTmdb_RejectsAnUnusableCase(string paramsJson)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "params.json"), paramsJson);
            File.WriteAllText(Path.Combine(directory, "a.json"), "{}");

            Assert.Throws<InvalidOperationException>(() => TmdbFixtureCase.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Handler_AnswersFromSavedResponses_AndReturns404ForOtherAddresses()
    {
        Dictionary<string, FixtureResponse> responses = new()
        {
            ["https://example.test/a.json"] = new FixtureResponse(HttpStatusCode.OK, "{\"a\":1}"),
            ["https://example.test/gone.json"] = new FixtureResponse(HttpStatusCode.InternalServerError, null),
        };
        using HttpClient client = new(new FixtureHttpMessageHandler(responses));

        using HttpResponseMessage ok = await client.GetAsync("https://example.test/a.json");
        using HttpResponseMessage failing = await client.GetAsync("https://example.test/gone.json");
        using HttpResponseMessage unknown = await client.GetAsync("https://example.test/other.json");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("{\"a\":1}", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, failing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Handler_HonoursCancellation()
    {
        using HttpClient client = new(new FixtureHttpMessageHandler(new Dictionary<string, FixtureResponse>()));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://example.test/a.json", cancellation.Token));
    }

    internal static string FixtureDirectory(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    internal static string UciFixtureDirectory(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "uci", name);

    internal static string TheSpaceFixtureDirectory(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "thespace", name);

    internal static string TmdbFixtureDirectory(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "tmdb_fixtures", name);
}
