namespace CinePosto.Domain.Models;

/// <summary>A single scraped showing of a film on a given day.</summary>
public sealed record ScrapedShowing
{
    /// <summary>Slug of the cinema the showing belongs to.</summary>
    public required string CinemaSlug { get; init; }

    /// <summary>Calendar day of the showing.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Start times of the showing.</summary>
    public required IReadOnlyList<TimeOnly> Times { get; init; }

    /// <summary>Auditorium or screen name, when known.</summary>
    public string? Screen { get; init; }

    /// <summary>Spoken or subtitled language, when known.</summary>
    public string? Language { get; init; }

    /// <summary>Source page the showing was scraped from, when known.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Session attributes such as 3D or original language.</summary>
    public IReadOnlyList<string> SessionAttributes { get; init; } = [];
}
