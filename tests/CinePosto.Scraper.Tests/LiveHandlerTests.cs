using System.Net;
using System.Net.Sockets;
using CinePosto.Connectors.TheSpace;
using CinePosto.Domain.Models;
using CinePosto.Scraper;

namespace CinePosto.Scraper.Tests;

/// <summary>
/// Checks the transport of the live mode against a server on this machine, never a real site. The guest session of
/// The Space is a cookie set by the first answer, so the handler has to send it back with the requests that follow.
/// </summary>
public sealed class LiveHandlerTests
{
    [Fact]
    public async Task LiveHandler_SendsBackTheSessionCookie()
    {
        using HttpListener server = new();
        string root = $"http://localhost:{FreePort()}";
        server.Prefixes.Add($"{root}/");
        server.Start();

        List<(string Method, string? Cookie)> received = [];
        Task serving = Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                HttpListenerContext context = await server.GetContextAsync();
                received.Add((context.Request.HttpMethod, context.Request.Headers["Cookie"]));
                if (context.Request.HttpMethod == "POST")
                {
                    context.Response.Headers.Add("Set-Cookie", "guest=abc123; Path=/; HttpOnly");
                }

                await using (StreamWriter writer = new(context.Response.OutputStream))
                {
                    await writer.WriteAsync("""{"result": []}""");
                }
            }
        });

        TheSpaceOptions options = TheSpaceOptions.Corciano with
        {
            ApiBaseUrl = $"{root}/api",
            RequestDelay = TimeSpan.Zero,
        };
        using HttpClient client = new(ScraperApp.CreateLiveHandler()) { Timeout = TimeSpan.FromSeconds(10) };
        DateOnly day = new(2026, 9, 22);

        ScrapeResult result = await new TheSpaceConnector(client, options).ScrapeAsync(day, [day, day.AddDays(1)], CancellationToken.None);
        await serving.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(result.Errors);
        Assert.Equal(
            [("POST", null), ("GET", "guest=abc123"), ("GET", "guest=abc123")],
            received);
    }

    private static int FreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
