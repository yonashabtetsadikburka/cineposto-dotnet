using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CinePosto.Domain.Models;
using CinePosto.Domain.Text;
using static CinePosto.Connectors.LegacyJson;

namespace CinePosto.Connectors.TheSpace;

/// <summary>Port of the API path of the legacy The Space connector (parity first, see ADR 0002).</summary>
/// <remarks>
/// <para>
/// A run starts a guest session with an empty POST and then asks for one day at a time. The session is a set of cookies
/// that the site sets on the first answer, so the <see cref="HttpClient"/> has to keep cookies (the default handler
/// does). Nothing is read from the body of the first answer.
/// </para>
/// <para>
/// When a request failed, the legacy code switched to a browser built to pass bot protections. That is not ported: here
/// the first failed or refused request ends the run, nothing else is asked, and the result has no film, so that the
/// showings stored before are kept.
/// </para>
/// <para>Showings are returned as the legacy connector builds them, one per session.</para>
/// </remarks>
public sealed partial class TheSpaceConnector : ICinemaConnector
{
    private static readonly string[] FallbackTimeFormats = ["HH:mm", "H:mm"];

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly TheSpaceOptions _options;

    /// <summary>Creates the connector of one venue with the given transport, clock and settings.</summary>
    public TheSpaceConnector(HttpClient httpClient, TheSpaceOptions options, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        string url = _options.AuthUrl;

        try
        {
            await StartGuestSessionAsync(url, cancellationToken).ConfigureAwait(false);

            foreach (DateOnly date in targetDates)
            {
                url = _options.FilmsUrl(date);
                await Task.Delay(_options.RequestDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
                using JsonDocument document = await FetchJsonAsync(url, cancellationToken).ConfigureAwait(false);
                ReadFilms(document.RootElement, state);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // As in the legacy code, what was read before the failure is dropped: one error, no film.
            return new ScrapeResult { Errors = [NewError("scrape", ex, _options.CinemaUrl, ex.Message)] };
        }

        List<ScrapedFilm> films = state.Films
            .Select((film, index) => film with { Showings = state.ShowingsByFilm[index] })
            .ToList();
        return new ScrapeResult { Films = films, Errors = state.Errors };
    }

    private async Task StartGuestSessionAsync(string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> FetchJsonAsync(string url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    // One attempt per request. The User-Agent belongs to the HttpClient: no header imitates a browser or the site.
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            int status = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"{request.Method} {request.RequestUri} failed with status {status}.");
        }

        return response;
    }

    private void ReadFilms(JsonElement root, ScrapeState state)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"The films response is a JSON {root.ValueKind} value, not an object.");
        }

        JsonElement films = FirstTruthy(root, "result", "films");
        if (films.ValueKind == JsonValueKind.Object)
        {
            films = FirstTruthy(films, "films", "items");
        }

        foreach (JsonElement entry in Walk(films, "films"))
        {
            AddFilm(entry, state);
        }
    }

    private void AddFilm(JsonElement entry, ScrapeState state)
    {
        try
        {
            ScrapedFilm? film = ParseFilm(entry, state.Errors);
            if (film is null)
            {
                return;
            }

            if (state.IndexByTitle.TryGetValue(film.TitleNormalized, out int index))
            {
                // Later entries add showings to the film found first; its other fields are kept.
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
            state.Errors.Add(NewError("parse_api_film", ex));
        }
    }

    private ScrapedFilm? ParseFilm(JsonElement entry, List<ConnectorError> errors)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Cannot read a film from a JSON {entry.ValueKind} value.");
        }

        // Steps run in the legacy order: a film without showings is kept, and the title is read as text at the end.
        JsonElement titleElement = FirstTruthy(entry, "filmTitle", "title");
        if (titleElement.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        string detailUrl = DetailUrl(entry);
        string poster = Absolute(TextOf(FirstTruthy(entry, "posterImageSrc", "panelImageUrl"), "poster"));
        string? description = TextOrNull(FirstTruthy(entry, "synopsisShort", "synopsis"));
        int? duration = Duration(entry);
        IReadOnlyList<string> genres = GenreNormalizer.Normalize(Property(entry, "genres"));
        string director = TruthyText(entry, "director").Trim();
        List<ScrapedShowing> showings = ParseShowings(entry, detailUrl, errors);

        string title = TextOf(titleElement, "title");
        return new ScrapedFilm
        {
            Title = title,
            TitleNormalized = TitleNormalizer.Normalize(title),
            Showings = showings,
            Poster = poster.Length == 0 ? null : poster,
            Description = description,
            Genres = genres,
            DurationMinutes = duration,
            Director = director.Length == 0 ? null : director,
        };
    }

    private string DetailUrl(JsonElement film)
    {
        string url = Absolute(TruthyText(film, "filmUrl"));
        if (url.Length > 0)
        {
            return url;
        }

        JsonElement id = FirstTruthy(film, "filmId");
        string slug = id.ValueKind switch
        {
            JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => id.GetString()!,
            JsonValueKind.Number => id.GetRawText(),
            _ => throw new InvalidOperationException($"Cannot read filmId from a JSON {id.ValueKind} value."),
        };
        return slug.Length > 0 ? $"{_options.BaseUrl}/film/{slug}" : _options.CinemaUrl;
    }

    private string Absolute(string url) =>
        url.Length == 0 || url.StartsWith("http", StringComparison.Ordinal) ? url : LegacyUrlResolver.Resolve(_options.BaseUrl, url);

    // The legacy connector builds the text "N min"; its output step then reads the minutes from it.
    private static int? Duration(JsonElement film)
    {
        JsonElement runningTime = Property(film, "runningTime");
        if (!IsTruthy(runningTime) || IsTruthy(Property(film, "isDurationUnknown")))
        {
            return null;
        }

        string? text = runningTime.ValueKind switch
        {
            JsonValueKind.String => runningTime.GetString(),
            JsonValueKind.Number => runningTime.GetRawText(),
            _ => null,
        };
        return text is null ? null : DurationParser.ParseMinutes($"{text} min");
    }

    private List<ScrapedShowing> ParseShowings(JsonElement film, string detailUrl, List<ConnectorError> errors)
    {
        List<ScrapedShowing> showings = [];
        foreach (JsonElement group in Walk(FirstTruthy(film, "showingGroups"), "showingGroups"))
        {
            // The day is the one written in the text: no time zone is applied. A group without a readable day is skipped.
            if (group.ValueKind != JsonValueKind.Object
                || !TryReadDateTime(Property(group, "date"), out DateOnly date, out _))
            {
                continue;
            }

            foreach (JsonElement session in Walk(FirstTruthy(group, "sessions"), "sessions"))
            {
                if (session.ValueKind != JsonValueKind.Object || ReadStartTime(session, errors) is not TimeOnly time)
                {
                    continue;
                }

                string? screen = TextOrNull(FirstTruthy(session, "screenName"));
                (string? language, List<string> attributes) = ReadAttributes(session);
                showings.Add(new ScrapedShowing
                {
                    CinemaSlug = CinemaSlug,
                    Date = date,
                    Times = [time],
                    Screen = screen,
                    Language = language,
                    SourceUrl = detailUrl,
                    SessionAttributes = attributes,
                });
            }
        }

        return showings;
    }

    private TimeOnly? ReadStartTime(JsonElement session, List<ConnectorError> errors)
    {
        JsonElement start = FirstTruthy(session, "startTime", "showTime");
        if (start.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (TryReadDateTime(start, out _, out TimeOnly time))
        {
            return time;
        }

        // The legacy code keeps the first five characters of a text that begins like a time, and skips anything else.
        if (start.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string text = start.GetString()!;
        string prefix = text.Length > 5 ? text[..5] : text;
        if (!TimePrefix().IsMatch(prefix))
        {
            return null;
        }

        if (TimeOnly.TryParseExact(prefix, FallbackTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            return time;
        }

        // Deviation: the legacy code keeps this text as it is, but the Domain stores a time of day.
        errors.Add(NewError("parse_time", new FormatException("A start time is not a time of day.")));
        return null;
    }

    private static (string? Language, List<string> Attributes) ReadAttributes(JsonElement session)
    {
        string? language = null;
        List<string> attributes = [];
        foreach (JsonElement attribute in Walk(FirstTruthy(session, "attributes"), "attributes"))
        {
            if (attribute.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? name = TextOrNull(FirstTruthy(attribute, "name", "value"));
            if (name is null)
            {
                continue;
            }

            attributes.Add(name);
            JsonElement type = Property(attribute, "attributeType");
            if (language is null && type.ValueKind == JsonValueKind.String && string.Equals(type.GetString(), "Language", StringComparison.Ordinal))
            {
                language = name;
            }
        }

        return (language, attributes);
    }

    // Python reads the text of the value, so a number such as 20260620 is read as a date too.
    private static bool TryReadDateTime(JsonElement value, out DateOnly date, out TimeOnly time)
    {
        date = default;
        time = default;
        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return text is not null
            && TheSpaceDateTime.TryParse(text.Replace("Z", "+00:00", StringComparison.Ordinal), out date, out time);
    }

    // Python: a or b, where a missing or empty value is false. Undefined when none is true.
    private static JsonElement FirstTruthy(JsonElement element, params ReadOnlySpan<string> names)
    {
        foreach (string name in names)
        {
            JsonElement value = Property(element, name);
            if (IsTruthy(value))
            {
                return value;
            }
        }

        return default;
    }

    // What a Python "for" loop gets from a value: the items of a list, and text for the keys of an object or the
    // characters of a string. A number or a boolean cannot be walked and raises.
    private static IEnumerable<JsonElement> Walk(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.Undefined => [],
        JsonValueKind.Array => value.EnumerateArray(),
        JsonValueKind.Object => value.EnumerateObject().Select(property => JsonSerializer.SerializeToElement(property.Name)),
        JsonValueKind.String => value.GetString()!.EnumerateRunes().Select(rune => JsonSerializer.SerializeToElement(rune.ToString())),
        _ => throw new InvalidOperationException($"Cannot walk {name}: it is a JSON {value.ValueKind} value."),
    };

    // A value used as text by the legacy code: anything else that is true raises there.
    private static string TextOf(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.Undefined => string.Empty,
        JsonValueKind.String => value.GetString() ?? string.Empty,
        _ => throw new InvalidOperationException($"Cannot read {name} from a JSON {value.ValueKind} value."),
    };

    // Deviation: the legacy code keeps a synopsis, a screen or a name of any type. Only text is stored here.
    private static string? TextOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private ConnectorError NewError(string phase, Exception exception, string? url = null, string? detail = null) => new()
    {
        CinemaSlug = CinemaSlug,
        Timestamp = _timeProvider.GetUtcNow(),
        ExceptionType = exception.GetType().Name,
        Phase = phase,
        Url = url,
        Detail = detail,
    };

    // Original Python: re.match(r"\d{1,2}:\d{2}", show_time[:5])
    [GeneratedRegex(@"\A\d{1,2}:\d{2}")]
    private static partial Regex TimePrefix();

    private sealed class ScrapeState
    {
        public List<ScrapedFilm> Films { get; } = [];

        public List<List<ScrapedShowing>> ShowingsByFilm { get; } = [];

        public Dictionary<string, int> IndexByTitle { get; } = new(StringComparer.Ordinal);

        public List<ConnectorError> Errors { get; } = [];
    }
}
