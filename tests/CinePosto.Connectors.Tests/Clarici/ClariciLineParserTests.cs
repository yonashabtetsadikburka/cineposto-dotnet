using System.Globalization;
using System.Text.Json;
using CinePosto.Connectors.Clarici;
using CinePosto.TestSupport;

namespace CinePosto.Connectors.Tests.Clarici;

public class ClariciLineParserTests
{
    public static IEnumerable<object?[]> ParseLineCases() =>
        TsvParityLoader.Load("clarici_parse_line.tsv")
            .Select(row => new object?[] { row[0], row[1], row[2], row[3] });

    [Theory]
    [MemberData(nameof(ParseLineCases))]
    public void Parse_MatchesLegacyOutcome(string? line, string? reference, string? wanted, string? expected)
    {
        DateOnly referenceDate = DateOnly.Parse(reference!, CultureInfo.InvariantCulture);
        IReadOnlySet<DateOnly>? wantedDates = wanted!.Equals("window", StringComparison.Ordinal)
            ? Enumerable.Range(0, 8).Select(referenceDate.AddDays).ToHashSet()
            : null;

        // Covered by parity rows with unknown weekday abbreviations (e.g. Xyz 24/09).
        if (expected!.StartsWith("EXCEPTION:", StringComparison.Ordinal))
        {
            Assert.ThrowsAny<Exception>(() => ClariciLineParser.Parse(line!, referenceDate, wantedDates));
            return;
        }

        ParsedLine? actual = ClariciLineParser.Parse(line!, referenceDate, wantedDates);

        if (expected.Equals("null", StringComparison.Ordinal))
        {
            Assert.Null(actual);
            return;
        }

        using JsonDocument doc = JsonDocument.Parse(expected);
        string expectedDate = doc.RootElement.GetProperty("date").GetString()!;
        List<string> expectedTimes = doc.RootElement.GetProperty("times").EnumerateArray()
            .Select(e => e.GetString()!)
            .Where(IsValidTime)
            .ToList();

        // Documented deviation: invalid times are dropped, so an empty remainder expects null.
        if (expectedTimes.Count == 0)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expectedDate, actual.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(expectedTimes, actual.Value.Times.Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void ParityFile_HasEnoughDataRows()
    {
        int count = TsvParityLoader.Load("clarici_parse_line.tsv").Count;
        Assert.True(count >= 74, $"Parity file has only {count} data rows; expected at least 74.");
    }

    private static bool IsValidTime(string time)
    {
        string[] parts = time.Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            && hours <= 23 && minutes <= 59;
    }
}
    