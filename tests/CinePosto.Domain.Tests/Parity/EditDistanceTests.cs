using System.Globalization;
using CinePosto.Domain.Text;

namespace CinePosto.Domain.Tests.Parity;

public class EditDistanceTests
{
    public static IEnumerable<object?[]> LevenshteinCases() =>
        TsvParityLoader.Load("levenshtein.tsv")
            .Select(row => new object?[] { row[0], row[1], int.Parse(row[2]!, CultureInfo.InvariantCulture) });

    [Theory]
    [MemberData(nameof(LevenshteinCases))]
    public void Levenshtein_MatchesLegacyDistance(string? a, string? b, int expected)
    {
        Assert.Equal(expected, EditDistance.Levenshtein(a!, b!));
    }
}
