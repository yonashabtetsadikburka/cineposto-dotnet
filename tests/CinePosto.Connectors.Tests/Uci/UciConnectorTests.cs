using System.Globalization;
using System.Net;
using System.Text.Json;
using CinePosto.Connectors.Uci;
using CinePosto.Domain.Models;

namespace CinePosto.Connectors.Tests.Uci;

public class UciConnectorTests
{
    private const string GoldenRoot = "Golden/uci";

    public static IEnumerable<object?[]> GoldenCases() =>
        Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, GoldenRoot))
            .OrderBy(Path.GetFileName)
            .Select(dir => new object?[] { Path.GetFileName(dir) });

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public async Task ScrapeAsync_MatchesGoldenCase(string caseName)
    {
        string caseDir = Path.Combine(AppContext.BaseDirectory, GoldenRoot, caseName);
        using JsonDocument paramsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "params.json")));
        JsonElement paramsRoot = paramsDoc.RootElement;

        DateOnly today = ParseDate(paramsRoot.GetProperty("today").GetString()!);
        IReadOnlyList<DateOnly>? dates = paramsRoot.GetProperty("dates").ValueKind == JsonValueKind.Array
            ? paramsRoot.GetProperty("dates").EnumerateArray().Select(e => ParseDate(e.GetString()!)).ToList()
            : null;

        Dictionary<string, (int Status, string Body)> responses = new(StringComparer.Ordinal);
        foreach (JsonElement response in paramsRoot.GetProperty("responses").EnumerateArray())
        {
            responses[UrlFor(response.GetProperty("date").GetString()!)] = (
                response.GetProperty("status").GetInt32(),
                File.ReadAllText(Path.Combine(caseDir, response.GetProperty("body").GetString()!)));
        }

        using HttpClient http = new(new FakeHttpMessageHandler(responses));
        UciConnector connector = new(http);

        ScrapeResult actual = await connector.ScrapeAsync(today, dates, CancellationToken.None);

        using JsonDocument expectedDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "expected.json")));
        JsonElement expectedRoot = expectedDoc.RootElement;

        JsonElement[] expectedFilms = expectedRoot.GetProperty("films").EnumerateArray().ToArray();
        Assert.Equal(expectedFilms.Length, actual.Films.Count);
        for (int i = 0; i < expectedFilms.Length; i++)
        {
            JsonElement expectedFilm = expectedFilms[i];
            ScrapedFilm actualFilm = actual.Films[i];
            Assert.Equal(expectedFilm.GetProperty("title").GetString(), actualFilm.Title);
            Assert.Equal(expectedFilm.GetProperty("title_normalized").GetString(), actualFilm.TitleNormalized);
            Assert.Equal(TextOrNull(expectedFilm.GetProperty("poster")), actualFilm.Poster);
            Assert.Equal(TextOrNull(expectedFilm.GetProperty("backdrop")), actualFilm.Backdrop);
            Assert.Equal(TextOrNull(expectedFilm.GetProperty("description")), actualFilm.Description);
            Assert.Equal(expectedFilm.GetProperty("genres").EnumerateArray().Select(g => g.GetString()!).ToList(), actualFilm.Genres);
            Assert.Equal(TextOrNull(expectedFilm.GetProperty("duration")), actualFilm.DurationMinutes?.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(TextOrNull(expectedFilm.GetProperty("director")), actualFilm.Director);

            JsonElement[] expectedShowings = expectedFilm.GetProperty("showings").EnumerateArray().ToArray();
            Assert.Equal(expectedShowings.Length, actualFilm.Showings.Count);
            for (int j = 0; j < expectedShowings.Length; j++)
            {
                JsonElement expectedShowing = expectedShowings[j];
                ScrapedShowing actualShowing = actualFilm.Showings[j];
                Assert.Equal(expectedShowing.GetProperty("date").GetString(), actualShowing.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Assert.Equal(
                    expectedShowing.GetProperty("times").EnumerateArray().Select(t => t.GetString()!).ToList(),
                    actualShowing.Times.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)).ToList());
                Assert.Equal(TextOrNull(expectedShowing.GetProperty("screen")), actualShowing.Screen);
                Assert.Equal(TextOrNull(expectedShowing.GetProperty("language")), actualShowing.Language);
                Assert.Equal(
                    expectedShowing.GetProperty("session_attributes").EnumerateArray().Select(a => a.GetString()!).ToList(),
                    actualShowing.SessionAttributes);
                Assert.Equal(expectedShowing.GetProperty("source_url").GetString(), actualShowing.SourceUrl);
                Assert.Equal("uci-perugia", actualShowing.CinemaSlug);
            }
        }

        JsonElement[] expectedErrors = expectedRoot.GetProperty("errors").EnumerateArray().ToArray();
        Assert.Equal(expectedErrors.Select(e => e.GetProperty("phase").GetString()!).ToList(), actual.Errors.Select(e => e.Phase).ToList());
        Assert.Equal(
            expectedErrors.Select(e => e.GetProperty("date").ValueKind == JsonValueKind.Null ? null : UrlFor(e.GetProperty("date").GetString()!)).ToList(),
            actual.Errors.Select(e => e.Url).ToList());
        Assert.All(actual.Errors, e => Assert.Equal("uci-perugia", e.CinemaSlug));
    }

    [Fact]
    public void GoldenCases_HasEnoughCases()
    {
        int count = Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, GoldenRoot)).Length;
        Assert.True(count >= 30, $"Golden folder has only {count} cases; expected at least 30.");
    }

    [Fact]
    public void Defaults_MatchLegacyConfiguration()
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, GoldenRoot, "legacy_config.json")));
        UciOptions options = new();

        Assert.Equal(config.RootElement.GetProperty("programming_url_template").GetString(), options.ProgrammingUrlTemplate);
        Assert.Equal(config.RootElement.GetProperty("base_url").GetString(), options.BaseUrl);
        Assert.Equal(config.RootElement.GetProperty("cinema_slug").GetString(), options.CinemaSlug);
        Assert.Equal(config.RootElement.GetProperty("cinema_name").GetString(), options.CinemaName);

        using HttpClient http = new(new FakeHttpMessageHandler([]));
        UciConnector connector = new(http);
        Assert.Equal(options.CinemaSlug, connector.CinemaSlug);
        Assert.Equal(options.CinemaName, connector.CinemaName);
    }

    [Fact]
    public async Task ScrapeAsync_AsksForJson()
    {
        FakeHttpMessageHandler handler = new(new Dictionary<string, (int, string)> { [UrlFor("2026-06-12")] = (200, """{"data": []}""") });
        using HttpClient http = new(handler);

        await new UciConnector(http).ScrapeAsync(new DateOnly(2026, 6, 12), null, CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/json");
    }

    [Fact]
    public async Task ScrapeAsync_RequestsEachDateOnce_InOrder()
    {
        FakeHttpMessageHandler handler = new(new Dictionary<string, (int, string)>
        {
            [UrlFor("2026-06-14")] = (200, """{"data": []}"""),
            [UrlFor("2026-06-12")] = (200, """{"data": []}"""),
        });
        using HttpClient http = new(handler);

        await new UciConnector(http).ScrapeAsync(
            new DateOnly(2026, 6, 12),
            [new DateOnly(2026, 6, 14), new DateOnly(2026, 6, 12)],
            CancellationToken.None);

        Assert.Equal(
            [UrlFor("2026-06-14"), UrlFor("2026-06-12")],
            handler.Requests.Select(r => r.RequestUri!.AbsoluteUri).ToList());
    }

    [Fact]
    public async Task ScrapeAsync_PropagatesCancellation()
    {
        using HttpClient http = new(new FakeHttpMessageHandler([], cancel: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new UciConnector(http).ScrapeAsync(new DateOnly(2026, 6, 12), null, CancellationToken.None));
    }

    [Fact]
    public async Task ScrapeAsync_UsesTheClockForErrorTimestamps()
    {
        DateTimeOffset now = new(2026, 6, 12, 10, 30, 0, TimeSpan.Zero);
        using HttpClient http = new(new FakeHttpMessageHandler([]));

        ScrapeResult result = await new UciConnector(http, new FixedTimeProvider(now))
            .ScrapeAsync(new DateOnly(2026, 6, 12), null, CancellationToken.None);

        ConnectorError error = Assert.Single(result.Errors);
        Assert.Equal(now, error.Timestamp);
        Assert.Equal("scrape", error.Phase);
        Assert.Equal(nameof(HttpRequestException), error.ExceptionType);
    }

    // The tests below cover documented deviations from the legacy code. The legacy code keeps any value it finds;
    // the Domain stores times of day and text, so these inputs cannot be compared with legacy output.

    [Fact]
    public async Task ScrapeAsync_TimeThatIsNotAClockTime_IsDroppedAndReported()
    {
        ScrapeResult result = await ScrapeOneDayAsync("""
            {"data": [{"title": "Orari Strani", "slug": "orari-strani", "screens": [{"2D": [{
              "language": {"name": "ITA"}, "screen": {"name": "2D"},
              "performances": [
                {"actual_start_at": "20:00"}, {"actual_start_at": "9:05"}, {"actual_start_at": "25:99"},
                {"actual_start_at": "sera"}, {"actual_start_at": 1800}, {"actual_start_at": "18:00:00"}]}]}]}]}
            """);

        ScrapedShowing showing = Assert.Single(Assert.Single(result.Films).Showings);
        Assert.Equal([new TimeOnly(9, 5), new TimeOnly(20, 0)], showing.Times);
        Assert.Equal(["parse_time", "parse_time", "parse_time", "parse_time"], result.Errors.Select(e => e.Phase).ToList());
    }

    [Theory]
    [InlineData("""{"title": "Slug Numero", "slug": 5, "screens": [{"2D": [{"performances": [{"actual_start_at": "18:00"}]}]}]}""")]
    [InlineData("""{"title": "Slug Lista", "slug": ["a"], "screens": [{"2D": [{"performances": [{"actual_start_at": "18:00"}]}]}]}""")]
    public async Task ScrapeAsync_SlugThatIsNotText_SkipsTheFilmAndReports(string movieJson)
    {
        ScrapeResult result = await ScrapeOneDayAsync($$"""{"data": [{{movieJson}}]}""");

        Assert.Empty(result.Films);
        Assert.Equal(["parse_programming_movie"], result.Errors.Select(e => e.Phase).ToList());
    }

    [Fact]
    public async Task ScrapeAsync_NamesThatAreNotText_AreStoredAsMissing()
    {
        ScrapeResult result = await ScrapeOneDayAsync("""
            {"data": [{"title": "Nomi Strani", "slug": "nomi-strani", "screens": [{"3D": [{
              "language": {"name": 5}, "screen": {"name": ["x"]}, "performances": [{"actual_start_at": "18:00"}]}]}]}]}
            """);

        ScrapedShowing showing = Assert.Single(Assert.Single(result.Films).Showings);
        Assert.Null(showing.Language);
        Assert.Null(showing.Screen);
        Assert.Equal(["3D"], showing.SessionAttributes);
        Assert.Empty(result.Errors);
    }

    private static async Task<ScrapeResult> ScrapeOneDayAsync(string body)
    {
        FakeHttpMessageHandler handler = new(new Dictionary<string, (int, string)> { [UrlFor("2026-06-12")] = (200, body) });
        using HttpClient http = new(handler);
        return await new UciConnector(http).ScrapeAsync(new DateOnly(2026, 6, 12), null, CancellationToken.None);
    }

    private static string UrlFor(string date) => new UciOptions().ProgrammingUrlTemplate.Replace("{date}", date, StringComparison.Ordinal);

    private static DateOnly ParseDate(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? TextOrNull(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText(),
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHttpMessageHandler(Dictionary<string, (int Status, string Body)> responses, bool cancel = false) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancel)
            {
                throw new OperationCanceledException();
            }

            Requests.Add(request);
            if (!responses.TryGetValue(request.RequestUri!.AbsoluteUri, out (int Status, string Body) response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)response.Status) { Content = new StringContent(response.Body) });
        }
    }
}
