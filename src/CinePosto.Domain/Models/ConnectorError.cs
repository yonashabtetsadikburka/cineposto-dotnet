namespace CinePosto.Domain.Models;

/// <summary>A failure recorded while scraping a cinema.</summary>
public sealed record ConnectorError
{
    /// <summary>Slug of the cinema being scraped.</summary>
    public required string CinemaSlug { get; init; }

    /// <summary>Moment the failure was recorded.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Full name of the exception type that was raised.</summary>
    public required string ExceptionType { get; init; }

    /// <summary>Scraping phase that failed, such as fetching or parsing.</summary>
    public required string Phase { get; init; }

    /// <summary>URL being processed when the failure happened, when known.</summary>
    public string? Url { get; init; }

    /// <summary>Additional failure detail, when available.</summary>
    public string? Detail { get; init; }
}
