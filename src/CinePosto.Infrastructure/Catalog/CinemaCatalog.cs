using CinePosto.Domain.Models;

namespace CinePosto.Infrastructure.Catalog;

/// <summary>Static data of the cinemas that have a connector. Values come from the legacy configuration.</summary>
public static class CinemaCatalog
{
    /// <summary>Multiplex Politeama Clarici in Foligno. Coordinates are approximate, as in the legacy source.</summary>
    public static Cinema Clarici { get; } = new()
    {
        Slug = "clarici-foligno",
        Name = "Multiplex Politeama Clarici",
        City = "Foligno",
        Address = "Via Giuseppe Garibaldi, 137, 06034 Foligno PG",
        Latitude = 42.95785,
        Longitude = 12.70780,
        Website = "https://www.cinemaclarici.it",
    };

    /// <summary>All cinemas that have a connector.</summary>
    public static IReadOnlyList<Cinema> All { get; } = [Clarici];

    /// <summary>Finds a cinema by slug, or returns <see langword="null"/> when no connector exists for it.</summary>
    public static Cinema? Find(string slug)
    {
        return All.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.Ordinal));
    }
}
