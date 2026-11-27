using CinePosto.Domain.Models;
using CinePosto.Infrastructure.Catalog;
using CinePosto.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CinePosto.Infrastructure.Tests;

public sealed class BackdropStoreTests
{
    private static readonly Cinema Clarici = CinemaCatalog.Clarici;

    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private const string Image = "https://example.test/backdrop.jpg";

    [Fact]
    public async Task GetFilmsNeedingBackdrop_ListsFilmsWithShowingsAndNoStoredLookup()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await SaveAsync(db, Film("Con Orari"), Film("Senza Orari") with { Showings = [] }, Film("Dal Cinema") with { Backdrop = Image });

        Film needing = Assert.Single(await db.Store.GetFilmsNeedingBackdropAsync(CancellationToken.None));

        Assert.Equal("Con Orari", needing.Title);
    }

    [Theory]
    [InlineData(Image)]
    [InlineData(null)]
    public async Task SaveBackdrop_StoresTheResult_AlsoWhenNothingWasFound(string? url)
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await SaveAsync(db, Film("Nebbia sul Lago"));
        Film film = Assert.Single(await db.Store.GetFilmsNeedingBackdropAsync(CancellationToken.None));

        await db.Store.SaveBackdropAsync(film.Id, url, Now, CancellationToken.None);

        Assert.Equal(url, (await ReadFilmAsync(db)).Backdrop);
        Assert.Empty(await db.Store.GetFilmsNeedingBackdropAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveBackdrop_ThenANewScrape_KeepsTheStoredResult()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await SaveAsync(db, Film("Nebbia sul Lago"));
        await db.Store.SaveBackdropAsync((await ReadFilmAsync(db)).Id, Image, Now, CancellationToken.None);

        await SaveAsync(db, Film("Nebbia sul Lago"));

        Assert.Equal(Image, (await ReadFilmAsync(db)).Backdrop);
        Assert.Empty(await db.Store.GetFilmsNeedingBackdropAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveBackdrop_NeverReplacesABackdropFromACinema()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await SaveAsync(db, Film("Dal Cinema") with { Backdrop = "https://example.test/from-cinema.jpg" });
        int id = (await ReadFilmAsync(db)).Id;

        await db.Store.SaveBackdropAsync(id, Image, Now, CancellationToken.None);
        await db.Store.ExpireBackdropsAsync(Now.AddYears(1), CancellationToken.None);

        Assert.Equal("https://example.test/from-cinema.jpg", (await ReadFilmAsync(db)).Backdrop);
    }

    [Fact]
    public async Task ACinemaBackdrop_ReplacesALookedUpOne_AndDoesNotExpire()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await SaveAsync(db, Film("Nebbia sul Lago"));
        await db.Store.SaveBackdropAsync((await ReadFilmAsync(db)).Id, Image, Now, CancellationToken.None);

        await SaveAsync(db, Film("Nebbia sul Lago") with { Backdrop = "https://example.test/from-cinema.jpg" });
        int expired = await db.Store.ExpireBackdropsAsync(Now.AddYears(1), CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal("https://example.test/from-cinema.jpg", (await ReadFilmAsync(db)).Backdrop);
    }

    [Theory]
    [InlineData(Image)]
    [InlineData(null)]
    public async Task ExpireBackdrops_DeletesOnlyTheResultsOlderThanTheLimit(string? url)
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await SaveAsync(db, Film("Nebbia sul Lago"));
        await db.Store.SaveBackdropAsync((await ReadFilmAsync(db)).Id, url, Now, CancellationToken.None);

        Assert.Equal(0, await db.Store.ExpireBackdropsAsync(Now, CancellationToken.None));
        Assert.Equal(url, (await ReadFilmAsync(db)).Backdrop);

        Assert.Equal(1, await db.Store.ExpireBackdropsAsync(Now.AddSeconds(1), CancellationToken.None));
        Assert.Null((await ReadFilmAsync(db)).Backdrop);
        Assert.Single(await db.Store.GetFilmsNeedingBackdropAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExpireBackdrops_AlsoDeletesTheResultOfAFilmThatIsNoLongerShown()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await SaveAsync(db, Film("Nebbia sul Lago"));
        await db.Store.SaveBackdropAsync((await ReadFilmAsync(db)).Id, Image, Now, CancellationToken.None);
        await db.Store.SaveAsync(Clarici, new ScrapeResult(), CancellationToken.None);

        Assert.Equal(1, await db.Store.ExpireBackdropsAsync(Now.AddDays(1), CancellationToken.None));
    }

    [Fact]
    public async Task SaveBackdrop_ForAnUnknownFilm_Throws()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Store.SaveBackdropAsync(999, Image, Now, CancellationToken.None));
    }

    [Fact]
    public async Task Migrate_UpgradesADatabaseOfTheFirstSchema_AndKeepsItsFilms()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(directory, "old.db");
            await using (CinePostoDbContext old = CinePostoDatabase.CreateContext(path))
            {
                Directory.CreateDirectory(directory);
                await old.GetService<IMigrator>().MigrateAsync("InitialCreate");
                await old.Database.ExecuteSqlRawAsync(
                    "INSERT INTO films (Title, TitleNormalized, TitleKey, GenresJson) VALUES ('Vecchio', 'Vecchio', 'vecchio', '[]')");
            }

            await using CinePostoDbContext upgraded = CinePostoDatabase.CreateContext(path);
            await DatabaseInitializer.MigrateAsync(upgraded, CancellationToken.None);

            FilmEntity film = await upgraded.Films.AsNoTracking().SingleAsync();
            Assert.Equal("Vecchio", film.Title);
            Assert.Null(film.Backdrop);
            Assert.Null(film.BackdropFetchedAtUtc);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static Task<SaveOutcome> SaveAsync(TestDatabase db, params ScrapedFilm[] films) =>
        db.Store.SaveAsync(Clarici, new ScrapeResult { Films = films }, CancellationToken.None);

    private static async Task<Film> ReadFilmAsync(TestDatabase db) =>
        (await db.Store.GetFilmsAsync(null, CancellationToken.None)) is [Film film]
            ? film
            : await ReadWithoutShowingsAsync(db);

    // A film without showings is not listed by the reader, so it is read from its table.
    private static async Task<Film> ReadWithoutShowingsAsync(TestDatabase db)
    {
        FilmEntity row = await db.Context.Films.AsNoTracking().SingleAsync();
        return new Film { Id = row.Id, Title = row.Title, TitleNormalized = row.TitleNormalized, Backdrop = row.Backdrop };
    }

    private static ScrapedFilm Film(string title) => new()
    {
        Title = title,
        TitleNormalized = title,
        Showings = [new ScrapedShowing { CinemaSlug = Clarici.Slug, Date = new DateOnly(2026, 11, 27), Times = [new TimeOnly(18, 0)] }],
    };
}
