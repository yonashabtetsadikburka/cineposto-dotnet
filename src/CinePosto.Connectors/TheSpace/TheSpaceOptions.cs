using System.Globalization;

namespace CinePosto.Connectors.TheSpace;

/// <summary>Settings of The Space connector for one venue, with defaults read from the legacy source.</summary>
/// <remarks>
/// The terms of use of thespacecinema.it do not allow copying or republishing the site's data without authorization.
/// The owner uses this source for personal, non-public use only: see section 9 of the requirements.
/// </remarks>
public sealed record TheSpaceOptions(
    int CinemaId,
    string CinemaSlug,
    string CinemaName,
    string CinemaUrl,
    string ApiBaseUrl = "https://www.thespacecinema.it/api/microservice",
    string BaseUrl = "https://www.thespacecinema.it")
{
    /// <summary>The venue of Corciano.</summary>
    public static TheSpaceOptions Corciano { get; } = new(
        1027,
        "the-space-corciano",
        "The Space Cinema Corciano",
        "https://www.thespacecinema.it/cinema/corciano/al-cinema");

    /// <summary>The venue of Terni.</summary>
    public static TheSpaceOptions Terni { get; } = new(
        1006,
        "the-space-terni",
        "The Space Cinema Terni",
        "https://www.thespacecinema.it/al-cinema/terni");

    /// <summary>Pause before each request that follows the first one, so that the site is not asked in a burst.</summary>
    public TimeSpan RequestDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Address that starts a guest session.</summary>
    public string AuthUrl => $"{ApiBaseUrl}/auth/token";

    /// <summary>Address of the films of one day.</summary>
    public string FilmsUrl(DateOnly date)
    {
        string venue = CinemaId.ToString(CultureInfo.InvariantCulture);
        string day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{ApiBaseUrl}/showings/cinemas/{venue}/films?showingDate={day}&includesSession=true&includeSessionAttributes=true";
    }
}
