using CinePosto.Domain.Models;

namespace CinePosto.Domain.Abstractions;

/// <summary>Stores the outcome of scraping a cinema.</summary>
public interface IScrapeResultWriter
{
    /// <summary>
    /// Stores the cinema and the films and showings of a scrape result. Storing the same result twice
    /// leaves the catalog unchanged. A result with errors and no films keeps the showings already stored.
    /// </summary>
    Task<SaveOutcome> SaveAsync(Cinema cinema, ScrapeResult result, CancellationToken cancellationToken);
}
