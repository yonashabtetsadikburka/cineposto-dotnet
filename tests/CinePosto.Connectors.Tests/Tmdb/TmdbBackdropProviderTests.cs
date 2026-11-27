using System.Net;
using System.Text.Json;
using CinePosto.Connectors.Tmdb;
using CinePosto.Domain.Models;

namespace CinePosto.Connectors.Tests.Tmdb;

public class TmdbBackdropProviderTests
{
    private const string Token = "placeholder-not-a-token";

    private static string GoldenDirectory => Path.Combine(AppContext.BaseDirectory, "Golden", "tmdb");

    // Documented difference: in these cases the legacy code stored "not found"; here nothing is known about the title.
    private static readonly Dictionary<string, BackdropLookupStatus> Failures = new(StringComparer.Ordinal)
    {
        ["search_unauthorized"] = BackdropLookupStatus.Refused,
        ["search_too_many_requests"] = BackdropLookupStatus.Refused,
        ["search_server_error"] = BackdropLookupStatus.Failed,
        ["search_body_not_json"] = BackdropLookupStatus.Failed,
        ["images_server_error_and_no_search_path"] = BackdropLookupStatus.Failed,
    };

    public static TheoryData<string> GoldenCases()
    {
        TheoryData<string> data = [];
        foreach (string directory in Directory.GetDirectories(GoldenDirectory).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(directory));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public async Task FindAsync_MatchesGoldenCase(string caseName)
    {
        string caseDir = Path.Combine(GoldenDirectory, caseName);
        using JsonDocument paramsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "params.json")));
        JsonElement paramsRoot = paramsDoc.RootElement;
        TmdbOptions options = NoDelay;
        string title = paramsRoot.GetProperty("title").GetString()!;

        Dictionary<string, (int Status, string Body)> responses = new(StringComparer.Ordinal)
        {
            [PathOf(options.SearchUrl(title))] = Saved(caseDir, paramsRoot.GetProperty("search")),
        };
        foreach (JsonElement images in paramsRoot.GetProperty("images").EnumerateArray())
        {
            responses[PathOf(options.ImagesUrl(images.GetProperty("movie_id").GetRawText()))] = Saved(caseDir, images);
        }

        FakeHandler handler = new(responses);
        using HttpClient http = new(handler);

        BackdropLookup actual = await new TmdbBackdropProvider(http, Token, options).FindAsync(title, CancellationToken.None);

        using JsonDocument expectedDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "expected.json")));
        JsonElement expected = expectedDoc.RootElement;
        string? expectedUrl = expected.GetProperty("url").GetString();

        Assert.Equal(expectedUrl, actual.Url);
        BackdropLookupStatus expectedStatus = Failures.TryGetValue(caseName, out BackdropLookupStatus failure)
            ? failure
            : expectedUrl is null ? BackdropLookupStatus.NotFound : BackdropLookupStatus.Found;
        Assert.Equal(expectedStatus, actual.Status);

        // The same requests as the legacy code, compared by address and decoded query.
        Assert.Equal(
            expected.GetProperty("requests").EnumerateArray().Select(r => Canonical(
                r.GetProperty("url").GetString()!,
                r.GetProperty("query").EnumerateObject().Select(q => (q.Name, q.Value.GetString()!)))).ToList(),
            handler.Requests.Select(r => Canonical(r.RequestUri!.GetLeftPart(UriPartial.Path), Query(r.RequestUri))).ToList());
        Assert.All(handler.Requests, r => Assert.Equal($"Bearer {Token}", r.Headers.Authorization!.ToString()));
    }

    [Fact]
    public void GoldenCases_HasEnoughCases()
    {
        Assert.True(GoldenCases().Count >= 30, "Expected at least 30 golden cases.");
    }

    [Fact]
    public void Defaults_MatchLegacyConfiguration()
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDirectory, "legacy_config.json")));
        TmdbOptions options = new();

        Assert.Equal(config.RootElement.GetProperty("api_base").GetString(), options.ApiBaseUrl);
        Assert.Equal(config.RootElement.GetProperty("image_base").GetString(), options.ImageBaseUrl);
        Assert.Equal(config.RootElement.GetProperty("backdrop_size").GetString(), options.BackdropSize);
        Assert.Equal(TimeSpan.FromSeconds(config.RootElement.GetProperty("min_call_interval_seconds").GetDouble()), options.RequestDelay);
    }

    [Fact]
    public async Task FindAsync_DoesNotPutTheTokenOnTheSharedClient()
    {
        FakeHandler handler = new(new Dictionary<string, (int, string)>());
        using HttpClient http = new(handler);

        await new TmdbBackdropProvider(http, Token, NoDelay).FindAsync("Titolo", CancellationToken.None);
        using HttpResponseMessage other = await http.GetAsync("https://example.test/cinema");

        // The same client serves the cinema connectors: their requests must not carry the token.
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        Assert.Null(handler.Requests[^1].Headers.Authorization);
        Assert.DoesNotContain("authorization", handler.Requests[^1].Headers.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(401, BackdropLookupStatus.Refused)]
    [InlineData(403, BackdropLookupStatus.Refused)]
    [InlineData(429, BackdropLookupStatus.Refused)]
    [InlineData(404, BackdropLookupStatus.Failed)]
    [InlineData(503, BackdropLookupStatus.Failed)]
    public async Task FindAsync_ReportsAFailedSearch_WithoutTheTokenInTheResult(int status, BackdropLookupStatus expected)
    {
        using HttpClient http = new(new FakeHandler(new Dictionary<string, (int, string)>
        {
            [PathOf(NoDelay.SearchUrl("Titolo"))] = (status, "{}"),
        }));

        BackdropLookup result = await new TmdbBackdropProvider(http, Token, NoDelay).FindAsync("Titolo", CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Url);
        Assert.DoesNotContain(Token, result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("""{"results": null}""")]
    [InlineData("""{"results": "abc"}""")]
    [InlineData("""{"results": ["abc"]}""")]
    [InlineData("""{"results": [{"id": [1]}]}""")]
    [InlineData("""{"results": [{"backdrop_path": 5}]}""")]
    public async Task FindAsync_AnAnswerThatCannotBeRead_Fails(string search)
    {
        using HttpClient http = new(new FakeHandler(new Dictionary<string, (int, string)>
        {
            [PathOf(NoDelay.SearchUrl("Titolo"))] = (200, search),
        }));

        BackdropLookup result = await new TmdbBackdropProvider(http, Token, NoDelay).FindAsync("Titolo", CancellationToken.None);

        Assert.Equal(BackdropLookupStatus.Failed, result.Status);
    }

    [Fact]
    public async Task FindAsync_PausesBeforeEveryRequestButTheFirst()
    {
        TmdbOptions options = new();
        using HttpClient http = new(new FakeHandler(new Dictionary<string, (int, string)>
        {
            [PathOf(options.SearchUrl("Uno"))] = (200, """{"results": [{"id": 1, "backdrop_path": "/a.jpg"}]}"""),
            [PathOf(options.ImagesUrl("1"))] = (200, """{"backdrops": []}"""),
            [PathOf(options.SearchUrl("Due"))] = (200, """{"results": []}"""),
        }));
        RecordingTimeProvider clock = new();
        TmdbBackdropProvider provider = new(http, Token, options, clock);

        await provider.FindAsync("Uno", CancellationToken.None);
        await provider.FindAsync("Due", CancellationToken.None);

        Assert.Equal(Enumerable.Repeat(TimeSpan.FromMilliseconds(300), 2), clock.Delays);
    }

    [Fact]
    public async Task FindAsync_ATimeOutOfTheClient_Fails_ButCancellingThrows()
    {
        using HttpClient http = new(new FakeHandler(new Dictionary<string, (int, string)>(), timeOut: true));
        TmdbBackdropProvider provider = new(http, Token, NoDelay);

        BackdropLookup result = await provider.FindAsync("Titolo", CancellationToken.None);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        Assert.Equal(BackdropLookupStatus.Failed, result.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.FindAsync("Titolo", cancelled.Token));
    }

    [Fact]
    public async Task FindAsync_WhenTheImagesTimeOut_UsesTheImageOfTheSearch()
    {
        using HttpClient http = new(new FakeHandler(
            new Dictionary<string, (int, string)>
            {
                [PathOf(NoDelay.SearchUrl("Titolo"))] = (200, """{"results": [{"id": 1, "backdrop_path": "/search.jpg"}]}"""),
            },
            timeOutOn: PathOf(NoDelay.ImagesUrl("1"))));

        BackdropLookup result = await new TmdbBackdropProvider(http, Token, NoDelay).FindAsync("Titolo", CancellationToken.None);

        Assert.Equal(BackdropLookupStatus.Found, result.Status);
        Assert.Equal(NoDelay.ImageUrl("/search.jpg"), result.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RefusesAMissingToken(string token)
    {
        using HttpClient http = new(new FakeHandler(new Dictionary<string, (int, string)>()));

        Assert.Throws<ArgumentException>(() => new TmdbBackdropProvider(http, token));
    }

    private static TmdbOptions NoDelay => new() { RequestDelay = TimeSpan.Zero };

    private static string PathOf(string url) => new Uri(url).AbsoluteUri;

    private static (int Status, string Body) Saved(string caseDir, JsonElement response) => (
        response.GetProperty("status").GetInt32(),
        File.ReadAllText(Path.Combine(caseDir, response.GetProperty("body").GetString()!)));

    private static IEnumerable<(string Name, string Value)> Query(Uri uri) => uri.Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .Select(pair => (Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1])));

    private static string Canonical(string url, IEnumerable<(string Name, string Value)> query) =>
        $"{url} {string.Join(" | ", query.Select(q => $"{q.Name}={q.Value}").Order(StringComparer.Ordinal))}";

    private sealed class RecordingTimeProvider : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            return System.CreateTimer(callback, state, TimeSpan.Zero, period);
        }
    }

    private sealed class FakeHandler(
        Dictionary<string, (int Status, string Body)> responses, bool timeOut = false, string? timeOutOn = null) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeOut || string.Equals(request.RequestUri!.AbsoluteUri, timeOutOn, StringComparison.Ordinal))
            {
                throw new TaskCanceledException("timed out");
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
