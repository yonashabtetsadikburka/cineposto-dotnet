using System.Globalization;
using System.Net;
using System.Text.Json;
using CinePosto.Connectors.TheSpace;
using CinePosto.Domain.Models;

namespace CinePosto.Connectors.Tests.TheSpace;

public class TheSpaceConnectorTests
{
    private const string GoldenRoot = "Golden/thespace";

    private static string GoldenDirectory => Path.Combine(AppContext.BaseDirectory, GoldenRoot);

    // A case is every folder with a params.json. The window cases of the scraper have one folder per venue.
    public static IEnumerable<object?[]> GoldenCases() =>
        Directory.GetFiles(GoldenDirectory, "params.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(GoldenDirectory, Path.GetDirectoryName(path)!).Replace('\\', '/'))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new object?[] { name });

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public async Task ScrapeAsync_MatchesGoldenCase(string caseName)
    {
        string caseDir = Path.Combine(GoldenDirectory, caseName);
        using JsonDocument paramsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "params.json")));
        JsonElement paramsRoot = paramsDoc.RootElement;

        TheSpaceOptions options = OptionsFor(paramsRoot.GetProperty("cinema_id").GetInt32()) with { RequestDelay = TimeSpan.Zero };
        DateOnly today = ParseDate(paramsRoot.GetProperty("today").GetString()!);
        IReadOnlyList<DateOnly>? dates = paramsRoot.GetProperty("dates").ValueKind == JsonValueKind.Array
            ? paramsRoot.GetProperty("dates").EnumerateArray().Select(e => ParseDate(e.GetString()!)).ToList()
            : null;

        JsonElement auth = paramsRoot.GetProperty("auth");
        Dictionary<string, (int Status, string Body)> responses = new(StringComparer.Ordinal)
        {
            [$"POST {options.AuthUrl}"] = Saved(caseDir, auth),
        };
        foreach (JsonElement response in paramsRoot.GetProperty("responses").EnumerateArray())
        {
            responses[$"GET {options.FilmsUrl(ParseDate(response.GetProperty("date").GetString()!))}"] = Saved(caseDir, response);
        }

        FakeHttpMessageHandler handler = new(responses);
        using HttpClient http = new(handler);

        ScrapeResult actual = await new TheSpaceConnector(http, options).ScrapeAsync(today, dates, CancellationToken.None);

        using JsonDocument expectedDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "expected.json")));
        JsonElement expectedRoot = expectedDoc.RootElement;

        // The same requests in the same order as the legacy code (its retries collapsed): it stops where the legacy stops.
        Assert.Equal(expectedRoot.GetProperty("requests").EnumerateArray().Select(r => r.GetString()!).ToList(), handler.Calls);

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
            // The expected value is the legacy text "N min"; the Domain stores N.
            Assert.Equal(
                TextOrNull(expectedFilm.GetProperty("duration")),
                actualFilm.DurationMinutes is int minutes ? $"{minutes.ToString(CultureInfo.InvariantCulture)} min" : null);
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
                Assert.Equal(options.CinemaSlug, actualShowing.CinemaSlug);
            }
        }

        JsonElement[] expectedErrors = expectedRoot.GetProperty("errors").EnumerateArray().ToArray();
        // Documented difference: the legacy names the failure after its browser fallback, which is not ported.
        Assert.Equal(
            expectedErrors.Select(e => e.GetProperty("phase").GetString() is "scrape_fallback" ? "scrape" : e.GetProperty("phase").GetString()!).ToList(),
            actual.Errors.Select(e => e.Phase).ToList());
        Assert.Equal(expectedErrors.Select(e => TextOrNull(e.GetProperty("url"))).ToList(), actual.Errors.Select(e => e.Url).ToList());
        Assert.All(actual.Errors, e => Assert.Equal(options.CinemaSlug, e.CinemaSlug));
    }

    [Fact]
    public void GoldenCases_HasEnoughCases()
    {
        Assert.True(GoldenCases().Count() >= 50, "Expected at least 50 golden cases.");
    }

    [Fact]
    public void Defaults_MatchLegacyConfiguration()
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDirectory, "legacy_config.json")));
        JsonElement root = config.RootElement;
        TheSpaceOptions[] venues = [TheSpaceOptions.Corciano, TheSpaceOptions.Terni];
        JsonElement[] legacyVenues = root.GetProperty("venues").EnumerateArray().ToArray();

        Assert.Equal(legacyVenues.Length, venues.Length);
        for (int i = 0; i < venues.Length; i++)
        {
            TheSpaceOptions options = venues[i];
            JsonElement legacy = legacyVenues[i];
            Assert.Equal(legacy.GetProperty("cinema_id").GetInt32(), options.CinemaId);
            Assert.Equal(legacy.GetProperty("slug").GetString(), options.CinemaSlug);
            Assert.Equal(legacy.GetProperty("name").GetString(), options.CinemaName);
            Assert.Equal(legacy.GetProperty("cinema_url").GetString(), options.CinemaUrl);
            Assert.Equal(root.GetProperty("api_base").GetString(), options.ApiBaseUrl);
            Assert.Equal(root.GetProperty("base_url").GetString(), options.BaseUrl);
            Assert.Equal(root.GetProperty("auth_url").GetString(), options.AuthUrl);
            Assert.Equal(
                legacy.GetProperty("films_url_template").GetString()!.Replace("{date}", "2026-09-05", StringComparison.Ordinal),
                options.FilmsUrl(new DateOnly(2026, 9, 5)));

            using HttpClient http = new(new FakeHttpMessageHandler([]));
            TheSpaceConnector connector = new(http, options);
            Assert.Equal(options.CinemaSlug, connector.CinemaSlug);
            Assert.Equal(options.CinemaName, connector.CinemaName);
        }
    }

    [Fact]
    public async Task ScrapeAsync_StartsAGuestSessionWithAnEmptyJsonBody_AndImitatesNothing()
    {
        FakeHttpMessageHandler handler = new(OneDay("""{"result": []}"""));
        using HttpClient http = new(handler);

        await new TheSpaceConnector(http, NoDelay).ScrapeAsync(Day, null, CancellationToken.None);

        Assert.Equal([HttpMethod.Post, HttpMethod.Get], handler.Requests.Select(r => r.Method).ToList());
        Assert.Equal("{}", handler.FirstBody);
        Assert.Equal("application/json", handler.FirstBodyType);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("application/json", Assert.Single(request.Headers.Accept).MediaType);
            Assert.Null(request.Headers.Referrer);
            Assert.Empty(request.Headers.UserAgent);
        });
    }

    [Fact]
    public void RequestDelay_IsOneSecondByDefault()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), TheSpaceOptions.Corciano.RequestDelay);
        Assert.Equal(TimeSpan.FromSeconds(1), TheSpaceOptions.Terni.RequestDelay);
    }

    [Fact]
    public async Task ScrapeAsync_PausesBeforeEachDayRequest()
    {
        DateOnly[] dates = [Day, Day.AddDays(1), Day.AddDays(2)];
        Dictionary<string, (int, string)> responses = OneDay("""{"result": []}""");
        foreach (DateOnly date in dates)
        {
            responses[$"GET {TheSpaceOptions.Corciano.FilmsUrl(date)}"] = (200, """{"result": []}""");
        }

        using HttpClient http = new(new FakeHttpMessageHandler(responses));
        RecordingTimeProvider clock = new();

        ScrapeResult result = await new TheSpaceConnector(http, TheSpaceOptions.Corciano, clock).ScrapeAsync(Day, dates, CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Equal(Enumerable.Repeat(TimeSpan.FromSeconds(1), 3), clock.Delays);
    }

    [Fact]
    public async Task ScrapeAsync_ARefusedSessionSendsNothingElse_AndSaysWhy()
    {
        DateTimeOffset now = new(2026, 6, 12, 10, 30, 0, TimeSpan.Zero);
        Dictionary<string, (int, string)> responses = OneDay("""{"result": []}""");
        responses[$"POST {TheSpaceOptions.Corciano.AuthUrl}"] = (403, "{}");
        FakeHttpMessageHandler handler = new(responses);
        using HttpClient http = new(handler);

        ScrapeResult result = await new TheSpaceConnector(http, NoDelay, new RecordingTimeProvider(now))
            .ScrapeAsync(Day, [Day, Day.AddDays(1)], CancellationToken.None);

        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
        Assert.Empty(result.Films);
        ConnectorError error = Assert.Single(result.Errors);
        Assert.Equal(now, error.Timestamp);
        Assert.Equal("scrape", error.Phase);
        Assert.Equal(nameof(HttpRequestException), error.ExceptionType);
        Assert.Contains("status 403", error.Detail);
    }

    [Fact]
    public async Task ScrapeAsync_PropagatesCancellation()
    {
        using HttpClient http = new(new FakeHttpMessageHandler([], cancel: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new TheSpaceConnector(http, NoDelay).ScrapeAsync(Day, null, CancellationToken.None));
    }

    // The tests below cover documented differences from the legacy code, which keeps any value it finds. The Domain
    // stores times of day and text, so these inputs cannot be compared with legacy output.

    [Theory]
    [InlineData("9:05", 9, 5)]
    [InlineData("9:05 circa", null, null)]
    [InlineData("25:99", null, null)]
    public async Task ScrapeAsync_ShortTimeTexts(string start, int? hour, int? minute)
    {
        ScrapeResult result = await ScrapeOneDayAsync($$"""
            {"result": [{"filmTitle": "Orari", "showingGroups": [{"date": "2026-06-12", "sessions": [{"startTime": "{{start}}"}]}]}]}
            """);

        ScrapedFilm film = Assert.Single(result.Films);
        if (hour is null)
        {
            Assert.Empty(film.Showings);
            Assert.Equal(["parse_time"], result.Errors.Select(e => e.Phase).ToList());
        }
        else
        {
            Assert.Equal([new TimeOnly(hour.Value, minute!.Value)], Assert.Single(film.Showings).Times);
            Assert.Empty(result.Errors);
        }
    }

    [Fact]
    public async Task ScrapeAsync_ValuesThatAreNotText_AreStoredAsMissing()
    {
        ScrapeResult result = await ScrapeOneDayAsync("""
            {"result": [{"filmTitle": "Strani", "synopsisShort": 5, "showingGroups": [{"date": "2026-06-12", "sessions": [
              {"startTime": "2026-06-12T18:00:00", "screenName": 7,
               "attributes": [{"name": 9, "attributeType": "Language"}, {"name": "2D"}]}]}]}]}
            """);

        ScrapedFilm film = Assert.Single(result.Films);
        Assert.Null(film.Description);
        ScrapedShowing showing = Assert.Single(film.Showings);
        Assert.Null(showing.Screen);
        Assert.Null(showing.Language);
        Assert.Equal(["2D"], showing.SessionAttributes);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("""{"a": 1}""")]
    [InlineData("""["x"]""")]
    [InlineData("true")]
    public async Task ScrapeAsync_FilmIdThatIsNotTextOrNumber_SkipsTheFilmAndReports(string filmId)
    {
        ScrapeResult result = await ScrapeOneDayAsync($$"""{"result": [{"filmTitle": "Id Strano", "filmId": {{filmId}}}]}""");

        Assert.Empty(result.Films);
        Assert.Equal(["parse_api_film"], result.Errors.Select(e => e.Phase).ToList());
    }

    private static DateOnly Day => new(2026, 6, 12);

    private static TheSpaceOptions NoDelay => TheSpaceOptions.Corciano with { RequestDelay = TimeSpan.Zero };

    private static Dictionary<string, (int, string)> OneDay(string body) => new(StringComparer.Ordinal)
    {
        [$"POST {TheSpaceOptions.Corciano.AuthUrl}"] = (200, "{}"),
        [$"GET {TheSpaceOptions.Corciano.FilmsUrl(Day)}"] = (200, body),
    };

    private static async Task<ScrapeResult> ScrapeOneDayAsync(string body)
    {
        using HttpClient http = new(new FakeHttpMessageHandler(OneDay(body)));
        return await new TheSpaceConnector(http, NoDelay).ScrapeAsync(Day, null, CancellationToken.None);
    }

    private static TheSpaceOptions OptionsFor(int cinemaId) =>
        new[] { TheSpaceOptions.Corciano, TheSpaceOptions.Terni }.Single(o => o.CinemaId == cinemaId);

    private static (int Status, string Body) Saved(string caseDir, JsonElement response) => (
        response.GetProperty("status").GetInt32(),
        File.ReadAllText(Path.Combine(caseDir, response.GetProperty("body").GetString()!)));

    private static DateOnly ParseDate(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? TextOrNull(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText(),
    };

    /// <summary>Records the pauses that are asked for and lets them end at once.</summary>
    private sealed class RecordingTimeProvider(DateTimeOffset? now = null) : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public override DateTimeOffset GetUtcNow() => now ?? base.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            return System.CreateTimer(callback, state, TimeSpan.Zero, period);
        }
    }

    /// <summary>Answers by method and address; anything else gets a 404.</summary>
    private sealed class FakeHttpMessageHandler(Dictionary<string, (int Status, string Body)> responses, bool cancel = false) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Calls { get; } = [];

        public string? FirstBody { get; private set; }

        public string? FirstBodyType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancel)
            {
                throw new OperationCanceledException();
            }

            if (Requests.Count == 0 && request.Content is not null)
            {
                FirstBody = await request.Content.ReadAsStringAsync(cancellationToken);
                FirstBodyType = request.Content.Headers.ContentType?.MediaType;
            }

            Requests.Add(request);
            string call = $"{request.Method} {request.RequestUri!.AbsoluteUri}";
            Calls.Add(call);
            if (!responses.TryGetValue(call, out (int Status, string Body) response))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage((HttpStatusCode)response.Status) { Content = new StringContent(response.Body) };
        }
    }
}
