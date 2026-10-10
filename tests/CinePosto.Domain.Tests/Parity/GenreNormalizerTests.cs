using System.Text.Json;
using CinePosto.Domain.Text;

namespace CinePosto.Domain.Tests.Parity;

public class GenreNormalizerTests
{
    public static IEnumerable<object?[]> GenreCases() =>
        TsvParityLoader.Load("genres.tsv")
            .Select(row => new object?[] { row[0], row[1] });

    [Theory]
    [MemberData(nameof(GenreCases))]
    public void Normalize_MatchesLegacyOutcome(string? inputJson, string? expectedJson)
    {
        // Rows whose expectation starts with EXCEPTION: record a legacy crash.
        if (expectedJson!.StartsWith("EXCEPTION:", StringComparison.Ordinal))
        {
            using JsonDocument probe = JsonDocument.Parse(inputJson!);
            Assert.Throws<InvalidOperationException>(() => GenreNormalizer.Normalize(probe.RootElement));
            return;
        }

        using JsonDocument input = JsonDocument.Parse(inputJson!);
        IReadOnlyList<string> actual = GenreNormalizer.Normalize(input.RootElement);

        using JsonDocument expected = JsonDocument.Parse(expectedJson);
        List<string> expectedGenres = expected.RootElement.EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(expectedGenres, actual);
    }
}
