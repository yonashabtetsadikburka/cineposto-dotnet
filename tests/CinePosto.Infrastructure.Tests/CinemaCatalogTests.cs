using System.Text.Json;
using CinePosto.Domain.Models;
using CinePosto.Infrastructure.Catalog;

namespace CinePosto.Infrastructure.Tests;

/// <summary>
/// The catalog is compared with <c>Parity/legacy_cinemas.json</c>, which is produced by reading the legacy
/// configuration (CINEMA_LOCATIONS) with a private script. Nothing is typed by hand here.
/// </summary>
public sealed class CinemaCatalogTests
{
    private static readonly Lazy<JsonElement> Legacy = new(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Parity", "legacy_cinemas.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    });

    public static TheoryData<string> Slugs()
    {
        TheoryData<string> data = [];
        foreach (Cinema cinema in CinemaCatalog.All)
        {
            data.Add(cinema.Slug);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void Cinema_MatchesTheLegacyConfiguration(string slug)
    {
        Cinema cinema = CinemaCatalog.Find(slug)!;
        JsonElement legacy = Legacy.Value.GetProperty(slug);

        Assert.Equal(legacy.GetProperty("name").GetString(), cinema.Name);
        Assert.Equal(legacy.GetProperty("address").GetString(), cinema.Address);
        Assert.Equal(legacy.GetProperty("city").GetString(), cinema.City);
        Assert.Equal(legacy.GetProperty("region").GetString(), cinema.Region);
        Assert.Equal(legacy.GetProperty("lat").GetDouble(), cinema.Latitude);
        Assert.Equal(legacy.GetProperty("lon").GetDouble(), cinema.Longitude);
        Assert.Equal(legacy.GetProperty("website").GetString(), cinema.Website);
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
