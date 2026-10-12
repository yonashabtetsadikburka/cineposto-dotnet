namespace CinePosto.Connectors.Clarici;

/// <summary>Settings of the Clarici connector, with defaults read from the legacy source.</summary>
public sealed record ClariciOptions(
    string MoviesUrl = "https://www.cinemaclarici.it/backend/movies.json",
    string SettingsUrl = "https://www.cinemaclarici.it/backend/settings.json",
    string Venue = "Politeama",
    string SiteUrl = "https://www.cinemaclarici.it/",
    string CinemaSlug = "clarici-foligno",
    string CinemaName = "Multiplex Politeama Clarici");
