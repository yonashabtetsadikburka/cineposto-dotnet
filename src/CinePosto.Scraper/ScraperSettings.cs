using System.Globalization;

namespace CinePosto.Scraper;

/// <summary>Where the scraper takes its data from.</summary>
internal enum ScraperMode
{
    /// <summary>Saved responses read from disk; no network is used.</summary>
    Fixtures,

    /// <summary>The real websites of the cinemas.</summary>
    Live,
}

/// <summary>Scraper configuration, read from environment variables.</summary>
internal sealed record ScraperSettings
{
    public const string DefaultUserAgent = "CinePosto/0.1";

    public required ScraperMode Mode { get; init; }

    public required string DatabasePath { get; init; }

    /// <summary>Folder with the saved responses of Clarici. It also sets the days of the run.</summary>
    public string? ClariciFixturesDirectory { get; init; }

    /// <summary>Folder with the saved responses of UCI. Its responses must cover the days of the run.</summary>
    public string? UciFixturesDirectory { get; init; }

    /// <summary>Folder with the saved responses of The Space, one folder per venue. They must cover the days of the run.</summary>
    public string? TheSpaceFixturesDirectory { get; init; }

    /// <summary>Folder with the saved answers of the TMDB API.</summary>
    public string? TmdbFixturesDirectory { get; init; }

    /// <summary>
    /// TMDB API read access token, a secret. Used in live mode only; without it no backdrop is looked up. It is left out
    /// of <see cref="ToString"/> and must never be written to a log.
    /// </summary>
    public string? TmdbReadToken { get; init; }

    public required string UserAgent { get; init; }

    public required TimeSpan HttpTimeout { get; init; }

    /// <summary>Only the mode: a record would otherwise print every value, the token included.</summary>
    public override string ToString() => $"{nameof(ScraperSettings)} {{ {nameof(Mode)} = {Mode} }}";

    /// <summary>
    /// Reads the settings. There is no default mode, so the scraper never reaches the network by accident.
    /// Returns <see langword="false"/> with a message when the configuration is not usable.
    /// </summary>
    public static bool TryParse(Func<string, string?> getVariable, out ScraperSettings? settings, out string? error)
    {
        settings = null;

        string? modeText = getVariable("SCRAPER_MODE");
        ScraperMode mode;
        if (string.Equals(modeText, "fixtures", StringComparison.OrdinalIgnoreCase))
        {
            mode = ScraperMode.Fixtures;
        }
        else if (string.Equals(modeText, "live", StringComparison.OrdinalIgnoreCase))
        {
            mode = ScraperMode.Live;
        }
        else
        {
            error = "SCRAPER_MODE must be 'fixtures' or 'live'.";
            return false;
        }

        string? clariciFixturesDirectory = getVariable("SCRAPER_CLARICI_FIXTURES_DIR");
        if (mode == ScraperMode.Fixtures && string.IsNullOrWhiteSpace(clariciFixturesDirectory))
        {
            error = "SCRAPER_CLARICI_FIXTURES_DIR is required when SCRAPER_MODE is 'fixtures'.";
            return false;
        }

        string? uciFixturesDirectory = getVariable("SCRAPER_UCI_FIXTURES_DIR");
        if (mode == ScraperMode.Fixtures && string.IsNullOrWhiteSpace(uciFixturesDirectory))
        {
            error = "SCRAPER_UCI_FIXTURES_DIR is required when SCRAPER_MODE is 'fixtures'.";
            return false;
        }

        string? theSpaceFixturesDirectory = getVariable("SCRAPER_THESPACE_FIXTURES_DIR");
        if (mode == ScraperMode.Fixtures && string.IsNullOrWhiteSpace(theSpaceFixturesDirectory))
        {
            error = "SCRAPER_THESPACE_FIXTURES_DIR is required when SCRAPER_MODE is 'fixtures'.";
            return false;
        }

        string? tmdbFixturesDirectory = getVariable("SCRAPER_TMDB_FIXTURES_DIR");
        if (mode == ScraperMode.Fixtures && string.IsNullOrWhiteSpace(tmdbFixturesDirectory))
        {
            error = "SCRAPER_TMDB_FIXTURES_DIR is required when SCRAPER_MODE is 'fixtures'.";
            return false;
        }

        // The saved answers need no token: in fixtures mode a real one is not even read.
        string? tmdbReadToken = mode == ScraperMode.Live ? getVariable("TMDB_READ_TOKEN")?.Trim() : null;
        if (tmdbReadToken?.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')) == true)
        {
            // The value is not repeated in the message, because it is a secret.
            error = "TMDB_READ_TOKEN has characters that a token cannot have.";
            return false;
        }

        double timeoutSeconds = 30;
        string? timeoutText = getVariable("SCRAPER_HTTP_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(timeoutText)
            && !(double.TryParse(timeoutText, NumberStyles.Float, CultureInfo.InvariantCulture, out timeoutSeconds)
                 && timeoutSeconds is >= 1 and <= 300))
        {
            error = "SCRAPER_HTTP_TIMEOUT_SECONDS must be a number between 1 and 300.";
            return false;
        }

        string? databasePath = getVariable("CINEPOSTO_DB_PATH");
        string? userAgent = getVariable("SCRAPER_USER_AGENT");

        settings = new ScraperSettings
        {
            Mode = mode,
            DatabasePath = string.IsNullOrWhiteSpace(databasePath) ? "cineposto.db" : databasePath,
            ClariciFixturesDirectory = clariciFixturesDirectory,
            UciFixturesDirectory = uciFixturesDirectory,
            TheSpaceFixturesDirectory = theSpaceFixturesDirectory,
            TmdbFixturesDirectory = tmdbFixturesDirectory,
            TmdbReadToken = string.IsNullOrEmpty(tmdbReadToken) ? null : tmdbReadToken,
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent,
            HttpTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        error = null;
        return true;
    }
}
