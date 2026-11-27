namespace CinePosto.Connectors.Tmdb;

/// <summary>Settings of the TMDB backdrop lookup, with defaults read from the legacy source.</summary>
/// <remarks>
/// The use of the TMDB API has conditions (attribution, no commercial use, nothing cached for more than 6 months): see
/// section 9 of the requirements. The read access token is not a setting kept here, so that it cannot be printed with it.
/// </remarks>
public sealed record TmdbOptions(
    string ApiBaseUrl = "https://api.themoviedb.org/3",
    string ImageBaseUrl = "https://image.tmdb.org/t/p",
    string BackdropSize = "w1280",
    string Language = "it-IT")
{
    /// <summary>Pause before each request that follows the first one (the legacy minimum interval).</summary>
    public TimeSpan RequestDelay { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Address of the search of a title.</summary>
    public string SearchUrl(string title) =>
        $"{ApiBaseUrl}/search/movie?query={Uri.EscapeDataString(title)}&language={Language}&include_adult=false";

    /// <summary>Address of the images of a film: those in English and those without text.</summary>
    public string ImagesUrl(string movieId) =>
        $"{ApiBaseUrl}/movie/{Uri.EscapeDataString(movieId)}/images?include_image_language=en,null";

    /// <summary>Address of an image, from the path that the API gives.</summary>
    public string ImageUrl(string filePath) => $"{ImageBaseUrl}/{BackdropSize}{filePath}";
}
