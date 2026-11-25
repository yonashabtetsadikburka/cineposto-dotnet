using System.Security.Cryptography;
using System.Text;

namespace CinePosto.Api;

/// <summary>Credentials that protect the admin endpoints. There is no default token.</summary>
internal sealed record AdminOptions(string Token)
{
    /// <summary>Shortest accepted token length.</summary>
    public const int MinimumTokenLength = 16;

    /// <summary>Header that carries the admin token.</summary>
    public const string HeaderName = "X-Admin-Token";

    /// <summary>Returns <see langword="true"/> when the token is present and long enough to be used.</summary>
    public static bool IsValidToken(string? token)
    {
        return !string.IsNullOrWhiteSpace(token) && token.Length >= MinimumTokenLength;
    }

    /// <summary>Compares the provided token with the configured one in constant time.</summary>
    public bool Matches(string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        // Hashing first makes both values the same length, so the comparison leaks nothing about the token.
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(Token));
        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
