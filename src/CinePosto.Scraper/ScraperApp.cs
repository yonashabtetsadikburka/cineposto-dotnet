using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CinePosto.Connectors.Clarici;
using CinePosto.Connectors.TheSpace;
using CinePosto.Connectors.Tmdb;
using CinePosto.Connectors.Uci;
using CinePosto.Domain.Abstractions;
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

    // In fixtures mode nothing is sent anywhere: the token is a placeholder, and the images point to an address that
    // does not exist, so that the web page does not contact TMDB either.
    private const string FixtureTmdbToken = "fixtures-need-no-token";
    private const string FixtureImageBaseUrl = "https://example.test/tmdb";

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
        TheSpaceOptions theSpaceCorciano = TheSpaceOptions.Corciano;
        TheSpaceOptions theSpaceTerni = TheSpaceOptions.Terni;
        TmdbOptions tmdbOptions = new();
        string? tmdbToken = settings!.TmdbReadToken;

        ScrapeWindow window;
        HttpMessageHandler handler;
        if (settings!.Mode == ScraperMode.Fixtures)
        {
            FixtureCase fixture;
            UciFixtureCase uciFixture;
            Dictionary<string, FixtureResponse> theSpaceResponses;
            Dictionary<string, FixtureResponse> tmdbResponses;
            tmdbOptions = new TmdbOptions(ImageBaseUrl: FixtureImageBaseUrl) { RequestDelay = TimeSpan.Zero };
            tmdbToken = FixtureTmdbToken;
            try
            {
                fixture = FixtureCase.Load(settings.ClariciFixturesDirectory!);
                uciFixture = UciFixtureCase.Load(settings.UciFixturesDirectory!);
                theSpaceResponses = TheSpaceFixtureCase.Load(settings.TheSpaceFixturesDirectory!)
                    .ToResponses([theSpaceCorciano, theSpaceTerni]);
                tmdbResponses = TmdbFixtureCase.Load(settings.TmdbFixturesDirectory!).ToResponses(tmdbOptions);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or FormatException)
            {
                await error.WriteLineAsync($"Cannot read the fixtures: {ex.Message}").ConfigureAwait(false);
                return ExitConfiguration;
            }

            // One window for every cinema: the days come from the Clarici case and the other cases have to cover them.
            window = new ScrapeWindow(fixture.Today, fixture.Dates);
            Dictionary<string, FixtureResponse> responses = BuildClariciResponses(fixture, clariciOptions);
            foreach ((string url, FixtureResponse response) in uciFixture.ToResponses(uciOptions).Concat(theSpaceResponses).Concat(tmdbResponses))
            {
                responses[url] = response;
            }

            handler = new FixtureHttpMessageHandler(responses);

            // Saved responses need no pause between requests.
            theSpaceCorciano = theSpaceCorciano with { RequestDelay = TimeSpan.Zero };
            theSpaceTerni = theSpaceTerni with { RequestDelay = TimeSpan.Zero };
        }
        else
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(ScrapeWindow.TimeZoneId, out TimeZoneInfo? zone))
            {
                await error.WriteLineAsync($"Time zone '{ScrapeWindow.TimeZoneId}' is not available on this system.").ConfigureAwait(false);
                return ExitConfiguration;
            }

            window = ScrapeWindow.ForNow(clock, zone);
            handler = CreateLiveHandler();
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

            // Done on every run, also without a token: what was looked up too long ago must not be kept.
            int expired = await store.ExpireBackdropsAsync(clock.GetUtcNow() - BackdropEnricher.MaxAge, cancellationToken).ConfigureAwait(false);
            if (expired > 0)
            {
                await output.WriteLineAsync($"backdrops expired={expired}").ConfigureAwait(false);
            }

            ScraperRunner runner = new(store, clock, output);
            int exitCode = await runner.RunAsync(ConnectorRegistry.Create(httpClient, clock, clariciOptions, uciOptions, theSpaceCorciano, theSpaceTerni), window, settings.Mode, cancellationToken).ConfigureAwait(false);

            if (tmdbToken is null)
            {
                await output.WriteLineAsync("backdrops disabled reason=no_token").ConfigureAwait(false);
            }
            else
            {
                IBackdropProvider backdrops = new TmdbBackdropProvider(httpClient, tmdbToken, tmdbOptions, clock);
                await new BackdropEnricher(store, backdrops, clock, output).RunAsync(cancellationToken).ConfigureAwait(false);
            }

            return exitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return ExitCancelled;
        }
    }

    /// <summary>The transport of the live mode. It keeps cookies: the guest session of The Space is made of them.</summary>
    internal static SocketsHttpHandler CreateLiveHandler() => new() { UseCookies = true };

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
