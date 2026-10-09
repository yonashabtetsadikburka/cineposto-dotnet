namespace CinePosto.Domain.Models;

/// <summary>The outcome of a scraping run.</summary>
public sealed record ScrapeResult
{
    /// <summary>Films collected during the run.</summary>
    public IReadOnlyList<ScrapedFilm> Films { get; init; } = [];

    /// <summary>Failures recorded during the run.</summary>
    public IReadOnlyList<ConnectorError> Errors { get; init; } = [];
}
