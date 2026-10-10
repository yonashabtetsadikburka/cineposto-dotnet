using CinePosto.Domain.Text;

namespace CinePosto.Domain.Tests.Parity;

public class TitleMatcherTests
{
    public static IEnumerable<object?[]> FuzzyMatchCases() =>
        TsvParityLoader.Load("fuzzy_match.tsv")
            .Select(row => new object?[] { row[0], row[1], bool.Parse(row[2]!) });

    [Theory]
    [MemberData(nameof(FuzzyMatchCases))]
    public void IsFuzzyMatch_MatchesLegacyOutcome(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, TitleMatcher.IsFuzzyMatch(a!, b!));
    }

    [Fact]
    public void AreEqual_IdenticalStrings_Match()
    {
        Assert.True(TitleMatcher.AreEqual("Dune", "Dune"));
    }

    [Fact]
    public void AreEqual_CaseInsensitive_Match()
    {
        Assert.True(TitleMatcher.AreEqual("DUNE", "dune"));
    }

    [Fact]
    public void AreEqual_MatchesAfterNormalization()
    {
        Assert.True(TitleMatcher.AreEqual("Dune (2021)", "Dune"));
    }

    [Fact]
    public void AreEqual_CompletelyDifferentTitles_NoMatch()
    {
        Assert.False(TitleMatcher.AreEqual("Dune", "Barbie"));
    }

    [Fact]
    public void IsFuzzyMatch_IdenticalStrings_Match()
    {
        Assert.True(TitleMatcher.IsFuzzyMatch("Dune", "Dune"));
    }

    [Fact]
    public void IsFuzzyMatch_CaseInsensitive_Match()
    {
        Assert.True(TitleMatcher.IsFuzzyMatch("DUNE", "dune"));
    }

    [Fact]
    public void IsFuzzyMatch_MatchesAfterNormalization()
    {
        Assert.True(TitleMatcher.IsFuzzyMatch("Dune (2021)", "Dune"));
    }

    [Fact]
    public void IsFuzzyMatch_Substring_Match()
    {
        Assert.True(TitleMatcher.IsFuzzyMatch("Dune", "Dune Part Two"));
    }

    [Fact]
    public void IsFuzzyMatch_SmallTypo_Matches()
    {
        Assert.True(TitleMatcher.IsFuzzyMatch("Oppenheimer", "Openheimer"));
    }

    [Fact]
    public void IsFuzzyMatch_CompletelyDifferentTitles_NoMatch()
    {
        Assert.False(TitleMatcher.IsFuzzyMatch("Oppenheimer", "Barbie"));
    }

    [Fact]
    public void IsFuzzyMatch_ShortDifferentTitles_NoMatch()
    {
        // distance("dune", "ring") is 3, above max(2, 6 // 4).
        Assert.False(TitleMatcher.IsFuzzyMatch("Dune", "Ring"));
    }

    [Fact]
    public void IsFuzzyMatch_IsSymmetric()
    {
        Assert.Equal(TitleMatcher.IsFuzzyMatch("Oppenheimer", "Openheimer"), TitleMatcher.IsFuzzyMatch("Openheimer", "Oppenheimer"));
    }

    [Fact]
    public void IsFuzzyMatch_BothEmpty_Match()
    {
        Assert.True(TitleMatcher.IsFuzzyMatch(string.Empty, string.Empty));
    }
}
