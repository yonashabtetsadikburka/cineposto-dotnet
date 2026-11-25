using System.Net;

namespace CinePosto.Scraper.Fixtures;

/// <summary>
/// Replaces the network: every request is answered from saved responses. An address with no saved response
/// gets a 404, exactly as if the site did not have the page. The connectors do not know the difference.
/// </summary>
internal sealed class FixtureHttpMessageHandler : HttpMessageHandler
{
    private readonly IReadOnlyDictionary<string, FixtureResponse> _responses;

    public FixtureHttpMessageHandler(IReadOnlyDictionary<string, FixtureResponse> responses)
    {
        _responses = responses;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string url = request.RequestUri?.AbsoluteUri ?? string.Empty;
        if (!_responses.TryGetValue(url, out FixtureResponse? saved))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        HttpResponseMessage response = new(saved.Status);
        if (saved.Body is not null)
        {
            response.Content = new StringContent(saved.Body);
        }

        return Task.FromResult(response);
    }
}

/// <summary>A saved HTTP response: a status and an optional body.</summary>
internal sealed record FixtureResponse(HttpStatusCode Status, string? Body);
