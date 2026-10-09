namespace CinePosto.Domain.Models;

/// <summary>A cinema whose schedule can be scraped.</summary>
public sealed record Cinema
{
    /// <summary>Stable identifier used across connectors.</summary>
    public required string Slug { get; init; }

    /// <summary>Display name of the cinema.</summary>
    public required string Name { get; init; }

    /// <summary>City where the cinema is located.</summary>
    public required string City { get; init; }

    /// <summary>Street address of the cinema.</summary>
    public required string Address { get; init; }

    /// <summary>Region where the cinema is located.</summary>
    public string Region { get; init; } = "Umbria";

    /// <summary>Geographic latitude of the cinema.</summary>
    public required double Latitude { get; init; }

    /// <summary>Geographic longitude of the cinema.</summary>
    public required double Longitude { get; init; }

    /// <summary>Official website of the cinema, when known.</summary>
    public string? Website { get; init; }
}
