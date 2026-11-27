using CinePosto.Domain.Models;

namespace CinePosto.Domain.Abstractions;

/// <summary>
/// Keeps the results of backdrop lookups with the films, each with the moment it was made, so that a title is asked
/// once and an old result can be thrown away.
/// </summary>
public interface IBackdropStore
{
    /// <summary>Deletes the results of the lookups made before the given moment. Returns how many were deleted.</summary>
    Task<int> ExpireBackdropsAsync(DateTimeOffset madeBefore, CancellationToken cancellationToken);

    /// <summary>
    /// The films that have showings, no backdrop and no stored lookup result. A film whose backdrop came from a cinema
    /// is never in the list.
    /// </summary>
    Task<IReadOnlyList<Film>> GetFilmsNeedingBackdropAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores the result of a lookup: an address, or <see langword="null"/> when the film has no backdrop. A backdrop
    /// that came from a cinema is not replaced.
    /// </summary>
    Task SaveBackdropAsync(int filmId, string? backdropUrl, DateTimeOffset madeAt, CancellationToken cancellationToken);
}
