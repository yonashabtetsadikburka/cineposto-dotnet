using System.Net;
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
}
