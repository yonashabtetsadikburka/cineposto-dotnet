using System.Globalization;
using System.Text.Json;
using CinePosto.Connectors.Clarici;

namespace CinePosto.Connectors.Tests.Clarici;

public class ClariciConnectorTests
{
    private const string GoldenRoot = "Golden/clarici";

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

        DateOnly today = DateOnly.Parse(paramsRoot.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);
        IReadOnlyList<DateOnly>? dates = paramsRoot.GetProperty("dates").ValueKind == JsonValueKind.Array
            ? paramsRoot.GetProperty("dates").EnumerateArray()
                .Select(e => DateOnly.Parse(e.GetString()!, CultureInfo.InvariantCulture))
                .ToList()
            : null;

        string moviesPath = Path.Combine(caseDir, "movies.json");
        string moviesBody = File.Exists(moviesPath)
            ? File.ReadAllText(moviesPath)
            : File.ReadAllText(Path.Combine(caseDir, "movies_raw.txt"));
        int moviesStatus = paramsRoot.GetProperty("movies_status").GetInt32();
        int settingsStatus = paramsRoot.GetProperty("settings_status").GetInt32();

        string? settingsBody = null;
        string settingsPath = Path.Combine(caseDir, "settings.json");
        if (File.Exists(settingsPath))
        {
            settingsBody = File.ReadAllText(settingsPath);
        }

        ClariciOptions options = new()
        {
            MoviesUrl = "https://test.local/movies.json",
            SettingsUrl = "https://test.local/settings.json",
        };
        FakeHttpMessageHandler handler = new(options, moviesStatus, moviesBody, settingsStatus, settingsBody);
        using HttpClient http = new(handler);
        ClariciConnector connector = new(http, options: options);

        Domain.Models.ScrapeResult actual = await connector.ScrapeAsync(today, dates, CancellationToken.None);

        using JsonDocument expectedDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(caseDir, "expected.json")));
        JsonElement expectedRoot = expectedDoc.RootElement;

        JsonElement.ArrayEnumerator expectedFilms = expectedRoot.GetProperty("films").EnumerateArray();
        Assert.Equal(expectedFilms.Count(), actual.Films.Count);
        int index = 0;
        foreach (JsonElement expectedFilm in expectedFilms)
        {
            Domain.Models.ScrapedFilm actualFilm = actual.Films[index++];
            Assert.Equal(expectedFilm.GetProperty("title").GetString(), actualFilm.Title);
            Assert.Equal(expectedFilm.GetProperty("title_normalized").GetString(), actualFilm.TitleNormalized);
            Assert.Equal(expectedFilm.GetProperty("poster").ValueKind == JsonValueKind.Null ? null : expectedFilm.GetProperty("poster").GetString(), actualFilm.Poster);
            Assert.Equal(expectedFilm.GetProperty("backdrop").ValueKind == JsonValueKind.Null ? null : expectedFilm.GetProperty("backdrop").GetString(), actualFilm.Backdrop);
            Assert.Equal(expectedFilm.GetProperty("description").ValueKind == JsonValueKind.Null ? null : expectedFilm.GetProperty("description").GetString(), actualFilm.Description);
            Assert.Equal(
                expectedFilm.GetProperty("genres").EnumerateArray().Select(GenreToString).ToList(),
                actualFilm.Genres);
            Assert.Equal(
                expectedFilm.GetProperty("duration").ValueKind == JsonValueKind.Null
                    ? null
                    : int.Parse(expectedFilm.GetProperty("duration").GetString()!.Split(' ')[0], CultureInfo.InvariantCulture),
                actualFilm.DurationMinutes);
            Assert.Equal(expectedFilm.GetProperty("director").ValueKind == JsonValueKind.Null ? null : expectedFilm.GetProperty("director").GetString(), actualFilm.Director);

            JsonElement.ArrayEnumerator expectedShowings = expectedFilm.GetProperty("showings").EnumerateArray();
            Assert.Equal(expectedShowings.Count(), actualFilm.Showings.Count);
            int showingIndex = 0;
            foreach (JsonElement expectedShowing in expectedShowings)
            {
                Domain.Models.ScrapedShowing actualShowing = actualFilm.Showings[showingIndex++];
                Assert.Equal(expectedShowing.GetProperty("date").GetString(), actualShowing.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Assert.Equal(
                    expectedShowing.GetProperty("times").EnumerateArray().Select(e => e.GetString()!).ToList(),
                    actualShowing.Times.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)).ToList());
                Assert.Equal(expectedShowing.GetProperty("language").GetString(), actualShowing.Language);
                Assert.Equal(expectedShowing.GetProperty("source_url").GetString(), actualShowing.SourceUrl);
            }
        }

        Assert.Equal(
            expectedRoot.GetProperty("error_phases").EnumerateArray().Select(e => e.GetString()!).ToList(),
            actual.Errors.Select(e => e.Phase).ToList());
    }

    [Fact]
    public void GoldenCases_HasEnoughCases()
    {
        int count = Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, GoldenRoot)).Length;
        Assert.True(count >= 24, $"Golden folder has only {count} cases; expected at least 24.");
    }

    private static string GenreToString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (int Status, string? Body)> _responses;

        public FakeHttpMessageHandler(ClariciOptions options, int moviesStatus, string moviesBody, int settingsStatus, string? settingsBody)
        {
            _responses = new Dictionary<string, (int, string?)>(StringComparer.Ordinal)
            {
                [options.MoviesUrl] = (moviesStatus, moviesBody),
                [options.SettingsUrl] = settingsBody is null ? (404, null) : (settingsStatus, settingsBody),
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (int status, string? body) = _responses[request.RequestUri!.AbsoluteUri];
            HttpResponseMessage response = new((System.Net.HttpStatusCode)status);
            if (body is not null)
            {
                response.Content = new StringContent(body);
            }

            return Task.FromResult(response);
        }
    }
}
