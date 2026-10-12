using System.Globalization;
using System.Text.Json;
using CinePosto.Domain.Models;
using CinePosto.Domain.Text;

namespace CinePosto.Connectors.Clarici;

/// <summary>Faithful port of the legacy Clarici connector (parity first, see ADR 0002).</summary>
public sealed class ClariciConnector : ICinemaConnector
{
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ClariciOptions _options;

    /// <summary>Creates a connector with the given transport, clock and settings.</summary>
    public ClariciConnector(HttpClient httpClient, TimeProvider? timeProvider = null, ClariciOptions? options = null)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new ClariciOptions();
    }

    /// <inheritdoc />
    public string CinemaSlug => _options.CinemaSlug;

    /// <inheritdoc />
    public string CinemaName => _options.CinemaName;

    /// <inheritdoc />
    public async Task<ScrapeResult> ScrapeAsync(DateOnly today, IReadOnlyList<DateOnly>? dates, CancellationToken cancellationToken)
    {
        List<ScrapedFilm> films = new();
        List<ConnectorError> errors = new();
        HashSet<DateOnly>? wanted = dates is null || dates.Count == 0 ? null : new HashSet<DateOnly>(dates);

        JsonDocument movies;
        try
        {
            movies = await FetchJsonAsync(_options.MoviesUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(NewError("scrape", ex));
            return new ScrapeResult { Films = films, Errors = errors };
        }

        using (movies)
        {
            JsonElement data = movies.RootElement;
            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("films", out JsonElement filmsElement))
                {
                    data = filmsElement;
                }
                else if (data.TryGetProperty("movies", out JsonElement moviesElement))
                {
                    data = moviesElement;
                }
                else
                {
                    data = default;
                }
            }

            List<JsonElement> entries = new();
            if (data.ValueKind == JsonValueKind.Array)
            {
                entries.AddRange(data.EnumerateArray());
            }
            else if (data.ValueKind != JsonValueKind.Undefined)
            {
                errors.Add(NewError("parse", new InvalidOperationException("movies.json has an unexpected shape.")));
                return new ScrapeResult { Films = films, Errors = errors };
            }

            List<ConvertedEvent> convertedEvents = new();
            try
            {
                using JsonDocument settings = await FetchJsonAsync(_options.SettingsUrl, cancellationToken).ConfigureAwait(false);
                if (settings.RootElement.ValueKind == JsonValueKind.Object
                    && settings.RootElement.TryGetProperty("eventi", out JsonElement eventi)
                    && eventi.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement ev in eventi.EnumerateArray())
                    {
                        if (ev.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        convertedEvents.Add(new ConvertedEvent(ev.Clone(), EventLines(ev)));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The legacy code only logs a warning here; events are skipped and movies are kept.
                // Logging is intentionally omitted (no logging package allowed); see the parity notes.
            }

            foreach (JsonElement entry in entries)
            {
                try
                {
                    ScrapedFilm? film = ParseEntry(entry, today, wanted);
                    if (film is not null)
                    {
                        films.Add(film);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(NewError("parse_entry", ex));
                }
            }

            foreach (ConvertedEvent converted in convertedEvents)
            {
                try
                {
                    ScrapedFilm? film = ParseConvertedEvent(converted, today, wanted);
                    if (film is not null)
                    {
                        films.Add(film);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(NewError("parse_entry", ex));
                }
            }
        }

        return new ScrapeResult { Films = films, Errors = errors };
    }

    private async Task<JsonDocument> FetchJsonAsync(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GET {url} failed with status {(int)response.StatusCode}.");
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    private sealed record ConvertedEvent(JsonElement Fields, List<string> Lines);

    private List<string> EventLines(JsonElement ev)
    {
        if (!ev.TryGetProperty("shows", out JsonElement shows))
        {
            return [];
        }

        JsonElement venueShows;
        if (shows.ValueKind == JsonValueKind.Object)
        {
            if (!shows.TryGetProperty(_options.Venue, out venueShows)
                || venueShows.ValueKind == JsonValueKind.Null || venueShows.ValueKind == JsonValueKind.Undefined)
            {
                return [];
            }
        }
        else if (IsFalsy(shows))
        {
            return [];
        }
        else
        {
            throw new InvalidOperationException($"Cannot read venue shows from a JSON {shows.ValueKind} value.");
        }

        if (venueShows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> lines = new();
        foreach (JsonElement item in venueShows.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!item.TryGetProperty("date", out JsonElement date) || !IsTruthy(date))
            {
                continue;
            }

            string datePart = date.ValueKind == JsonValueKind.String ? date.GetString() ?? string.Empty : date.GetRawText();
            string timesPart = string.Empty;
            if (item.TryGetProperty("times", out JsonElement times)
                && times.ValueKind != JsonValueKind.Null && times.ValueKind != JsonValueKind.Undefined)
            {
                timesPart = times.ValueKind == JsonValueKind.String ? times.GetString() ?? string.Empty : times.GetRawText();
            }

            lines.Add($"{datePart}: {timesPart}");
        }

        return lines;
    }

    private ScrapedFilm? ParseEntry(JsonElement entry, DateOnly today, HashSet<DateOnly>? wanted)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Cannot read a film entry from a JSON {entry.ValueKind} value.");
        }

        if (entry.TryGetProperty("isVisible", out JsonElement visible) && visible.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        string title = RequiredText(entry, "title");
        if (title.Length == 0)
        {
            return null;
        }

        return BuildFilm(entry, title, VenueLines(entry), today, wanted);
    }

    private ScrapedFilm? ParseConvertedEvent(ConvertedEvent converted, DateOnly today, HashSet<DateOnly>? wanted)
    {
        string title = RequiredText(converted.Fields, "title");
        if (title.Length == 0)
        {
            return null;
        }

        return BuildFilm(converted.Fields, title, converted.Lines, today, wanted);
    }

    private ScrapedFilm? BuildFilm(JsonElement fields, string title, List<string> lines, DateOnly today, HashSet<DateOnly>? wanted)
    {
        Dictionary<DateOnly, List<TimeOnly>> byDate = new();
        foreach (string line in lines)
        {
            ParsedLine? parsed = ClariciLineParser.Parse(line, today, wanted);
            if (parsed is not null)
            {
                if (!byDate.TryGetValue(parsed.Value.Date, out List<TimeOnly>? times))
                {
                    times = new List<TimeOnly>();
                    byDate[parsed.Value.Date] = times;
                }

                times.AddRange(parsed.Value.Times);
            }
        }

        if (byDate.Count == 0)
        {
            return null;
        }

        bool isVO = fields.TryGetProperty("isVO", out JsonElement isVOElement) && IsTruthy(isVOElement);
        List<ScrapedShowing> showings = byDate
            .OrderBy(pair => pair.Key)
            .Select(pair => new ScrapedShowing
            {
                CinemaSlug = CinemaSlug,
                Date = pair.Key,
                Times = pair.Value.Distinct().OrderBy(t => t).ToList(),
                Language = isVO ? "VOST" : "ITA",
                SourceUrl = _options.SiteUrl,
            })
            .ToList();

        return new ScrapedFilm
        {
            Title = title,
            TitleNormalized = TitleNormalizer.Normalize(title),
            Showings = showings,
            Poster = MediaOrNull(fields, "image"),
            Backdrop = MediaOrNull(fields, "backdrop"),
            Description = TrimmedOrNull(fields, "description", throwOnOther: true),
            Genres = GenresOf(fields),
            DurationMinutes = DurationOf(fields),
            Director = TrimmedOrNull(fields, "director", throwOnOther: true),
        };
    }

    private List<string> VenueLines(JsonElement entry)
    {
        if (!entry.TryGetProperty("cinemas", out JsonElement cinemas)
            || cinemas.ValueKind == JsonValueKind.Null || cinemas.ValueKind == JsonValueKind.Undefined)
        {
            return [];
        }

        if (cinemas.ValueKind != JsonValueKind.Object)
        {
            if (IsFalsy(cinemas))
            {
                return [];
            }

            throw new InvalidOperationException($"Cannot read venue shows from a JSON {cinemas.ValueKind} value.");
        }

        if (!cinemas.TryGetProperty(_options.Venue, out JsonElement venue)
            || venue.ValueKind == JsonValueKind.Null || venue.ValueKind == JsonValueKind.Undefined)
        {
            return [];
        }

        if (venue.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> lines = new();
        foreach (JsonElement item in venue.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"Cannot read a showtime line from a JSON {item.ValueKind} value.");
            }

            lines.Add(item.GetString() ?? string.Empty);
        }

        return lines;
    }

    private static string RequiredText(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return (value.GetString() ?? string.Empty).Trim();
        }

        throw new InvalidOperationException($"Cannot read {property} from a JSON {value.ValueKind} value.");
    }

    private static string? TrimmedOrNull(JsonElement element, string property, bool throwOnOther)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string trimmed = (value.GetString() ?? string.Empty).Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        if (throwOnOther && IsTruthy(value))
        {
            throw new InvalidOperationException($"Cannot read {property} from a JSON {value.ValueKind} value.");
        }

        return null;
    }

    private static string? MediaOrNull(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString() ?? string.Empty;
            return text.Length == 0 ? null : text;
        }

        return IsTruthy(value) ? value.GetRawText() : null;
    }

    private static IReadOnlyList<string> GenresOf(JsonElement element)
    {
        if (!element.TryGetProperty("genre", out JsonElement genre))
        {
            return [];
        }

        if (genre.ValueKind == JsonValueKind.Array)
        {
            return GenreNormalizer.Normalize(genre);
        }

        if (genre.ValueKind == JsonValueKind.String)
        {
            string text = genre.GetString() ?? string.Empty;
            return text.Length == 0 ? [] : [text];
        }

        return IsTruthy(genre) ? [genre.GetRawText()] : [];
    }

    private static int? DurationOf(JsonElement element)
    {
        if (!element.TryGetProperty("duration", out JsonElement duration)
            || duration.ValueKind != JsonValueKind.Number
            || duration.GetRawText().IndexOfAny(['.', 'e', 'E']) >= 0
            || !duration.TryGetInt32(out int minutes))
        {
            return null;
        }

        return minutes;
    }

    private static bool IsTruthy(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.Number => element.GetDouble() != 0,
        JsonValueKind.String => !string.IsNullOrEmpty(element.GetString()),
        JsonValueKind.Array => element.GetArrayLength() != 0,
        JsonValueKind.Object => element.EnumerateObject().Any(),
        _ => true,
    };

    private static bool IsFalsy(JsonElement element) => !IsTruthy(element);

    private ConnectorError NewError(string phase, Exception? exception = null) => new()
    {
        CinemaSlug = CinemaSlug,
        Timestamp = _timeProvider.GetUtcNow(),
        ExceptionType = exception is null ? nameof(HttpRequestException) : exception.GetType().Name,
        Phase = phase,
        Url = _options.MoviesUrl,
    };
}
