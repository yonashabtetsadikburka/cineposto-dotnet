using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CinePosto.Connectors.Clarici;
using CinePosto.Connectors.Uci;
using CinePosto.Infrastructure.Persistence;
using CinePosto.Scraper.Fixtures;

namespace CinePosto.Scraper;

/// <summary>
/// Entry point logic of the scraper. Exit codes: 0 success, 1 a cinema failed without producing films,
/// 2 invalid configuration, 3 database error, 130 cancelled.
/// </summary>
internal static class ScraperApp
{
    public const int ExitOk = 0;
    public const int ExitCinemaFailed = 1;
    public const int ExitConfiguration = 2;
    public const int ExitDatabase = 3;
    public const int ExitCancelled = 130;

    public static async Task<int> RunAsync(
        Func<string, string?> getVariable,
        TextWriter output,
        TextWriter error,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!ScraperSettings.TryParse(getVariable, out ScraperSettings? settings, out string? settingsError))
        {
            await error.WriteLineAsync(settingsError).ConfigureAwait(false);
            return ExitConfiguration;
        }

        ClariciOptions clariciOptions = new();
        UciOptions uciOptions = new();

        ScrapeWindow window;
        HttpMessageHandler handler;
        if (settings!.Mode == ScraperMode.Fixtures)
        {
            FixtureCase fixture;
            UciFixtureCase uciFixture;
            try
            {
                fixture = FixtureCase.Load(settings.ClariciFixturesDirectory!);
                uciFixture = UciFixtureCase.Load(settings.UciFixturesDirectory!);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or FormatException)
            {
                await error.WriteLineAsync($"Cannot read the fixtures: {ex.Message}").ConfigureAwait(false);
                return ExitConfiguration;
            }

            // One window for every cinema: the days come from the Clarici case and the UCI case has to cover them.
            window = new ScrapeWindow(fixture.Today, fixture.Dates);
            Dictionary<string, FixtureResponse> responses = BuildClariciResponses(fixture, clariciOptions);
            foreach ((string url, FixtureResponse response) in uciFixture.ToResponses(uciOptions))
            {
                responses[url] = response;
            }

            handler = new FixtureHttpMessageHandler(responses);
        }
        else
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(ScrapeWindow.TimeZoneId, out TimeZoneInfo? zone))
            {
                await error.WriteLineAsync($"Time zone '{ScrapeWindow.TimeZoneId}' is not available on this system.").ConfigureAwait(false);
                return ExitConfiguration;
            }

            window = ScrapeWindow.ForNow(clock, zone);
            handler = new SocketsHttpHandler();
        }

        using HttpClient httpClient = new(handler) { Timeout = settings.HttpTimeout };
        try
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(settings.UserAgent);
        }
        catch (FormatException)
        {
            await error.WriteLineAsync("SCRAPER_USER_AGENT is not a valid User-Agent value.").ConfigureAwait(false);
            return ExitConfiguration;
        }

        try
        {
            await using CinePostoDbContext db = CinePostoDatabase.CreateContext(settings.DatabasePath);
            try
            {
                await DatabaseInitializer.MigrateAsync(db, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await error.WriteLineAsync($"Database error: {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
                return ExitDatabase;
            }

            SqliteShowtimeStore store = new(db);
            ScraperRunner runner = new(store, clock, output);
            return await runner.RunAsync(ConnectorRegistry.Create(httpClient, clock, clariciOptions, uciOptions), window, settings.Mode, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return ExitCancelled;
        }
    }

    private static Dictionary<string, FixtureResponse> BuildClariciResponses(FixtureCase fixture, ClariciOptions options)
    {
        return new Dictionary<string, FixtureResponse>(StringComparer.Ordinal)
        {
            [options.MoviesUrl] = new FixtureResponse(fixture.MoviesStatus, fixture.MoviesBody),
            [options.SettingsUrl] = fixture.SettingsBody is null
                ? new FixtureResponse(HttpStatusCode.NotFound, null)
                : new FixtureResponse(fixture.SettingsStatus, fixture.SettingsBody),
        };
    }
}
