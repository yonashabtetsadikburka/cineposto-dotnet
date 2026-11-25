using CinePosto.Domain.Abstractions;
using CinePosto.Domain.Models;

namespace CinePosto.Scraper;

/// <summary>Runs every connector, stores what it found and reports one line per cinema.</summary>
internal sealed class ScraperRunner
{
    private readonly IScrapeResultWriter _writer;
    private readonly TimeProvider _clock;
    private readonly TextWriter _output;

    public ScraperRunner(IScrapeResultWriter writer, TimeProvider clock, TextWriter output)
    {
        _writer = writer;
        _clock = clock;
        _output = output;
    }

    /// <summary>
    /// Returns 0 when every cinema produced data and 1 when at least one failed without producing any film.
    /// A failing connector never stops the others. Cancelling the token stops the run.
    /// </summary>
    public async Task<int> RunAsync(IReadOnlyList<ConnectorRegistration> registrations, ScrapeWindow window, ScraperMode mode, CancellationToken cancellationToken)
    {
        int failedCinemas = 0;
        foreach (ConnectorRegistration registration in registrations)
        {
            ScrapeResult result = await ScrapeSafelyAsync(registration, window, cancellationToken).ConfigureAwait(false);
            SaveOutcome outcome = await _writer.SaveAsync(registration.Cinema, result, cancellationToken).ConfigureAwait(false);

            string modeName = mode.ToString().ToLowerInvariant();
            _output.WriteLine(
                $"cinema={registration.Cinema.Slug} mode={modeName} films={result.Films.Count} showings={outcome.Showings} errors={result.Errors.Count} showings_replaced={outcome.ShowingsReplaced.ToString().ToLowerInvariant()}");
            foreach (ConnectorError error in result.Errors)
            {
                _output.WriteLine($"error cinema={error.CinemaSlug} phase={error.Phase} type={error.ExceptionType}");
            }

            if (result.Errors.Count > 0 && result.Films.Count == 0)
            {
                failedCinemas++;
            }
        }

        return failedCinemas > 0 ? 1 : 0;
    }

    private async Task<ScrapeResult> ScrapeSafelyAsync(ConnectorRegistration registration, ScrapeWindow window, CancellationToken cancellationToken)
    {
        try
        {
            return await registration.Connector.ScrapeAsync(window.Today, window.Dates, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // An HTTP timeout surfaces as a TaskCanceledException while the run itself is not being cancelled.
            // The connectors let it through, so it is recorded here as a failure of that cinema only.
            return new ScrapeResult
            {
                Errors =
                [
                    new ConnectorError
                    {
                        CinemaSlug = registration.Cinema.Slug,
                        Timestamp = _clock.GetUtcNow(),
                        ExceptionType = ex.GetType().Name,
                        Phase = ex is OperationCanceledException ? "timeout" : "scrape",
                        Detail = ex.Message,
                    },
                ],
            };
        }
    }
}
