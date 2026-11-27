using System.Text.Json;
using CinePosto.Domain.Models;
using CinePosto.Infrastructure.Catalog;

namespace CinePosto.Infrastructure.Tests;

/// <summary>
/// The catalog is compared with <c>Parity/legacy_cinemas.json</c>, which is produced by reading the legacy
/// configuration (CINEMA_LOCATIONS) with a private script, and for the corrected cinemas with
/// <c>Parity/legacy_map_locations.json</c>, which the same script reads from the legacy web map.
/// </summary>
public sealed class CinemaCatalogTests
{
    /// <summary>
    /// Cinemas whose address and position differ from CINEMA_LOCATIONS on purpose. In the legacy project the backend
    /// configuration and the web map disagree about UCI: the maintainer confirmed the address of the map.
    /// </summary>
    private static readonly string[] PositionCorrected = ["uci-perugia"];

    private static readonly Lazy<JsonElement> Legacy = LoadParity("legacy_cinemas.json");

    private static readonly Lazy<JsonElement> LegacyMap = LoadParity("legacy_map_locations.json");

    public static TheoryData<string> Slugs()
    {
        TheoryData<string> data = [];
        foreach (Cinema cinema in CinemaCatalog.All)
        {
            data.Add(cinema.Slug);
        }

        return data;
    }

    public static TheoryData<string> UncorrectedSlugs()
    {
        TheoryData<string> data = [];
        foreach (Cinema cinema in CinemaCatalog.All.Where(c => !PositionCorrected.Contains(c.Slug)))
        {
            data.Add(cinema.Slug);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void Cinema_NameRegionAndWebsiteMatchTheLegacyConfiguration(string slug)
    {
        Cinema cinema = CinemaCatalog.Find(slug)!;
        JsonElement legacy = Legacy.Value.GetProperty(slug);

        Assert.Equal(legacy.GetProperty("name").GetString(), cinema.Name);
        Assert.Equal(legacy.GetProperty("region").GetString(), cinema.Region);
        Assert.Equal(legacy.GetProperty("website").GetString(), cinema.Website);
    }

    [Theory]
    [MemberData(nameof(UncorrectedSlugs))]
    public void Cinema_AddressAndPositionMatchTheLegacyConfiguration(string slug)
    {
        Cinema cinema = CinemaCatalog.Find(slug)!;
        JsonElement legacy = Legacy.Value.GetProperty(slug);

        Assert.Equal(legacy.GetProperty("address").GetString(), cinema.Address);
        Assert.Equal(legacy.GetProperty("city").GetString(), cinema.City);
        Assert.Equal(legacy.GetProperty("lat").GetDouble(), cinema.Latitude);
        Assert.Equal(legacy.GetProperty("lon").GetDouble(), cinema.Longitude);
    }

    [Fact]
    public void UciPerugia_IsInVialeCentovaAsTheLegacyMapSays()
    {
        Cinema cinema = CinemaCatalog.UciPerugia;
        JsonElement map = LegacyMap.Value.GetProperty(cinema.Slug);

        // Position and postal code come from the legacy web map; the address text is the one the maintainer confirmed.
        Assert.Equal(map.GetProperty("lat").GetDouble(), cinema.Latitude);
        Assert.Equal(map.GetProperty("lon").GetDouble(), cinema.Longitude);
        Assert.Contains("06132", map.GetProperty("address").GetString());
        Assert.Contains("06132", cinema.Address);
        Assert.Equal("Viale Centova, 1/D, 06132 Perugia PG", cinema.Address);
        Assert.Equal("Perugia", cinema.City);
    }

    [Fact]
    public void UciPerugia_IsNotTheCorcianoAddressOfTheLegacyBackend()
    {
        // Guards the correction: the backend configuration of the legacy project is known to be wrong for UCI.
        JsonElement backend = Legacy.Value.GetProperty(CinemaCatalog.UciPerugia.Slug);

        Assert.NotEqual(backend.GetProperty("address").GetString(), CinemaCatalog.UciPerugia.Address);
        Assert.NotEqual(backend.GetProperty("lat").GetDouble(), CinemaCatalog.UciPerugia.Latitude);
    }

    private static Lazy<JsonElement> LoadParity(string fileName)
    {
        return new Lazy<JsonElement>(() =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Parity", fileName);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        });
    }

    [Fact]
    public void LegacyFile_ListsExactlyTheCinemasOfTheCatalog()
    {
        // A cinema added to the catalog has to be added to the private generator too, so that it is compared.
        string[] expected = [.. Legacy.Value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];
        string[] actual = [.. CinemaCatalog.All.Select(c => c.Slug).Order(StringComparer.Ordinal)];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Slugs_AreUniqueAndEveryCinemaIsFound()
    {
        Assert.Equal(CinemaCatalog.All.Count, CinemaCatalog.All.Select(c => c.Slug).Distinct(StringComparer.Ordinal).Count());
        Assert.All(CinemaCatalog.All, cinema => Assert.Same(cinema, CinemaCatalog.Find(cinema.Slug)));
    }

    [Fact]
    public void Find_ReturnsNullForAnUnknownCinema()
    {
        Assert.Null(CinemaCatalog.Find("not-a-cinema"));
        Assert.Null(CinemaCatalog.Find("UCI-PERUGIA"));
    }
}
