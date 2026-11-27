using CinePosto.Domain.Abstractions;
using CinePosto.Domain.Models;

namespace CinePosto.Scraper;

/// <summary>
/// Gives a backdrop to the films that have none, after the cinemas have been scraped. It never makes a run fail: a film
/// without a backdrop is shown with its poster.
/// </summary>
internal sealed class BackdropEnricher
{
    /// <summary>
    /// How long the result of a lookup is kept. The terms of the TMDB API allow 6 months at most; the margin covers a
    /// scraper that does not run for a few weeks.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(150);

    /// <summary>After this many failures in a row the catalogue is taken to be unreachable.</summary>
    public const int MaxFailuresInARow = 3;

    private readonly IBackdropStore _store;
    private readonly IBackdropProvider _provider;
    private readonly TimeProvider _clock;
    private readonly TextWriter _output;

    public BackdropEnricher(IBackdropStore store, IBackdropProvider provider, TimeProvider clock, TextWriter output)
    {
        _store = store;
        _provider = provider;
        _clock = clock;
        _output = output;
    }

    /// <summary>
    /// Looks up every film that has no stored result and stores what is found, also when it is nothing. A failed lookup
    /// is not stored, so it is tried again by the next run. Stops when the catalogue refuses the requests.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Film> films = await _store.GetFilmsNeedingBackdropAsync(cancellationToken).ConfigureAwait(false);
        int found = 0;
        int notFound = 0;
        int failed = 0;
        int failuresInARow = 0;
        string stopped = string.Empty;
        string? failureType = null;

        foreach (Film film in films)
        {
            BackdropLookup lookup = await _provider.FindAsync(film.Title, cancellationToken).ConfigureAwait(false);
            if (lookup.Status is BackdropLookupStatus.Found or BackdropLookupStatus.NotFound)
            {
                await _store.SaveBackdropAsync(film.Id, lookup.Url, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                failuresInARow = 0;
                if (lookup.Status == BackdropLookupStatus.Found)
                {
                    found++;
                }
                else
                {
                    notFound++;
                }

                continue;
            }

            failed++;
            failuresInARow++;
            failureType = lookup.FailureType;
            if (lookup.Status == BackdropLookupStatus.Refused)
            {
                stopped = " stopped=refused";
                break;
            }

            if (failuresInARow == MaxFailuresInARow)
            {
                stopped = " stopped=failures";
                break;
            }
        }

        string type = failureType is null ? string.Empty : $" last_failure={failureType}";
        _output.WriteLine($"backdrops films={films.Count} found={found} not_found={notFound} failed={failed}{type}{stopped}");
    }
}
