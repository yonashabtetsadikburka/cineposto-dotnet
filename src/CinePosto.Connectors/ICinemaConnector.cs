using CinePosto.Domain.Models;

namespace CinePosto.Connectors;

/// <summary>Scrapes the schedule of a single cinema.</summary>
public interface ICinemaConnector
{
    /// <summary>Stable identifier of the cinema.</summary>
    string CinemaSlug { get; }

    /// <summary>Display name of the cinema.</summary>
    string CinemaName { get; }

    /// <summary>Scrapes showtimes for the requested dates.</summary>
    Task<ScrapeResult> ScrapeAsync(DateOnly today, IReadOnlyList<DateOnly>? dates, CancellationToken cancellationToken);
}
