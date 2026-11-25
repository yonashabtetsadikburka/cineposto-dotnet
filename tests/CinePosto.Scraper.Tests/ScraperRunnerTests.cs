using CinePosto.Connectors;
using CinePosto.Domain.Abstractions;
using CinePosto.Domain.Models;
using CinePosto.Scraper;

namespace CinePosto.Scraper.Tests;

public sealed class ScraperRunnerTests
{
    private static readonly ScrapeWindow Window = ScrapeWindow.FromToday(new DateOnly(2026, 9, 22));

    [Fact]
    public async Task RunAsync_StoresEveryCinema_AndReturnsZero()
    {
        RecordingWriter writer = new();
        StringWriter output = new();
        ScraperRunner runner = new(writer, TimeProvider.System, output);

        int exit = await runner.RunAsync(
            [Registration("one", _ => Task.FromResult(WithOneFilm())), Registration("two", _ => Task.FromResult(WithOneFilm()))],
            Window,
            ScraperMode.Fixtures,
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(["one", "two"], writer.Saved.Select(s => s.Cinema.Slug).ToList());
        Assert.Contains("cinema=one mode=fixtures films=1", output.ToString());
    }

    [Fact]
    public async Task RunAsync_AFailingCinemaDoesNotStopTheOthers_AndReturnsOne()
    {
        RecordingWriter writer = new();
        ScraperRunner runner = new(writer, TimeProvider.System, new StringWriter());

        int exit = await runner.RunAsync(
            [Registration("broken", _ => throw new HttpRequestException("boom")), Registration("fine", _ => Task.FromResult(WithOneFilm()))],
            Window,
            ScraperMode.Fixtures,
            CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal(2, writer.Saved.Count);
        ConnectorError error = Assert.Single(writer.Saved[0].Result.Errors);
        Assert.Equal("scrape", error.Phase);
        Assert.Equal("HttpRequestException", error.ExceptionType);
        Assert.Single(writer.Saved[1].Result.Films);
    }

    [Fact]
    public async Task RunAsync_AnHttpTimeoutIsRecordedAsAFailure_NotAsACancellation()
    {
        RecordingWriter writer = new();
        ScraperRunner runner = new(writer, TimeProvider.System, new StringWriter());

        // HttpClient reports a timeout as a TaskCanceledException while the run itself is still active.
        int exit = await runner.RunAsync(
            [Registration("slow", _ => throw new TaskCanceledException("timeout"))],
            Window,
            ScraperMode.Live,
            CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal("timeout", Assert.Single(writer.Saved[0].Result.Errors).Phase);
    }

    [Fact]
    public async Task RunAsync_CancellationStopsTheRun()
    {
        RecordingWriter writer = new();
        ScraperRunner runner = new(writer, TimeProvider.System, new StringWriter());
        using CancellationTokenSource cancellation = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            [Registration("one", _ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            })],
            Window,
            ScraperMode.Live,
            cancellation.Token));

        Assert.Empty(writer.Saved);
    }

    [Fact]
    public async Task RunAsync_PassesTheWindowToTheConnector()
    {
        DateOnly? seenToday = null;
        IReadOnlyList<DateOnly>? seenDates = null;
        ScraperRunner runner = new(new RecordingWriter(), TimeProvider.System, new StringWriter());

        await runner.RunAsync(
            [Registration("one", (today, dates) =>
            {
                seenToday = today;
                seenDates = dates;
                return Task.FromResult(WithOneFilm());
            })],
            Window,
            ScraperMode.Fixtures,
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 22), seenToday);
        Assert.Equal(8, seenDates!.Count);
    }

    private static ScrapeResult WithOneFilm() => new() { Films = [new ScrapedFilm { Title = "Film", TitleNormalized = "Film" }] };

    private static ConnectorRegistration Registration(string slug, Func<CancellationToken, Task<ScrapeResult>> scrape) =>
        Registration(slug, (_, _) => scrape(CancellationToken.None));

    private static ConnectorRegistration Registration(string slug, Func<DateOnly, IReadOnlyList<DateOnly>?, Task<ScrapeResult>> scrape)
    {
        Cinema cinema = new() { Slug = slug, Name = slug, City = "Foligno", Address = "Via Esempio 1", Latitude = 0, Longitude = 0 };
        return new ConnectorRegistration(cinema, new DelegateConnector(slug, scrape));
    }

    private sealed class DelegateConnector : ICinemaConnector
    {
        private readonly Func<DateOnly, IReadOnlyList<DateOnly>?, Task<ScrapeResult>> _scrape;

        public DelegateConnector(string slug, Func<DateOnly, IReadOnlyList<DateOnly>?, Task<ScrapeResult>> scrape)
        {
            CinemaSlug = slug;
            _scrape = scrape;
        }

        public string CinemaSlug { get; }

        public string CinemaName => CinemaSlug;

        public Task<ScrapeResult> ScrapeAsync(DateOnly today, IReadOnlyList<DateOnly>? dates, CancellationToken cancellationToken) => _scrape(today, dates);
    }

    private sealed class RecordingWriter : IScrapeResultWriter
    {
        public List<(Cinema Cinema, ScrapeResult Result)> Saved { get; } = [];

        public Task<SaveOutcome> SaveAsync(Cinema cinema, ScrapeResult result, CancellationToken cancellationToken)
        {
            Saved.Add((cinema, result));
            return Task.FromResult(new SaveOutcome { Films = result.Films.Count, Showings = 0, ShowingsReplaced = true });
        }
    }
}
