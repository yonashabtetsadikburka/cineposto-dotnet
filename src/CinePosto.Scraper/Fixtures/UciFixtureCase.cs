using System.Globalization;
using System.Net;
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
    public required IReadOnlyList<UciFixtureDay> Days { get; init; }

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
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("responses", out JsonElement responses)
            || responses.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"params.json in '{directory}' has no 'responses' list.");
        }

        List<UciFixtureDay> days = [];
        HashSet<DateOnly> seen = [];
        foreach (JsonElement entry in responses.EnumerateArray())
        {
            UciFixtureDay day = ReadDay(entry, directory);
            if (!seen.Add(day.Date))
            {
                throw new InvalidOperationException($"params.json in '{directory}' has two responses for {day.Date:yyyy-MM-dd}.");
            }

            days.Add(day);
        }

        return new UciFixtureCase { Days = days };
    }

    /// <summary>The saved responses by address, as the connector asks for them.</summary>
    public Dictionary<string, FixtureResponse> ToResponses(UciOptions options)
    {
        return Days.ToDictionary(
            day => options.ProgrammingUrl(day.Date),
            day => new FixtureResponse(day.Status, day.Body),
            StringComparer.Ordinal);
    }

    private static UciFixtureDay ReadDay(JsonElement entry, string directory)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"params.json in '{directory}' has a response that is not an object.");
        }

        if (!entry.TryGetProperty("date", out JsonElement dateElement) || dateElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"params.json in '{directory}' has a response without a 'date'.");
        }

        DateOnly date = DateOnly.ParseExact(dateElement.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        HttpStatusCode status = entry.TryGetProperty("status", out JsonElement statusElement) && statusElement.ValueKind == JsonValueKind.Number
            ? (HttpStatusCode)statusElement.GetInt32()
            : HttpStatusCode.OK;

        // Only a file name is accepted, so a case cannot read files outside its own folder.
        if (!entry.TryGetProperty("body", out JsonElement bodyElement)
            || bodyElement.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(bodyElement.GetString())
            || !string.Equals(Path.GetFileName(bodyElement.GetString()), bodyElement.GetString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The response for {date:yyyy-MM-dd} in '{directory}' needs a 'body' that is a file name.");
        }

        string bodyPath = Path.Combine(directory, bodyElement.GetString()!);
        if (!File.Exists(bodyPath))
        {
            throw new InvalidOperationException($"The body file '{bodyElement.GetString()}' of {date:yyyy-MM-dd} is missing in '{directory}'.");
        }

        return new UciFixtureDay(date, status, File.ReadAllText(bodyPath));
    }
}

/// <summary>One saved response: the day it answers, its status and its body.</summary>
internal sealed record UciFixtureDay(DateOnly Date, HttpStatusCode Status, string Body);
