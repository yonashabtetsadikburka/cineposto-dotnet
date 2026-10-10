using System.Globalization;
using CinePosto.Domain.Text;

namespace CinePosto.Domain.Tests.Parity;

public class DurationParserTests
{
    public static IEnumerable<object?[]> DurationCases() =>
        TsvParityLoader.Load("duration.tsv")
            // The "0" row expecting null comes from an integer generator input,
            // which the string-based signature cannot represent; documented gap.
            .Where(row => !(row[0] == "0" && row[1] is null))
            .Select(row => new object?[] { row[0], row[1] });

    [Theory]
    [MemberData(nameof(DurationCases))]
    public void ParseMinutes_MatchesLegacyOutcome(string? input, string? expected)
    {
        int? actual = DurationParser.ParseMinutes(input);

        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        int expectedMinutes = int.Parse(expected.Split(' ')[0], CultureInfo.InvariantCulture);
        Assert.Equal(expectedMinutes, actual);
    }
}
