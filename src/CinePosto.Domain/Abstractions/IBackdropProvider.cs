using CinePosto.Domain.Models;

namespace CinePosto.Domain.Abstractions;

/// <summary>Finds a landscape background image for a film in an external catalogue.</summary>
public interface IBackdropProvider
{
    /// <summary>
    /// Looks a film up by title. A failure of the catalogue is reported in the result, not thrown; only cancelling the
    /// token throws.
    /// </summary>
    Task<BackdropLookup> FindAsync(string title, CancellationToken cancellationToken);
}
