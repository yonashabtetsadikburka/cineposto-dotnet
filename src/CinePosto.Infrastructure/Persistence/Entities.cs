namespace CinePosto.Infrastructure.Persistence;

/// <summary>Row of the <c>cinemas</c> table.</summary>
internal sealed class CinemaEntity
{
    public required string Slug { get; set; }

    public required string Name { get; set; }

    public required string City { get; set; }

    public required string Address { get; set; }

    public required string Region { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string? Website { get; set; }
}

/// <summary>Row of the <c>films</c> table. Genres are stored as a JSON array of strings.</summary>
internal sealed class FilmEntity
{
    public int Id { get; set; }

    public required string Title { get; set; }

    public required string TitleNormalized { get; set; }

    /// <summary>Matching key computed from the normalized title; unique across the catalog.</summary>
    public required string TitleKey { get; set; }

    public string? OriginalTitle { get; set; }

    public string? Poster { get; set; }

    public string? Backdrop { get; set; }

    /// <summary>
    /// When the backdrop was looked up in an external catalogue (UTC), also when nothing was found. Null when no lookup
    /// is stored, and for a backdrop that came from a cinema.
    /// </summary>
    public DateTime? BackdropFetchedAtUtc { get; set; }

    public string? Description { get; set; }

    public string? Director { get; set; }

    public int? DurationMinutes { get; set; }

    public string GenresJson { get; set; } = "[]";
}

/// <summary>Row of the <c>showings</c> table. Times are stored as a JSON array of <c>HH:mm</c> strings.</summary>
internal sealed class ShowingEntity
{
    public int Id { get; set; }

    public int FilmId { get; set; }

    public FilmEntity? Film { get; set; }

    public required string CinemaSlug { get; set; }

    public CinemaEntity? Cinema { get; set; }

    public DateOnly Date { get; set; }

    public string TimesJson { get; set; } = "[]";

    public string? Screen { get; set; }

    public string? Language { get; set; }

    public string? SourceUrl { get; set; }

    public string SessionAttributesJson { get; set; } = "[]";
}
