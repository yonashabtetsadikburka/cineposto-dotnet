using System.Globalization;
using System.Net;
using System.Text.Json;

namespace CinePosto.Scraper.Fixtures;

/// <summary>
/// Reads the saved responses listed in a <c>params.json</c>: each one has a <c>status</c> and the name of the file that
/// holds its <c>body</c>. Every method throws <see cref="InvalidOperationException"/> when an entry is not usable.
/// </summary>
internal static class FixtureEntries
{
    /// <summary>Reads a response that answers one day: it also has a <c>date</c>.</summary>
    public static FixtureDay ReadDay(JsonElement entry, string directory)
    {
        RequireObject(entry, directory);
        if (!entry.TryGetProperty("date", out JsonElement dateElement) || dateElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"params.json in '{directory}' has a response without a 'date'.");
        }

        DateOnly date = DateOnly.ParseExact(dateElement.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        FixtureResponse response = ReadResponse(entry, directory, $"The response for {date:yyyy-MM-dd}");
        return new FixtureDay(date, response.Status, response.Body!);
    }

    /// <summary>Reads the status and the body of a response. The status is 200 when the entry has none.</summary>
    public static FixtureResponse ReadResponse(JsonElement entry, string directory, string name)
    {
        RequireObject(entry, directory);
        HttpStatusCode status = entry.TryGetProperty("status", out JsonElement statusElement) && statusElement.ValueKind == JsonValueKind.Number
            ? (HttpStatusCode)statusElement.GetInt32()
            : HttpStatusCode.OK;

        // Only a file name is accepted, so a case cannot read files outside its own folder.
        if (!entry.TryGetProperty("body", out JsonElement bodyElement)
            || bodyElement.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(bodyElement.GetString())
            || !string.Equals(Path.GetFileName(bodyElement.GetString()), bodyElement.GetString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name} in '{directory}' needs a 'body' that is a file name.");
        }

        string bodyPath = Path.Combine(directory, bodyElement.GetString()!);
        if (!File.Exists(bodyPath))
        {
            throw new InvalidOperationException($"{name} in '{directory}' names the missing body file '{bodyElement.GetString()}'.");
        }

        return new FixtureResponse(status, File.ReadAllText(bodyPath));
    }

    /// <summary>Reads the <c>responses</c> list of a params object, one entry per day and no day twice.</summary>
    public static IReadOnlyList<FixtureDay> ReadDays(JsonElement root, string directory)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("responses", out JsonElement responses)
            || responses.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"params.json in '{directory}' has no 'responses' list.");
        }

        List<FixtureDay> days = [];
        HashSet<DateOnly> seen = [];
        foreach (JsonElement entry in responses.EnumerateArray())
        {
            FixtureDay day = ReadDay(entry, directory);
            if (!seen.Add(day.Date))
            {
                throw new InvalidOperationException($"params.json in '{directory}' has two responses for {day.Date:yyyy-MM-dd}.");
            }

            days.Add(day);
        }

        return days;
    }

    private static void RequireObject(JsonElement entry, string directory)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"params.json in '{directory}' has a response that is not an object.");
        }
    }
}

/// <summary>One saved response: the day it answers, its status and its body.</summary>
internal sealed record FixtureDay(DateOnly Date, HttpStatusCode Status, string Body);
