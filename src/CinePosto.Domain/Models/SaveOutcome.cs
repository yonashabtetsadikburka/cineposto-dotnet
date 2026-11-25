namespace CinePosto.Domain.Models;

/// <summary>What happened when a scrape result was stored.</summary>
public sealed record SaveOutcome
{
    /// <summary>Number of films written or updated.</summary>
    public required int Films { get; init; }

    /// <summary>Number of showings written for the cinema.</summary>
    public required int Showings { get; init; }

    /// <summary>
    /// <see langword="false"/> when the scrape failed without any film and the previously stored
    /// showings of the cinema were kept, so the last known data keeps being served.
    /// </summary>
    public required bool ShowingsReplaced { get; init; }
}
