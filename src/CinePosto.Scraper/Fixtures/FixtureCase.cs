using System.Globalization;
using System.Net;
using System.Text.Json;

namespace CinePosto.Scraper.Fixtures;

/// <summary>
/// A saved scrape read from a folder: <c>params.json</c> (today, dates, response statuses), the movies body
/// (<c>movies.json</c> or <c>movies_raw.txt</c>) and an optional <c>settings.json</c>. The layout is the one of
/// the golden cases used by the connector tests.
/// </summary>
internal sealed record FixtureCase
{
    public required DateOnly Today { get; init; }

    /// <summary>Dates to scrape, or <see langword="null"/> to apply no window filter.</summary>
    public IReadOnlyList<DateOnly>? Dates { get; init; }

    public required HttpStatusCode MoviesStatus { get; init; }

    public required string MoviesBody { get; init; }

    public required HttpStatusCode SettingsStatus { get; init; }

    /// <summary>Body of the settings response, or <see langword="null"/> when the fixture has none.</summary>
    public string? SettingsBody { get; init; }

    /// <summary>Reads a fixture folder. Throws <see cref="InvalidOperationException"/> when it is not usable.</summary>
    public static FixtureCase Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"Fixtures directory '{directory}' does not exist.");
        }

        string paramsPath = Path.Combine(directory, "params.json");
        if (!File.Exists(paramsPath))
        {
            throw new InvalidOperationException($"Fixtures directory '{directory}' has no params.json.");
        }

        string moviesPath = Path.Combine(directory, "movies.json");
        if (!File.Exists(moviesPath))
        {
            moviesPath = Path.Combine(directory, "movies_raw.txt");
        }

        if (!File.Exists(moviesPath))
        {
            throw new InvalidOperationException($"Fixtures directory '{directory}' has neither movies.json nor movies_raw.txt.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(paramsPath));
        JsonElement root = document.RootElement;

        DateOnly today = ParseDate(root, "today", directory);
        IReadOnlyList<DateOnly>? dates = null;
        if (root.TryGetProperty("dates", out JsonElement datesElement) && datesElement.ValueKind == JsonValueKind.Array)
        {
            dates = datesElement.EnumerateArray()
                .Select(e => DateOnly.ParseExact(e.GetString() ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture))
                .ToList();
        }

        string settingsPath = Path.Combine(directory, "settings.json");
        return new FixtureCase
        {
            Today = today,
            Dates = dates,
            MoviesStatus = ReadStatus(root, "movies_status"),
            MoviesBody = File.ReadAllText(moviesPath),
            SettingsStatus = ReadStatus(root, "settings_status"),
            SettingsBody = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null,
        };
    }

    private static DateOnly ParseDate(JsonElement root, string name, string directory)
    {
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"params.json in '{directory}' has no '{name}' date.");
        }

        return DateOnly.ParseExact(element.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static HttpStatusCode ReadStatus(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number
            ? (HttpStatusCode)element.GetInt32()
            : HttpStatusCode.OK;
    }
}
