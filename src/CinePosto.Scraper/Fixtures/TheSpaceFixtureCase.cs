using System.Text.Json;
using CinePosto.Connectors.TheSpace;

namespace CinePosto.Scraper.Fixtures;

/// <summary>
/// The saved responses of The Space, read from a folder with one folder per venue. Each of them has a
/// <c>params.json</c> with the <c>cinema_id</c>, the answer to the guest session request (<c>auth</c>) and one response
/// per day, in the layout of the golden cases used by the connector tests. The days of the run come from the Clarici
/// case, so this case only has to cover them.
/// </summary>
internal sealed record TheSpaceFixtureCase
{
    public required IReadOnlyList<TheSpaceFixtureVenue> Venues { get; init; }

    /// <summary>Reads a fixture folder. Throws <see cref="InvalidOperationException"/> when it is not usable.</summary>
    public static TheSpaceFixtureCase Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"The Space fixtures directory '{directory}' does not exist.");
        }

        List<TheSpaceFixtureVenue> venues = [];
        foreach (string venueDirectory in Directory.GetDirectories(directory).Order(StringComparer.Ordinal))
        {
            TheSpaceFixtureVenue venue = ReadVenue(venueDirectory);
            if (venues.Any(v => v.CinemaId == venue.CinemaId))
            {
                throw new InvalidOperationException($"The Space fixtures directory '{directory}' has two cases for the venue {venue.CinemaId}.");
            }

            venues.Add(venue);
        }

        if (venues.Count == 0)
        {
            throw new InvalidOperationException($"The Space fixtures directory '{directory}' has no venue folder.");
        }

        // The guest session has one address for every venue, so the saved answers to it have to be the same.
        if (venues.Select(v => v.Session).Distinct().Count() > 1)
        {
            throw new InvalidOperationException($"The venues in '{directory}' answer the guest session request in different ways.");
        }

        return new TheSpaceFixtureCase { Venues = venues };
    }

    /// <summary>
    /// The saved responses by address, as the connectors ask for them. Every venue given must have a case, and a case
    /// for another venue is an error.
    /// </summary>
    public Dictionary<string, FixtureResponse> ToResponses(IReadOnlyList<TheSpaceOptions> venues)
    {
        if (!Venues.Select(v => v.CinemaId).Order().SequenceEqual(venues.Select(o => o.CinemaId).Order()))
        {
            throw new InvalidOperationException(
                $"The Space fixtures cover the venues {string.Join(", ", Venues.Select(v => v.CinemaId))}, "
                + $"the connectors need {string.Join(", ", venues.Select(o => o.CinemaId))}.");
        }

        Dictionary<string, FixtureResponse> responses = new(StringComparer.Ordinal);
        foreach (TheSpaceOptions options in venues)
        {
            TheSpaceFixtureVenue venue = Venues.Single(v => v.CinemaId == options.CinemaId);
            responses[options.AuthUrl] = venue.Session;
            foreach (FixtureDay day in venue.Days)
            {
                responses[options.FilmsUrl(day.Date)] = new FixtureResponse(day.Status, day.Body);
            }
        }

        return responses;
    }

    private static TheSpaceFixtureVenue ReadVenue(string directory)
    {
        string paramsPath = Path.Combine(directory, "params.json");
        if (!File.Exists(paramsPath))
        {
            throw new InvalidOperationException($"The Space fixtures folder '{directory}' has no params.json.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(paramsPath));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("cinema_id", out JsonElement id)
            || id.ValueKind != JsonValueKind.Number
            || !id.TryGetInt32(out int cinemaId))
        {
            throw new InvalidOperationException($"params.json in '{directory}' has no 'cinema_id' number.");
        }

        if (!root.TryGetProperty("auth", out JsonElement auth))
        {
            throw new InvalidOperationException($"params.json in '{directory}' has no 'auth' response.");
        }

        FixtureResponse session = FixtureEntries.ReadResponse(auth, directory, "The 'auth' response");
        return new TheSpaceFixtureVenue(cinemaId, session, FixtureEntries.ReadDays(root, directory));
    }
}

/// <summary>The saved responses of one venue.</summary>
internal sealed record TheSpaceFixtureVenue(int CinemaId, FixtureResponse Session, IReadOnlyList<FixtureDay> Days);
