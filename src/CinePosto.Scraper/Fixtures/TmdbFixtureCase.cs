using System.Globalization;
using System.Text.Json;
using CinePosto.Connectors.Tmdb;

namespace CinePosto.Scraper.Fixtures;

/// <summary>
/// The saved answers of the TMDB API, read from a folder: <c>params.json</c> lists the answers to the <c>searches</c>
/// by title and to the <c>images</c> requests by <c>movie_id</c>.
/// </summary>
internal sealed record TmdbFixtureCase
{
    public required IReadOnlyDictionary<string, FixtureResponse> Searches { get; init; }

    public required IReadOnlyDictionary<string, FixtureResponse> Images { get; init; }

    /// <summary>Reads a fixture folder. Throws <see cref="InvalidOperationException"/> when it is not usable.</summary>
    public static TmdbFixtureCase Load(string directory)
    {
        string paramsPath = Path.Combine(directory, "params.json");
        if (!File.Exists(paramsPath))
        {
            throw new InvalidOperationException($"TMDB fixtures directory '{directory}' does not exist or has no params.json.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(paramsPath));
        return new TmdbFixtureCase
        {
            Searches = ReadList(document.RootElement, directory, "searches", entry =>
                entry.TryGetProperty("title", out JsonElement title) && title.ValueKind == JsonValueKind.String
                    ? title.GetString()
                    : null),
            Images = ReadList(document.RootElement, directory, "images", entry =>
                entry.TryGetProperty("movie_id", out JsonElement id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : null),
        };
    }

    /// <summary>The saved answers by address, as the provider asks for them.</summary>
    public Dictionary<string, FixtureResponse> ToResponses(TmdbOptions options)
    {
        // The handler compares the address in the form in which the request carries it.
        return Searches.Select(search => (Url: options.SearchUrl(search.Key), search.Value))
            .Concat(Images.Select(images => (Url: options.ImagesUrl(images.Key), images.Value)))
            .ToDictionary(entry => new Uri(entry.Url).AbsoluteUri, entry => entry.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, FixtureResponse> ReadList(
        JsonElement root, string directory, string name, Func<JsonElement, string?> keyOf)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(name, out JsonElement list)
            || list.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"params.json in '{directory}' has no '{name}' list.");
        }

        Dictionary<string, FixtureResponse> responses = new(StringComparer.Ordinal);
        foreach (JsonElement entry in list.EnumerateArray())
        {
            string key = (entry.ValueKind == JsonValueKind.Object ? keyOf(entry) : null)
                ?? throw new InvalidOperationException($"params.json in '{directory}' has an entry of '{name}' without its key.");
            FixtureResponse response = FixtureEntries.ReadResponse(entry, directory, $"An entry of '{name}'");
            if (!responses.TryAdd(key, response))
            {
                throw new InvalidOperationException($"params.json in '{directory}' has two entries of '{name}' for '{key}'.");
            }
        }

        return responses;
    }
}
