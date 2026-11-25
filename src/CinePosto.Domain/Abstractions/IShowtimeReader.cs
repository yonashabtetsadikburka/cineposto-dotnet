using CinePosto.Domain.Models;

namespace CinePosto.Domain.Abstractions;

/// <summary>Reads the stored catalog of cinemas, films and showings.</summary>
public interface IShowtimeReader
{
    /// <summary>Returns all stored cinemas ordered by name.</summary>
    Task<IReadOnlyList<Cinema>> GetCinemasAsync(CancellationToken cancellationToken);

    /// <summary>Returns the films that have showings, optionally only on the given day, ordered by title.</summary>
    Task<IReadOnlyList<Film>> GetFilmsAsync(DateOnly? date, CancellationToken cancellationToken);

    /// <summary>Returns the showings matching every filter that is set, ordered by date and cinema.</summary>
    Task<IReadOnlyList<Showing>> GetShowingsAsync(DateOnly? date, string? cinemaSlug, int? filmId, CancellationToken cancellationToken);

    /// <summary>Returns the row counts of the stored catalog.</summary>
    Task<DatasetInfo> GetDatasetInfoAsync(CancellationToken cancellationToken);
}
