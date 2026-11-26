namespace CinePosto.Connectors.Uci;

/// <summary>Settings of the UCI connector, with defaults read from the legacy source.</summary>
/// <remarks>
/// The programming endpoint is an undocumented backend that the legacy project found by watching the site's own requests.
/// It can change without notice, and its terms of use have not been checked yet (requirements, open questions).
/// </remarks>
public sealed record UciOptions(
    string ProgrammingUrlTemplate = "https://myuci---uci-backend-production-nfluwp7wga-oc.a.run.app/api/theatres/uci-cinemas-perugia/programming/{date}",
    string BaseUrl = "https://ucicinemas.it",
    string CinemaSlug = "uci-perugia",
    string CinemaName = "UCI Cinemas Perugia");
