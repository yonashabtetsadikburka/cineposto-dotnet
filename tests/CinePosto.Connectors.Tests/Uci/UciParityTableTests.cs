using System.Text.Json;
using CinePosto.Connectors.Uci;
using CinePosto.TestSupport;

namespace CinePosto.Connectors.Tests.Uci;

/// <summary>Checks the two text helpers of the UCI connector against tables produced by the legacy functions.</summary>
public class UciParityTableTests
{
    public static IEnumerable<object?[]> StripHtmlCases() => Rows("uci_strip_html.tsv");

    public static IEnumerable<object?[]> ResolveUrlCases() => Rows("uci_resolve_url.tsv");

    [Theory]
    [MemberData(nameof(StripHtmlCases))]
    public void StripHtml_MatchesLegacy(string input, string expected)
    {
        Assert.Equal(expected, UciConnector.StripHtml(input));
    }

    [Theory]
    [MemberData(nameof(ResolveUrlCases))]
    public void Resolve_MatchesLegacyUrlJoin(string relative, string expected)
    {
        Assert.Equal(expected, UciUrlResolver.Resolve(new UciOptions().BaseUrl, relative));
    }

    [Theory]
    [InlineData("uci_strip_html.tsv", 20)]
    [InlineData("uci_resolve_url.tsv", 40)]
    public void ParityFile_HasEnoughDataRows(string fileName, int minimum)
    {
        int count = TsvParityLoader.Load(fileName).Count;
        Assert.True(count >= minimum, $"Parity file '{fileName}' has only {count} data rows; expected at least {minimum}.");
    }

    // Both columns are JSON strings, so that tabs, line breaks and quotes survive in a tab-separated file.
    private static IEnumerable<object?[]> Rows(string fileName) =>
        TsvParityLoader.Load(fileName).Select(row => new object?[]
        {
            JsonSerializer.Deserialize<string>(row[0]!),
            JsonSerializer.Deserialize<string>(row[1]!),
        });
}
