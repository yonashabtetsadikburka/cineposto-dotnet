using CinePosto.Api;

namespace CinePosto.Api.Tests;

public sealed class AdminOptionsTests
{
    private const string Token = "0123456789abcdef";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("0123456789abcde")]
    public void IsValidToken_RejectsMissingOrShortTokens(string? token)
    {
        Assert.False(AdminOptions.IsValidToken(token));
    }

    [Fact]
    public void IsValidToken_AcceptsATokenOfTheMinimumLength()
    {
        Assert.True(AdminOptions.IsValidToken(Token));
    }

    [Fact]
    public void Matches_AcceptsOnlyTheConfiguredToken()
    {
        AdminOptions options = new(Token);

        Assert.True(options.Matches(Token));
        Assert.False(options.Matches("0123456789abcdee"));
        Assert.False(options.Matches(Token + "x"));
        Assert.False(options.Matches(string.Empty));
        Assert.False(options.Matches(null));
    }

    [Theory]
    [InlineData(null, "8080")]
    [InlineData("", "8080")]
    [InlineData("9090", "9090")]
    [InlineData("9090;9091", "9090")]
    [InlineData("not-a-port", "8080")]
    [InlineData("70000", "8080")]
    public void HealthProbe_ResolvesThePortFromTheEnvironmentValue(string? configured, string expected)
    {
        Assert.Equal(expected, HealthProbe.ResolvePort(configured));
    }
}
