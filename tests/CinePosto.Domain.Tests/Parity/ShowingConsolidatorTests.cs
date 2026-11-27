using System.Globalization;
using System.Text.Json;
using CinePosto.Domain.Models;
using CinePosto.Domain.Showings;

namespace CinePosto.Domain.Tests.Parity;

/// <summary>Compares the merge of showings with <c>consolidate_showings.json</c>, produced by the legacy function.</summary>
public class ShowingConsolidatorTests
{
    private static readonly Lazy<Dictionary<string, JsonElement>> Cases = new(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Parity", "consolidate_showings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.Clone(), StringComparer.Ordinal);
    });

    public static TheoryData<string> CaseNames()
    {
        TheoryData<string> data = [];
        foreach (string name in Cases.Value.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Consolidate_MatchesTheLegacyOutput(string name)
    {
        JsonElement testCase = Cases.Value[name];
        List<ScrapedShowing> input = testCase.GetProperty("input").EnumerateArray().Select(Read).ToList();

        IReadOnlyList<ScrapedShowing> actual = ShowingConsolidator.Consolidate(input);

        ScrapedShowing[] expected = testCase.GetProperty("expected").EnumerateArray().Select(Read).ToArray();
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].CinemaSlug, actual[i].CinemaSlug);
            Assert.Equal(expected[i].Date, actual[i].Date);
            Assert.Equal(expected[i].Times, actual[i].Times);
            Assert.Equal(expected[i].Screen, actual[i].Screen);
            Assert.Equal(expected[i].Language, actual[i].Language);
            Assert.Equal(expected[i].SourceUrl, actual[i].SourceUrl);
            Assert.Equal(expected[i].SessionAttributes, actual[i].SessionAttributes);
        }
    }

    [Fact]
    public void TheParityFile_HasEnoughCases()
    {
        Assert.True(Cases.Value.Count >= 20, "Expected at least 20 parity cases.");
    }

    [Fact]
    public void Consolidate_DoesNotChangeTheShowingsItReceives()
    {
        List<string> attributes = ["2D"];
        ScrapedShowing first = Showing("18:00", attributes);
        ScrapedShowing second = Showing("21:00", ["2D"]);

        ShowingConsolidator.Consolidate([first, second]);

        Assert.Equal([new TimeOnly(18, 0)], first.Times);
        Assert.Equal(["2D"], attributes);
    }

    private static ScrapedShowing Showing(string time, IReadOnlyList<string> attributes) => new()
    {
        CinemaSlug = "cinema-a",
        Date = new DateOnly(2026, 9, 22),
        Times = [TimeOnly.Parse(time, CultureInfo.InvariantCulture)],
        SessionAttributes = attributes,
    };

    private static ScrapedShowing Read(JsonElement element) => new()
    {
        CinemaSlug = element.GetProperty("cinema_slug").GetString()!,
        Date = DateOnly.ParseExact(element.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        Times = element.GetProperty("times").EnumerateArray()
            .Select(t => TimeOnly.ParseExact(t.GetString()!, "HH:mm", CultureInfo.InvariantCulture))
            .ToList(),
        Screen = element.GetProperty("screen").GetString(),
        Language = element.GetProperty("language").GetString(),
        SourceUrl = element.GetProperty("source_url").GetString(),
        SessionAttributes = element.GetProperty("session_attributes").EnumerateArray().Select(a => a.GetString()!).ToList(),
    };
}
