using System.Globalization;
using System.Text.Json;
using CinePosto.Domain.Abstractions;
using CinePosto.Domain.Models;
using CinePosto.Domain.Showings;
using CinePosto.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace CinePosto.Infrastructure.Persistence;

/// <summary>Stores and reads the catalog in the SQLite database.</summary>
public sealed class SqliteShowtimeStore : IScrapeResultWriter, IShowtimeReader
{
    private const string TimeFormat = "HH:mm";

    private readonly CinePostoDbContext _db;

    /// <summary>Creates a store over the given context.</summary>
    public SqliteShowtimeStore(CinePostoDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<SaveOutcome> SaveAsync(Cinema cinema, ScrapeResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cinema);
        ArgumentNullException.ThrowIfNull(result);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await UpsertCinemaAsync(cinema, cancellationToken).ConfigureAwait(false);

        bool failedWithoutFilms = result.Errors.Count > 0 && result.Films.Count == 0;
        if (failedWithoutFilms)
        {
            // Keep serving the last known showings of this cinema (FR-12).
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            int kept = await _db.Showings.CountAsync(s => s.CinemaSlug == cinema.Slug, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new SaveOutcome { Films = 0, Showings = kept, ShowingsReplaced = false };
        }

        await _db.Showings.Where(s => s.CinemaSlug == cinema.Slug).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        // Two scraped films can be the same film row, so their showings are gathered per row before being merged.
        Dictionary<string, FilmEntity> films = new(StringComparer.Ordinal);
        Dictionary<FilmEntity, List<ScrapedShowing>> showingsByFilm = [];
        List<FilmEntity> filmOrder = [];
        foreach (ScrapedFilm scraped in result.Films)
        {
            FilmEntity film = await UpsertFilmAsync(scraped, films, cancellationToken).ConfigureAwait(false);
            if (!showingsByFilm.TryGetValue(film, out List<ScrapedShowing>? gathered))
            {
                gathered = [];
                showingsByFilm[film] = gathered;
                filmOrder.Add(film);
            }

            foreach (ScrapedShowing showing in scraped.Showings)
            {
                if (!string.Equals(showing.CinemaSlug, cinema.Slug, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Showing of cinema '{showing.CinemaSlug}' cannot be stored for cinema '{cinema.Slug}'.");
                }

                gathered.Add(showing);
            }
        }

        int showingCount = 0;
        foreach (FilmEntity film in filmOrder)
        {
            // The legacy output step: one row per day, language, screen and attributes, with all its times.
            foreach (ScrapedShowing showing in ShowingConsolidator.Consolidate(showingsByFilm[film]))
            {
                _db.Showings.Add(new ShowingEntity
                {
                    Film = film,
                    CinemaSlug = cinema.Slug,
                    Date = showing.Date,
                    TimesJson = JsonSerializer.Serialize(
                        showing.Times.Select(t => t.ToString(TimeFormat, CultureInfo.InvariantCulture))),
                    Screen = showing.Screen,
                    Language = showing.Language,
                    SourceUrl = showing.SourceUrl,
                    SessionAttributesJson = JsonSerializer.Serialize(showing.SessionAttributes),
                });
                showingCount++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new SaveOutcome { Films = films.Count, Showings = showingCount, ShowingsReplaced = true };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Cinema>> GetCinemasAsync(CancellationToken cancellationToken)
    {
        List<CinemaEntity> rows = await _db.Cinemas.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(c => new Cinema
        {
            Slug = c.Slug,
            Name = c.Name,
            City = c.City,
            Address = c.Address,
            Region = c.Region,
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            Website = c.Website,
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Film>> GetFilmsAsync(DateOnly? date, CancellationToken cancellationToken)
    {
        IQueryable<FilmEntity> query = _db.Films.AsNoTracking()
            .Where(f => _db.Showings.Any(s => s.FilmId == f.Id && (date == null || s.Date == date)));

        List<FilmEntity> rows = await query.OrderBy(f => f.Title).ThenBy(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(f => new Film
        {
            Id = f.Id,
            Title = f.Title,
            TitleNormalized = f.TitleNormalized,
            OriginalTitle = f.OriginalTitle,
            Poster = f.Poster,
            Backdrop = f.Backdrop,
            Description = f.Description,
            Director = f.Director,
            DurationMinutes = f.DurationMinutes,
            Genres = ReadStrings(f.GenresJson),
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Showing>> GetShowingsAsync(DateOnly? date, string? cinemaSlug, int? filmId, CancellationToken cancellationToken)
    {
        IQueryable<ShowingEntity> query = _db.Showings.AsNoTracking();
        if (date is not null)
        {
            query = query.Where(s => s.Date == date);
        }

        if (!string.IsNullOrEmpty(cinemaSlug))
        {
            query = query.Where(s => s.CinemaSlug == cinemaSlug);
        }

        if (filmId is not null)
        {
            query = query.Where(s => s.FilmId == filmId);
        }

        List<ShowingEntity> rows = await query
            .OrderBy(s => s.Date).ThenBy(s => s.CinemaSlug).ThenBy(s => s.FilmId).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.Select(s => new Showing
        {
            FilmId = s.FilmId,
            CinemaSlug = s.CinemaSlug,
            Date = s.Date,
            Times = ReadStrings(s.TimesJson)
                .Select(t => TimeOnly.ParseExact(t, TimeFormat, CultureInfo.InvariantCulture))
                .ToList(),
            Screen = s.Screen,
            Language = s.Language,
            SourceUrl = s.SourceUrl,
            SessionAttributes = ReadStrings(s.SessionAttributesJson),
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<DatasetInfo> GetDatasetInfoAsync(CancellationToken cancellationToken)
    {
        return new DatasetInfo
        {
            Cinemas = await _db.Cinemas.CountAsync(cancellationToken).ConfigureAwait(false),
            Films = await _db.Films.CountAsync(cancellationToken).ConfigureAwait(false),
            Showings = await _db.Showings.CountAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    private static List<string> ReadStrings(string json)
    {
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private async Task UpsertCinemaAsync(Cinema cinema, CancellationToken cancellationToken)
    {
        CinemaEntity? entity = await _db.Cinemas.FindAsync([cinema.Slug], cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            _db.Cinemas.Add(new CinemaEntity
            {
                Slug = cinema.Slug,
                Name = cinema.Name,
                City = cinema.City,
                Address = cinema.Address,
                Region = cinema.Region,
                Latitude = cinema.Latitude,
                Longitude = cinema.Longitude,
                Website = cinema.Website,
            });
            return;
        }

        entity.Name = cinema.Name;
        entity.City = cinema.City;
        entity.Address = cinema.Address;
        entity.Region = cinema.Region;
        entity.Latitude = cinema.Latitude;
        entity.Longitude = cinema.Longitude;
        entity.Website = cinema.Website ?? entity.Website;
    }

    private async Task<FilmEntity> UpsertFilmAsync(ScrapedFilm scraped, Dictionary<string, FilmEntity> seen, CancellationToken cancellationToken)
    {
        string key = TitleNormalizer.Key(scraped.TitleNormalized);
        if (!seen.TryGetValue(key, out FilmEntity? entity))
        {
            entity = await _db.Films.FirstOrDefaultAsync(f => f.TitleKey == key, cancellationToken).ConfigureAwait(false);
            if (entity is null)
            {
                entity = new FilmEntity { Title = scraped.Title, TitleNormalized = scraped.TitleNormalized, TitleKey = key };
                _db.Films.Add(entity);
            }

            seen[key] = entity;
        }

        // Newer scrapes refine the stored values but never blank them out.
        entity.Title = scraped.Title;
        entity.TitleNormalized = scraped.TitleNormalized;
        entity.OriginalTitle = scraped.OriginalTitle ?? entity.OriginalTitle;
        entity.Poster = scraped.Poster ?? entity.Poster;
        entity.Backdrop = scraped.Backdrop ?? entity.Backdrop;
        entity.Description = scraped.Description ?? entity.Description;
        entity.Director = scraped.Director ?? entity.Director;
        entity.DurationMinutes = scraped.DurationMinutes ?? entity.DurationMinutes;
        if (scraped.Genres.Count > 0)
        {
            entity.GenresJson = JsonSerializer.Serialize(scraped.Genres);
        }

        return entity;
    }
}
