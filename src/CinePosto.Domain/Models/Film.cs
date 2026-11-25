namespace CinePosto.Domain.Models;

/// <summary>A film stored in the catalog and shown by one or more cinemas.</summary>
public sealed record Film
{
    /// <summary>Stable identifier of the film in the catalog.</summary>
    public required int Id { get; init; }

    /// <summary>Title as displayed by the source.</summary>
    public required string Title { get; init; }

    /// <summary>Normalized title used for matching across sources.</summary>
    public required string TitleNormalized { get; init; }

    /// <summary>Original-language title, when available.</summary>
    public string? OriginalTitle { get; init; }

    /// <summary>Poster image URL, when available.</summary>
    public string? Poster { get; init; }

    /// <summary>Backdrop image URL, when available.</summary>
    public string? Backdrop { get; init; }

    /// <summary>Synopsis, when available.</summary>
    public string? Description { get; init; }

    /// <summary>Director name, when available.</summary>
    public string? Director { get; init; }

    /// <summary>Runtime in minutes, when available.</summary>
    public int? DurationMinutes { get; init; }

    /// <summary>Genres, when available.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];
}
