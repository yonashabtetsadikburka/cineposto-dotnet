using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CinePosto.Domain.Models;
using CinePosto.Domain.Text;
using static CinePosto.Connectors.LegacyJson;

namespace CinePosto.Connectors.Uci;

/// <summary>Faithful port of the legacy UCI connector (parity first, see ADR 0002).</summary>
/// <remarks>
/// The source answers one request per day and lists every film of the catalogue, flagging with <c>not_today</c> the ones with no
/// screenings that day. Showings are returned as the legacy code builds them, one per format and variant, without merging those
/// that share date, language, screen and attributes: the legacy code does that when it writes its output, not in the connector.
/// </remarks>
public sealed partial class UciConnector : ICinemaConnector
{
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly UciOptions _options;

    /// <summary>Creates a connector with the given transport, clock and settings.</summary>
    public UciConnector(HttpClient httpClient, TimeProvider? timeProvider = null, UciOptions? options = null)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new UciOptions();
    }

    /// <inheritdoc />
    public string CinemaSlug => _options.CinemaSlug;

    /// <inheritdoc />
    public string CinemaName => _options.CinemaName;

    /// <inheritdoc />
    public async Task<ScrapeResult> ScrapeAsync(DateOnly today, IReadOnlyList<DateOnly>? dates, CancellationToken cancellationToken)
    {
        // The legacy code falls back to today when no dates are given.
        IReadOnlyList<DateOnly> targetDates = dates is null || dates.Count == 0 ? [today] : dates;
        ScrapeState state = new();

        foreach (DateOnly date in targetDates)
        {
            string dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string url = _options.ProgrammingUrl(date);
            try
            {
                using JsonDocument document = await FetchJsonAsync(url, cancellationToken).ConfigureAwait(false);
                ReadProgramming(document.RootElement, date, dateText, state);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                state.Errors.Add(NewError("scrape", ex, url));
            }
        }

        List<ScrapedFilm> films = state.Films
            .Select((film, index) => film with { Showings = state.ShowingsByFilm[index] })
            .ToList();
        return new ScrapeResult { Films = films, Errors = state.Errors };
    }

    /// <summary>Removes tags from a description the way the legacy code does: decode the entities first, then drop anything between angle brackets.</summary>
    internal static string StripHtml(string text) => HtmlTag().Replace(WebUtility.HtmlDecode(text), string.Empty).Trim();

    private async Task<JsonDocument> FetchJsonAsync(string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GET {url} failed with status {(int)response.StatusCode}.");
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    private void ReadProgramming(JsonElement root, DateOnly date, string dateText, ScrapeState state)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"The programming response is a JSON {root.ValueKind} value, not an object.");
        }

        if (!root.TryGetProperty("data", out JsonElement data))
        {
            return;
        }

        switch (data.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (JsonElement movie in data.EnumerateArray())
                {
                    AddMovie(movie, date, dateText, state);
                }

                break;

            case JsonValueKind.Object:
                // The legacy code walks the keys of the object, which are text and cannot be read as films.
                foreach (JsonProperty unused in data.EnumerateObject())
                {
                    state.Errors.Add(NewError("parse_programming_movie", NotAMovie(JsonValueKind.String)));
                }

                break;

            case JsonValueKind.String:
                // Same for a string: the legacy code walks its characters.
                foreach (Rune unused in (data.GetString() ?? string.Empty).EnumerateRunes())
                {
                    state.Errors.Add(NewError("parse_programming_movie", NotAMovie(JsonValueKind.String)));
                }

                break;

            default:
                throw new InvalidOperationException($"The programming data is a JSON {data.ValueKind} value, not a list.");
        }
    }

    private void AddMovie(JsonElement movie, DateOnly date, string dateText, ScrapeState state)
    {
        try
        {
            if (movie.ValueKind != JsonValueKind.Object)
            {
                throw NotAMovie(movie.ValueKind);
            }

            // The source lists its whole catalogue and marks the films that are not shown on the requested day.
            if (movie.TryGetProperty("not_today", out JsonElement notToday) && notToday.ValueKind == JsonValueKind.True)
            {
                return;
            }

            ScrapedFilm? film = ParseMovie(movie, date, dateText, state.Errors);
            if (film is null)
            {
                return;
            }

            if (state.IndexByTitle.TryGetValue(film.TitleNormalized, out int index))
            {
                // Later days add showings to the film found first; its other fields are kept.
                state.ShowingsByFilm[index].AddRange(film.Showings);
            }
            else
            {
                state.IndexByTitle[film.TitleNormalized] = state.Films.Count;
                state.Films.Add(film);
                state.ShowingsByFilm.Add([.. film.Showings]);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            state.Errors.Add(NewError("parse_programming_movie", ex));
        }
    }

    private ScrapedFilm? ParseMovie(JsonElement movie, DateOnly date, string dateText, List<ConnectorError> errors)
    {
        // Steps run in the legacy order, because the order decides whether a malformed film is skipped silently or reported.
        JsonElement titleElement = Property(movie, "title");
        if (!IsTruthy(titleElement))
        {
            return null;
        }

        string slug = TruthyText(movie, "slug");
        string detailUrl = slug.Length > 0 ? $"{_options.BaseUrl}/film/{slug}/" : _options.BaseUrl;

        string poster = TruthyText(movie, "poster");
        if (poster.Length == 0)
        {
            poster = TruthyText(movie, "top_image");
        }

        if (poster.Length > 0 && !poster.StartsWith("http", StringComparison.Ordinal))
        {
            poster = LegacyUrlResolver.Resolve(_options.BaseUrl, poster);
        }

        IReadOnlyList<string> genres = GenreNormalizer.Normalize(Property(movie, "genres"));

        string description = TruthyText(movie, "description");
        if (description.Length > 0)
        {
            description = StripHtml(description);
        }

        List<ScrapedShowing> showings = BuildShowings(Property(movie, "screens"), date, dateText, detailUrl, errors);
        if (showings.Count == 0)
        {
            return null;
        }

        // The legacy code only reads the title as text here, after the showings: a non-text title is an error only for films that have showings.
        if (titleElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Cannot read title from a JSON {titleElement.ValueKind} value.");
        }

        string title = titleElement.GetString() ?? string.Empty;
        return new ScrapedFilm
        {
            Title = title,
            TitleNormalized = TitleNormalizer.Normalize(title),
            Showings = showings,
            Poster = poster.Length == 0 ? null : poster,
            Description = description.Length == 0 ? null : description,
            Genres = genres,
        };
    }

    private List<ScrapedShowing> BuildShowings(JsonElement screens, DateOnly date, string dateText, string detailUrl, List<ConnectorError> errors)
    {
        List<ScrapedShowing> showings = [];
        switch (screens.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Object:
            case JsonValueKind.String:
                // Missing, or an object or text: walking it finds no group of screens.
                return showings;

            case JsonValueKind.Array:
                break;

            default:
                throw new InvalidOperationException($"Cannot read screens from a JSON {screens.ValueKind} value.");
        }

        foreach (JsonElement group in screens.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonProperty format in group.EnumerateObject())
            {
                if (format.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement variant in format.Value.EnumerateArray())
                {
                    if (variant.ValueKind == JsonValueKind.Object)
                    {
                        showings.Add(BuildShowing(variant, format.Name, date, dateText, detailUrl, errors));
                    }
                }
            }
        }

        return showings;
    }

    private ScrapedShowing BuildShowing(JsonElement variant, string format, DateOnly date, string dateText, string detailUrl, List<ConnectorError> errors)
    {
        string? language = null;
        if (Property(variant, "language") is { ValueKind: JsonValueKind.Object } languageObject)
        {
            language = TextOrNull(Property(languageObject, "name"));
        }

        // A screen given as an object is read by name. Any other non-empty value means "the screen is the format".
        string? screenName = null;
        JsonElement screen = Property(variant, "screen");
        if (IsTruthy(screen))
        {
            screenName = screen.ValueKind == JsonValueKind.Object ? TextOrNull(Property(screen, "name")) : format;
        }

        return new ScrapedShowing
        {
            CinemaSlug = CinemaSlug,
            Date = date,
            Times = ReadTimes(Property(variant, "performances"), dateText, errors),
            Screen = string.IsNullOrEmpty(screenName) ? null : screenName,
            Language = string.IsNullOrEmpty(language) ? null : language,
            SourceUrl = detailUrl,
            SessionAttributes = format.Length > 0 && !string.Equals(format, screenName, StringComparison.Ordinal) ? [format] : [],
        };
    }

    private List<TimeOnly> ReadTimes(JsonElement performances, string dateText, List<ConnectorError> errors)
    {
        List<TimeOnly> times = [];
        if (!IsTruthy(performances))
        {
            return times;
        }

        switch (performances.ValueKind)
        {
            case JsonValueKind.Object:
            case JsonValueKind.String:
                // Walking an object or text finds no performance.
                return times;

            case JsonValueKind.Array:
                break;

            default:
                throw new InvalidOperationException($"Cannot read performances from a JSON {performances.ValueKind} value.");
        }

        foreach (JsonElement performance in performances.EnumerateArray())
        {
            if (performance.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // A performance on another day is dropped. A missing or empty day is kept.
            JsonElement day = Property(performance, "day");
            if (IsTruthy(day)
                && !(day.ValueKind == JsonValueKind.String && string.Equals(day.GetString(), dateText, StringComparison.Ordinal)))
            {
                continue;
            }

            JsonElement start = Property(performance, "actual_start_at");
            if (!IsTruthy(start))
            {
                continue;
            }

            if (start.ValueKind == JsonValueKind.String
                && TimeOnly.TryParseExact(start.GetString(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
            {
                times.Add(time);
            }
            else
            {
                // Deviation: the legacy code keeps any value as text, but the Domain stores a time of day. The film is kept without this time.
                errors.Add(NewError("parse_time", new FormatException("A start time is not a time of day in HH:mm form.")));
            }
        }

        times.Sort();
        return times;
    }

    // Deviation: the legacy code keeps a name of any type. Only text is stored here.
    private static string? TextOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static InvalidOperationException NotAMovie(JsonValueKind kind) =>
        new($"Cannot read a film from a JSON {kind} value.");

    private ConnectorError NewError(string phase, Exception exception, string? url = null) => new()
    {
        CinemaSlug = CinemaSlug,
        Timestamp = _timeProvider.GetUtcNow(),
        ExceptionType = exception.GetType().Name,
        Phase = phase,
        Url = url,
    };

    // Original Python: re.sub(r"<[^>]+>", "", ...)
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    private sealed class ScrapeState
    {
        public List<ScrapedFilm> Films { get; } = [];

        public List<List<ScrapedShowing>> ShowingsByFilm { get; } = [];

        public Dictionary<string, int> IndexByTitle { get; } = new(StringComparer.Ordinal);

        public List<ConnectorError> Errors { get; } = [];
    }
}
