namespace CinePosto.Domain.Models;

/// <summary>A film as scraped from a cinema source.</summary>
public sealed record ScrapedFilm
{
    /// <summary>Title as displayed by the source.</summary>
    public required string Title { get; init; }

    /// <summary>Normalized title used for matching across sources.</summary>
    public required string TitleNormalized { get; init; }

    /// <summary>Showings collected for this film.</summary>
    public IReadOnlyList<ScrapedShowing> Showings { get; init; } = [];

    /// <summary>Poster image URL, when available.</summary>
    public string? Poster { get; init; }

    /// <summary>Synopsis, when available.</summary>
    public string? Description { get; init; }

    /// <summary>Genres, when available.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Runtime in minutes, when available.</summary>
    public int? DurationMinutes { get; init; }

    /// <summary>Director name, when available.</summary>
    public string? Director { get; init; }

    /// <summary>Original-language title, when available.</summary>
    public string? OriginalTitle { get; init; }

    /// <summary>Backdrop image URL, when available.</summary>
    public string? Backdrop { get; init; }
}
