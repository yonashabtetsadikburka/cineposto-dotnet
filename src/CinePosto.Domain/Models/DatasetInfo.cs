namespace CinePosto.Domain.Models;

/// <summary>Row counts of the stored catalog.</summary>
public sealed record DatasetInfo
{
    /// <summary>Number of stored cinemas.</summary>
    public required int Cinemas { get; init; }

    /// <summary>Number of stored films.</summary>
    public required int Films { get; init; }

    /// <summary>Number of stored showings.</summary>
    public required int Showings { get; init; }
}
