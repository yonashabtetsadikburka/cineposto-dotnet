namespace CinePosto.Domain.Models;

/// <summary>How a backdrop lookup ended.</summary>
public enum BackdropLookupStatus
{
    /// <summary>The catalogue has an image.</summary>
    Found,

    /// <summary>The catalogue answered and has no image for the title.</summary>
    NotFound,

    /// <summary>This lookup failed: nothing is known about the title.</summary>
    Failed,

    /// <summary>The catalogue refuses the requests (credentials or rate limit): asking again would not help now.</summary>
    Refused,
}

/// <summary>The result of looking a film up. Only <see cref="BackdropLookupStatus.Found"/> has an address.</summary>
public sealed record BackdropLookup(BackdropLookupStatus Status, string? Url = null, string? FailureType = null);
