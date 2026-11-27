using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CinePosto.Domain.Abstractions;
using CinePosto.Domain.Models;
using static CinePosto.Connectors.LegacyJson;

namespace CinePosto.Connectors.Tmdb;

/// <summary>
/// Port of the legacy backdrop lookup (<c>tmdb.py</c>, <c>fetch_backdrop</c>; parity first, see ADR 0002): the first
/// usable search result, and among its images the widest one in English or without text.
/// </summary>
/// <remarks>
/// One difference is deliberate. The legacy code stored "not found" for ever when a request failed, so a wrong token
/// excluded every film from then on. Here a failure is reported as such, so that it is not stored.
/// </remarks>
public sealed class TmdbBackdropProvider : IBackdropProvider
{
    private readonly HttpClient _httpClient;
    private readonly AuthenticationHeaderValue _authorization;
    private readonly TmdbOptions _options;
    private readonly TimeProvider _timeProvider;
    private bool _hasAsked;

    /// <summary>
    /// Creates a provider. The token is sent only with the requests this class makes: the client is shared with the
    /// cinema connectors, so no default header is set on it.
    /// </summary>
    public TmdbBackdropProvider(HttpClient httpClient, string readAccessToken, TmdbOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(readAccessToken);
        _httpClient = httpClient;
        _authorization = new AuthenticationHeaderValue("Bearer", readAccessToken);
        _options = options ?? new TmdbOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<BackdropLookup> FindAsync(string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new BackdropLookup(BackdropLookupStatus.NotFound);
        }

        try
        {
            return await SearchAsync(title, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            // An answer that cannot be read, a network error or a time-out. The message is not kept: only the type.
            bool refused = ex is HttpRequestException
            {
                StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests,
            };
            return new BackdropLookup(
                refused ? BackdropLookupStatus.Refused : BackdropLookupStatus.Failed,
                FailureType: ex.GetType().Name);
        }
    }

    private async Task<BackdropLookup> SearchAsync(string title, CancellationToken cancellationToken)
    {
        JsonElement movie;
        using (JsonDocument search = await GetJsonAsync(_options.SearchUrl(title), cancellationToken).ConfigureAwait(false))
        {
            // Every entry is read, as in the legacy code, so an entry that is not an object fails the lookup.
            movie = ListOf(search.RootElement, "results")
                .Where(result => IsTruthy(Property(RequireObject(result), "backdrop_path")) || IsTruthy(Property(result, "id")))
                .ToList()
                .FirstOrDefault();
            if (movie.ValueKind == JsonValueKind.Undefined)
            {
                return new BackdropLookup(BackdropLookupStatus.NotFound);
            }

            movie = movie.Clone();
        }

        JsonElement path = Property(movie, "backdrop_path");
        JsonElement id = Property(movie, "id");
        if (IsTruthy(id))
        {
            try
            {
                string? picked = await PickImageAsync(IdText(id), cancellationToken).ConfigureAwait(false);
                if (picked is not null)
                {
                    return new BackdropLookup(BackdropLookupStatus.Found, _options.ImageUrl(picked));
                }
            }
            catch (Exception ex) when (IsTruthy(path)
                && (ex is HttpRequestException or JsonException
                    || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)))
            {
                // As in the legacy code, the image of the search result is used when the list of images cannot be had:
                // the request failed or timed out, or its answer cannot be read.
            }
        }

        return IsTruthy(path)
            ? new BackdropLookup(BackdropLookupStatus.Found, _options.ImageUrl(TextOf(path, "backdrop_path")))
            : new BackdropLookup(BackdropLookupStatus.NotFound);
    }

    // The widest image, then the best voted, then the first. Images with a language other than English are used only
    // when there is no other.
    private async Task<string?> PickImageAsync(string movieId, CancellationToken cancellationToken)
    {
        using JsonDocument document = await GetJsonAsync(_options.ImagesUrl(movieId), cancellationToken).ConfigureAwait(false);
        List<JsonElement> images = ListOf(document.RootElement, "backdrops").Select(RequireObject).ToList();
        if (images.Count == 0)
        {
            return null;
        }

        List<JsonElement> preferred = images.Where(HasNoTextOrEnglishText).ToList();
        List<JsonElement> pool = preferred.Count > 0 ? preferred : images;

        JsonElement best = pool[0];
        (double Width, double Vote) bestRank = Rank(best);
        foreach (JsonElement image in pool.Skip(1))
        {
            (double Width, double Vote) rank = Rank(image);
            if (rank.CompareTo(bestRank) > 0)
            {
                best = image;
                bestRank = rank;
            }
        }

        JsonElement filePath = Property(best, "file_path");
        return IsTruthy(filePath) ? TextOf(filePath, "file_path") : null;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        if (_hasAsked)
        {
            await Task.Delay(_options.RequestDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        _hasAsked = true;
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Authorization = _authorization;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    // Python: (answer or {}).get(name, []). An empty answer or a missing list is an empty list; anything else that is
    // not a list fails.
    private static IEnumerable<JsonElement> ListOf(JsonElement root, string name)
    {
        if (!IsTruthy(root))
        {
            return [];
        }

        JsonElement list = Property(RequireObject(root), name);
        return list.ValueKind switch
        {
            JsonValueKind.Undefined => [],
            JsonValueKind.Array => list.EnumerateArray(),
            JsonValueKind.Object or JsonValueKind.String when !IsTruthy(list) => [],
            _ => throw new InvalidOperationException($"Cannot read {name} from a JSON {list.ValueKind} value."),
        };
    }

    private static JsonElement RequireObject(JsonElement element) => element.ValueKind == JsonValueKind.Object
        ? element
        : throw new InvalidOperationException($"Expected an object, not a JSON {element.ValueKind} value.");

    private static bool HasNoTextOrEnglishText(JsonElement image)
    {
        JsonElement language = Property(image, "iso_639_1");
        return !IsTruthy(language)
            || (language.ValueKind == JsonValueKind.String && string.Equals(language.GetString(), "en", StringComparison.Ordinal));
    }

    private static (double Width, double Vote) Rank(JsonElement image) => (Number(image, "width"), Number(image, "vote_average"));

    // Python: value or 0. Only a number can be compared with the others.
    private static double Number(JsonElement image, string name)
    {
        JsonElement value = Property(image, name);
        if (!IsTruthy(value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : throw new InvalidOperationException($"Cannot compare {name}: it is a JSON {value.ValueKind} value.");
    }

    private static string IdText(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.Number => id.GetRawText(),
        JsonValueKind.String => id.GetString()!,
        _ => throw new InvalidOperationException($"Cannot read id from a JSON {id.ValueKind} value."),
    };

    private static string TextOf(JsonElement value, string name) => value.ValueKind == JsonValueKind.String
        ? value.GetString()!
        : throw new InvalidOperationException($"Cannot read {name} from a JSON {value.ValueKind} value.");
}
