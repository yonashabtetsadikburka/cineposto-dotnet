namespace CinePosto.Api;

/// <summary>
/// Container healthcheck. The runtime image has no curl, so the app can probe itself:
/// <c>dotnet CinePosto.Api.dll --healthcheck</c> exits 0 when <c>/health</c> answers with a success status.
/// </summary>
internal static class HealthProbe
{
    public const string Flag = "--healthcheck";

    public static async Task<int> RunAsync()
    {
        string port = ResolvePort(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"));
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(4) };
        try
        {
            using HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}/health").ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }

    /// <summary>Takes the first port of <c>ASPNETCORE_HTTP_PORTS</c> (a semicolon separated list), defaulting to 8080.</summary>
    internal static string ResolvePort(string? configured)
    {
        string? first = configured?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return !string.IsNullOrEmpty(first) && int.TryParse(first, out int port) && port is > 0 and < 65536 ? first : "8080";
    }
}
