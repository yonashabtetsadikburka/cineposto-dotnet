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

    /// <summary>
    /// UCI Cinemas Perugia, in Viale Centova. This differs on purpose from the legacy backend configuration, which has
    /// Via Corcianese in Corciano: the maintainer confirmed Viale Centova, which is also what the legacy web map used.
    /// The position is the one of that map. See legacy-parity-notes.md.
    /// </summary>
    public static Cinema UciPerugia { get; } = new()
    {
        Slug = "uci-perugia",
        Name = "UCI Cinemas Perugia",
        City = "Perugia",
        Address = "Viale Centova, 1/D, 06132 Perugia PG",
        Latitude = 43.0963682,
        Longitude = 12.3553159,
        Website = "https://ucicinemas.it",
    };

    /// <summary>
    /// The Space Cinema in Corciano, as the legacy backend configuration has it. The legacy web map gives another
    /// address and position (Via Pierluigi Nervi): which one is right has not been confirmed.
    /// </summary>
    public static Cinema TheSpaceCorciano { get; } = new()
    {
        Slug = "the-space-corciano",
        Name = "The Space Cinema Corciano",
        City = "Corciano",
        Address = "Via Trasimeno Ovest 164, 06073 Corciano PG",
        Latitude = 43.0866,
        Longitude = 12.3073,
        Website = "https://www.thespacecinema.it",
    };

    /// <summary>The Space Cinema in Terni.</summary>
    public static Cinema TheSpaceTerni { get; } = new()
    {
        Slug = "the-space-terni",
        Name = "The Space Cinema Terni",
        City = "Terni",
        Address = "Viale Donato Bramante, Snc, 05100 Terni TR",
        Latitude = 42.57265,
        Longitude = 12.63553,
        Website = "https://www.thespacecinema.it/al-cinema/terni",
    };

    /// <summary>All cinemas that have a connector.</summary>
    public static IReadOnlyList<Cinema> All { get; } = [Clarici, UciPerugia, TheSpaceCorciano, TheSpaceTerni];

    /// <summary>Finds a cinema by slug, or returns <see langword="null"/> when no connector exists for it.</summary>
    public static Cinema? Find(string slug)
    {
        return All.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.Ordinal));
    }
}
