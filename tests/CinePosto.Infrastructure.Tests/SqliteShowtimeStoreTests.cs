using System.Globalization;
using CinePosto.Domain.Models;
using CinePosto.Infrastructure.Catalog;
using CinePosto.Infrastructure.Persistence;

namespace CinePosto.Infrastructure.Tests;

public sealed class SqliteShowtimeStoreTests
{
    private static readonly Cinema Clarici = CinemaCatalog.Clarici;

    private static readonly Cinema Other = new()
    {
        Slug = "other-cinema",
        Name = "Other Cinema",
        City = "Perugia",
        Address = "Via Esempio 1",
        Latitude = 43.1,
        Longitude = 12.4,
    };

    private static readonly DateOnly Day = new(2026, 9, 22);

    [Fact]
    public async Task Migrate_CreatesAnEmptySchemaOnANewFile()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();

        DatasetInfo info = await db.Store.GetDatasetInfoAsync(CancellationToken.None);

        Assert.Equal(0, info.Cinemas);
        Assert.Equal(0, info.Films);
        Assert.Equal(0, info.Showings);
    }

    [Fact]
    public async Task Migrate_CanRunTwiceOnTheSameFile()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();

        await DatabaseInitializer.MigrateAsync(db.Context, CancellationToken.None);

        Assert.Equal(0, (await db.Store.GetDatasetInfoAsync(CancellationToken.None)).Films);
    }

    [Fact]
    public async Task SaveAsync_StoresTheCatalogAndReadsItBack()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();

        SaveOutcome outcome = await db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["20:00", "18:30"])), CancellationToken.None);

        Assert.Equal(1, outcome.Films);
        Assert.Equal(1, outcome.Showings);
        Assert.True(outcome.ShowingsReplaced);

        Cinema cinema = Assert.Single(await db.Store.GetCinemasAsync(CancellationToken.None));
        Assert.Equal("clarici-foligno", cinema.Slug);
        Assert.Equal("Foligno", cinema.City);

        Film film = Assert.Single(await db.Store.GetFilmsAsync(null, CancellationToken.None));
        Assert.Equal("Nebbia sul Lago", film.Title);
        Assert.Equal(100, film.DurationMinutes);
        Assert.Equal(["Fantasia"], film.Genres);

        Showing showing = Assert.Single(await db.Store.GetShowingsAsync(null, null, null, CancellationToken.None));
        Assert.Equal(film.Id, showing.FilmId);
        Assert.Equal(Day, showing.Date);
        Assert.Equal([new TimeOnly(18, 30), new TimeOnly(20, 0)], showing.Times);
        Assert.Equal("ITA", showing.Language);
    }

    [Fact]
    public async Task SaveAsync_SameResultTwice_LeavesTheCatalogUnchanged()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        ScrapeResult result = ResultOf(MakeFilm("Nebbia sul Lago", ["20:00"]));

        await db.Store.SaveAsync(Clarici, result, CancellationToken.None);
        await db.Store.SaveAsync(Clarici, result, CancellationToken.None);

        DatasetInfo info = await db.Store.GetDatasetInfoAsync(CancellationToken.None);
        Assert.Equal(1, info.Cinemas);
        Assert.Equal(1, info.Films);
        Assert.Equal(1, info.Showings);
    }

    [Fact]
    public async Task SaveAsync_ReplacesShowingsOfTheCinema()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["20:00"])), CancellationToken.None);

        await db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["22:15"])), CancellationToken.None);

        Showing showing = Assert.Single(await db.Store.GetShowingsAsync(null, null, null, CancellationToken.None));
        Assert.Equal([new TimeOnly(22, 15)], showing.Times);
    }

    [Fact]
    public async Task SaveAsync_ErrorsWithoutFilms_KeepsTheLastKnownShowings()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["20:00"])), CancellationToken.None);
        ScrapeResult failed = new()
        {
            Errors =
            [
                new ConnectorError { CinemaSlug = Clarici.Slug, Timestamp = DateTimeOffset.UtcNow, ExceptionType = "HttpRequestException", Phase = "scrape" },
            ],
        };

        SaveOutcome outcome = await db.Store.SaveAsync(Clarici, failed, CancellationToken.None);

        Assert.False(outcome.ShowingsReplaced);
        Assert.Equal(1, outcome.Showings);
        Assert.Single(await db.Store.GetShowingsAsync(null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_EmptyResultWithoutErrors_ClearsTheShowings()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["20:00"])), CancellationToken.None);

        SaveOutcome outcome = await db.Store.SaveAsync(Clarici, new ScrapeResult(), CancellationToken.None);

        Assert.True(outcome.ShowingsReplaced);
        Assert.Empty(await db.Store.GetShowingsAsync(null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_DoesNotBlankOutExistingFilmFields()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["20:00"])), CancellationToken.None);
        ScrapedFilm sparse = new()
        {
            Title = "Nebbia sul Lago",
            TitleNormalized = "Nebbia sul Lago",
            Showings = [MakeShowing(Clarici.Slug, ["21:00"])],
        };

        await db.Store.SaveAsync(Clarici, ResultOf(sparse), CancellationToken.None);

        Film film = Assert.Single(await db.Store.GetFilmsAsync(null, CancellationToken.None));
        Assert.Equal("https://example.test/p.jpg", film.Poster);
        Assert.Equal(100, film.DurationMinutes);
        Assert.Equal(["Fantasia"], film.Genres);
    }

    [Fact]
    public async Task SaveAsync_SameFilmInTwoCinemas_SharesOneFilmRow()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();

        await db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["20:00"])), CancellationToken.None);
        await db.Store.SaveAsync(Other, ResultOf(MakeFilm("NEBBIA SUL LAGO", ["21:00"], Other.Slug)), CancellationToken.None);

        DatasetInfo info = await db.Store.GetDatasetInfoAsync(CancellationToken.None);
        Assert.Equal(2, info.Cinemas);
        Assert.Equal(1, info.Films);
        Assert.Equal(2, info.Showings);
    }

    [Fact]
    public async Task SaveAsync_ShowingOfAnotherCinema_Throws()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["20:00"], Other.Slug)), CancellationToken.None));

        Assert.Equal(0, (await db.Store.GetDatasetInfoAsync(CancellationToken.None)).Films);
    }

    [Fact]
    public async Task GetFilmsAsync_FiltersByDate()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["20:00"])), CancellationToken.None);

        Assert.Single(await db.Store.GetFilmsAsync(Day, CancellationToken.None));
        Assert.Empty(await db.Store.GetFilmsAsync(Day.AddDays(1), CancellationToken.None));
    }

    [Fact]
    public async Task GetShowingsAsync_FiltersByDateCinemaAndFilm()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await db.Store.SaveAsync(Clarici, ResultOf(MakeFilm("Nebbia sul Lago", ["20:00"])), CancellationToken.None);
        await db.Store.SaveAsync(Other, ResultOf(MakeFilm("Festa Sintetica", ["21:00"], Other.Slug)), CancellationToken.None);
        int festaId = (await db.Store.GetFilmsAsync(null, CancellationToken.None)).Single(f => f.Title == "Festa Sintetica").Id;

        Assert.Equal(2, (await db.Store.GetShowingsAsync(Day, null, null, CancellationToken.None)).Count);
        Assert.Empty(await db.Store.GetShowingsAsync(Day.AddDays(1), null, null, CancellationToken.None));
        Assert.Equal(Other.Slug, Assert.Single(await db.Store.GetShowingsAsync(null, Other.Slug, null, CancellationToken.None)).CinemaSlug);
        Assert.Equal(festaId, Assert.Single(await db.Store.GetShowingsAsync(null, null, festaId, CancellationToken.None)).FilmId);
    }

    private static ScrapeResult ResultOf(params ScrapedFilm[] films) => new() { Films = films };

    private static ScrapedFilm MakeFilm(string title, string[] times, string? cinemaSlug = null)
    {
        return new ScrapedFilm
        {
            Title = title,
            TitleNormalized = title,
            Poster = "https://example.test/p.jpg",
            Genres = ["Fantasia"],
            DurationMinutes = 100,
            Showings = [MakeShowing(cinemaSlug ?? Clarici.Slug, times)],
        };
    }

    private static ScrapedShowing MakeShowing(string cinemaSlug, string[] times)
    {
        return new ScrapedShowing
        {
            CinemaSlug = cinemaSlug,
            Date = Day,
            Times = times.Select(t => TimeOnly.Parse(t, CultureInfo.InvariantCulture)).ToList(),
            Language = "ITA",
            SourceUrl = "https://example.test/",
        };
    }
}
