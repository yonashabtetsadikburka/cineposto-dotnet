using System.Globalization;
using CinePosto.Domain.Models;

namespace CinePosto.Api.Endpoints;

/// <summary>Body of every error response. It carries a machine-readable code, never user-facing text.</summary>
internal sealed record ErrorBody(string Code);

internal sealed record CinemaDto(
    string Slug,
    string Name,
    string City,
    string Address,
    string Region,
    double Latitude,
    double Longitude,
    string? Website)
{
    public static CinemaDto From(Cinema cinema) =>
        new(cinema.Slug, cinema.Name, cinema.City, cinema.Address, cinema.Region, cinema.Latitude, cinema.Longitude, cinema.Website);
}

internal sealed record FilmDto(
    int Id,
    string Title,
    string TitleNormalized,
    string? OriginalTitle,
    string? Poster,
    string? Backdrop,
    string? Description,
    string? Director,
    int? DurationMinutes,
    IReadOnlyList<string> Genres)
{
    public static FilmDto From(Film film) =>
        new(film.Id, film.Title, film.TitleNormalized, film.OriginalTitle, film.Poster, film.Backdrop, film.Description, film.Director, film.DurationMinutes, film.Genres);
}

internal sealed record ShowingDto(
    int FilmId,
    string CinemaSlug,
    string Date,
    IReadOnlyList<string> Times,
    string? Screen,
    string? Language,
    string? SourceUrl,
    IReadOnlyList<string> SessionAttributes)
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm";

    public static ShowingDto From(Showing showing) =>
        new(
            showing.FilmId,
            showing.CinemaSlug,
            showing.Date.ToString(DateFormat, CultureInfo.InvariantCulture),
            showing.Times.Select(t => t.ToString(TimeFormat, CultureInfo.InvariantCulture)).ToList(),
            showing.Screen,
            showing.Language,
            showing.SourceUrl,
            showing.SessionAttributes);
}

internal sealed record DatasetInfoDto(int Cinemas, int Films, int Showings)
{
    public static DatasetInfoDto From(DatasetInfo info) => new(info.Cinemas, info.Films, info.Showings);
}
