using System.Text.Json;
using CinePosto.Connectors.Uci;

namespace CinePosto.Scraper.Fixtures;

/// <summary>
/// The saved responses of the UCI programming API, read from a folder: <c>params.json</c> lists one response per day
/// (<c>date</c>, <c>status</c> and the name of the file that holds the <c>body</c>). The layout is the one of the golden
/// cases used by the connector tests. The days of the run come from the Clarici case, so this case only has to cover them:
/// a day without a response gets a 404, as if the site did not have the page.
/// </summary>
internal sealed record UciFixtureCase
{
    public required IReadOnlyList<FixtureDay> Days { get; init; }

    /// <summary>Reads a fixture folder. Throws <see cref="InvalidOperationException"/> when it is not usable.</summary>
    public static UciFixtureCase Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"UCI fixtures directory '{directory}' does not exist.");
        }

        string paramsPath = Path.Combine(directory, "params.json");
        if (!File.Exists(paramsPath))
        {
            throw new InvalidOperationException($"UCI fixtures directory '{directory}' has no params.json.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(paramsPath));
        return new UciFixtureCase { Days = FixtureEntries.ReadDays(document.RootElement, directory) };
    }

    /// <summary>The saved responses by address, as the connector asks for them.</summary>
    public Dictionary<string, FixtureResponse> ToResponses(UciOptions options)
    {
        return Days.ToDictionary(
            day => options.ProgrammingUrl(day.Date),
            day => new FixtureResponse(day.Status, day.Body),
            StringComparer.Ordinal);
    }
}
