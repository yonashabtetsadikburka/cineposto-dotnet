using System.Net;
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
}
