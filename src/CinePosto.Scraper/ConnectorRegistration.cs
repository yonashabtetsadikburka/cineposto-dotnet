using CinePosto.Connectors;
using CinePosto.Connectors.Clarici;
using CinePosto.Connectors.Uci;
using CinePosto.Domain.Models;
using CinePosto.Infrastructure.Catalog;

namespace CinePosto.Scraper;

/// <summary>A connector together with the cinema it scrapes.</summary>
internal sealed record ConnectorRegistration(Cinema Cinema, ICinemaConnector Connector);

/// <summary>The single place that lists the connectors. New cinemas are added here and in the cinema catalog.</summary>
internal static class ConnectorRegistry
{
    public static IReadOnlyList<ConnectorRegistration> Create(
        HttpClient httpClient,
        TimeProvider clock,
        ClariciOptions clariciOptions,
        UciOptions uciOptions)
    {
        return
        [
            Register(CinemaCatalog.Clarici, new ClariciConnector(httpClient, clock, clariciOptions)),
            Register(CinemaCatalog.UciPerugia, new UciConnector(httpClient, clock, uciOptions)),
        ];
    }

    private static ConnectorRegistration Register(Cinema cinema, ICinemaConnector connector)
    {
        if (!string.Equals(cinema.Slug, connector.CinemaSlug, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Connector '{connector.CinemaSlug}' does not match catalog cinema '{cinema.Slug}'.");
        }

        return new ConnectorRegistration(cinema, connector);
    }
}
