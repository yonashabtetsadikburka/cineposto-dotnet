using System.Globalization;
using CinePosto.Domain.Abstractions;
using CinePosto.Domain.Models;

namespace CinePosto.Api.Endpoints;

/// <summary>Read-only catalog endpoints and the token-protected admin endpoint.</summary>
internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder api = app.MapGroup("/api/v1");
        api.MapGet("/cinemas", GetCinemasAsync);
        api.MapGet("/films", GetFilmsAsync);
        api.MapGet("/showings", GetShowingsAsync);
        api.MapGet("/admin/dataset-info", GetDatasetInfoAsync);
        return app;
    }

    internal static async Task<IResult> GetCinemasAsync(IShowtimeReader reader, CancellationToken cancellationToken)
    {
        IReadOnlyList<Cinema> cinemas = await reader.GetCinemasAsync(cancellationToken);
        return Results.Ok(cinemas.Select(CinemaDto.From));
    }

    internal static async Task<IResult> GetFilmsAsync(IShowtimeReader reader, string? date, CancellationToken cancellationToken)
    {
        if (!TryParseDate(date, out DateOnly? day))
        {
            return InvalidDate();
        }

        IReadOnlyList<Film> films = await reader.GetFilmsAsync(day, cancellationToken);
        return Results.Ok(films.Select(FilmDto.From));
    }

    internal static async Task<IResult> GetShowingsAsync(
        IShowtimeReader reader,
        string? date,
        string? cinema,
        int? filmId,
        CancellationToken cancellationToken)
    {
        if (!TryParseDate(date, out DateOnly? day))
        {
            return InvalidDate();
        }

        IReadOnlyList<Showing> showings = await reader.GetShowingsAsync(day, cinema, filmId, cancellationToken);
        return Results.Ok(showings.Select(ShowingDto.From));
    }

    internal static async Task<IResult> GetDatasetInfoAsync(
        IShowtimeReader reader,
        AdminOptions admin,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!admin.Matches(request.Headers[AdminOptions.HeaderName].FirstOrDefault()))
        {
            return Results.Json(new ErrorBody("unauthorized"), statusCode: StatusCodes.Status401Unauthorized);
        }

        DatasetInfo info = await reader.GetDatasetInfoAsync(cancellationToken);
        return Results.Ok(DatasetInfoDto.From(info));
    }

    /// <summary>Parses an optional <c>yyyy-MM-dd</c> date. A missing value is valid and yields <see langword="null"/>.</summary>
    internal static bool TryParseDate(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (DateOnly.TryParseExact(value, ShowingDto.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            date = parsed;
            return true;
        }

        return false;
    }

    private static IResult InvalidDate() => Results.Json(new ErrorBody("invalid_date"), statusCode: StatusCodes.Status400BadRequest);
}
