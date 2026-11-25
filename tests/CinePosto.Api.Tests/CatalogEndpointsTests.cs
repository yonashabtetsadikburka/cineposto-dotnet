using CinePosto.Api;
using CinePosto.Api.Endpoints;
using CinePosto.Domain.Abstractions;
using CinePosto.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CinePosto.Api.Tests;

public sealed class CatalogEndpointsTests
{
    private const string Token = "0123456789abcdef";

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("", true, null)]
    [InlineData("2026-09-22", true, "2026-09-22")]
    [InlineData("22/09/2026", false, null)]
    [InlineData("2026-13-40", false, null)]
    [InlineData("tomorrow", false, null)]
    public void TryParseDate_AcceptsOnlyIsoDates(string? value, bool expectedValid, string? expectedDate)
    {
        bool valid = CatalogEndpoints.TryParseDate(value, out DateOnly? date);

        Assert.Equal(expectedValid, valid);
        Assert.Equal(expectedDate, date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task GetFilmsAsync_WithAnInvalidDate_Returns400WithACodeOnly()
    {
        IResult result = await CatalogEndpoints.GetFilmsAsync(new FakeReader(), "not-a-date", CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        ErrorBody body = Assert.IsType<ErrorBody>(((JsonHttpResult<ErrorBody>)result).Value);
        Assert.Equal("invalid_date", body.Code);
    }

    [Fact]
    public async Task GetShowingsAsync_WithAnInvalidDate_Returns400()
    {
        IResult result = await CatalogEndpoints.GetShowingsAsync(new FakeReader(), "x", null, null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task GetShowingsAsync_FormatsTimesAndDatesForTheClient()
    {
        FakeReader reader = new()
        {
            Showings =
            [
                new Showing
                {
                    FilmId = 7,
                    CinemaSlug = "clarici-foligno",
                    Date = new DateOnly(2026, 9, 22),
                    Times = [new TimeOnly(18, 30), new TimeOnly(20, 0)],
                    Language = "ITA",
                },
            ],
        };

        IResult result = await CatalogEndpoints.GetShowingsAsync(reader, null, null, null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        ShowingDto dto = Assert.Single(((Ok<IEnumerable<ShowingDto>>)result).Value!);
        Assert.Equal("2026-09-22", dto.Date);
        Assert.Equal(["18:30", "20:00"], dto.Times);
        Assert.Equal(7, dto.FilmId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-token-wrong-token")]
    public async Task GetDatasetInfoAsync_WithoutTheRightToken_Returns401(string? header)
    {
        DefaultHttpContext context = new();
        if (header is not null)
        {
            context.Request.Headers[AdminOptions.HeaderName] = header;
        }

        IResult result = await CatalogEndpoints.GetDatasetInfoAsync(new FakeReader(), new AdminOptions(Token), context.Request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusOf(result));
    }

    [Fact]
    public async Task GetDatasetInfoAsync_WithTheRightToken_ReturnsTheCounts()
    {
        DefaultHttpContext context = new();
        context.Request.Headers[AdminOptions.HeaderName] = Token;
        FakeReader reader = new() { Info = new DatasetInfo { Cinemas = 1, Films = 2, Showings = 3 } };

        IResult result = await CatalogEndpoints.GetDatasetInfoAsync(reader, new AdminOptions(Token), context.Request, CancellationToken.None);

        DatasetInfoDto dto = Assert.IsType<DatasetInfoDto>(((Ok<DatasetInfoDto>)result).Value);
        Assert.Equal(new DatasetInfoDto(1, 2, 3), dto);
    }

    private static int StatusOf(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? StatusCodes.Status200OK;

    private sealed class FakeReader : IShowtimeReader
    {
        public IReadOnlyList<Showing> Showings { get; init; } = [];

        public DatasetInfo Info { get; init; } = new() { Cinemas = 0, Films = 0, Showings = 0 };

        public Task<IReadOnlyList<Cinema>> GetCinemasAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Cinema>>([]);

        public Task<IReadOnlyList<Film>> GetFilmsAsync(DateOnly? date, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Film>>([]);

        public Task<IReadOnlyList<Showing>> GetShowingsAsync(DateOnly? date, string? cinemaSlug, int? filmId, CancellationToken cancellationToken)
            => Task.FromResult(Showings);

        public Task<DatasetInfo> GetDatasetInfoAsync(CancellationToken cancellationToken) => Task.FromResult(Info);
    }
}
