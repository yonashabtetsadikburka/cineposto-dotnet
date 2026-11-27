using CinePosto.Domain.Abstractions;
using CinePosto.Domain.Models;
using CinePosto.Scraper;

namespace CinePosto.Scraper.Tests;

public sealed class BackdropEnricherTests
{
    private static readonly BackdropLookup Found = new(BackdropLookupStatus.Found, "https://example.test/a.jpg");
    private static readonly BackdropLookup NotFound = new(BackdropLookupStatus.NotFound);
    private static readonly BackdropLookup Failed = new(BackdropLookupStatus.Failed, FailureType: "HttpRequestException");
    private static readonly BackdropLookup Refused = new(BackdropLookupStatus.Refused, FailureType: "HttpRequestException");

    [Fact]
    public async Task Run_StoresWhatWasFoundAndWhatWasNot_ButNoFailure()
    {
        (FakeStore store, FakeProvider provider, string output) = await RunAsync(Found, NotFound, Failed, Found);

        Assert.Equal([(1, "https://example.test/a.jpg"), (2, null), (4, "https://example.test/a.jpg")], store.Saved);
        Assert.Equal(4, provider.Asked.Count);
        Assert.Equal("backdrops films=4 found=2 not_found=1 failed=1 last_failure=HttpRequestException", output.Trim());
    }

    [Fact]
    public async Task Run_StopsAtTheFirstRefusal()
    {
        (FakeStore store, FakeProvider provider, string output) = await RunAsync(Found, Refused, Found, Found);

        Assert.Equal(2, provider.Asked.Count);
        Assert.Single(store.Saved);
        Assert.EndsWith("failed=1 last_failure=HttpRequestException stopped=refused", output.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_StopsAfterThreeFailuresInARow()
    {
        (_, FakeProvider provider, string output) = await RunAsync(Failed, Failed, Failed, Found);

        Assert.Equal(3, provider.Asked.Count);
        Assert.EndsWith("stopped=failures", output.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_ASuccessResetsTheCountOfFailures()
    {
        (_, FakeProvider provider, string output) = await RunAsync(Failed, Failed, NotFound, Failed, Failed, Found);

        Assert.Equal(6, provider.Asked.Count);
        Assert.DoesNotContain("stopped", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_WithNothingToLookUp_AsksNothing()
    {
        (FakeStore store, FakeProvider provider, string output) = await RunAsync();

        Assert.Empty(provider.Asked);
        Assert.Empty(store.Saved);
        Assert.Equal("backdrops films=0 found=0 not_found=0 failed=0", output.Trim());
    }

    [Fact]
    public void MaxAge_IsWithinTheSixMonthsOfTheTmdbTerms()
    {
        Assert.InRange(BackdropEnricher.MaxAge, TimeSpan.FromDays(1), TimeSpan.FromDays(180));
    }

    private static async Task<(FakeStore Store, FakeProvider Provider, string Output)> RunAsync(params BackdropLookup[] lookups)
    {
        FakeStore store = new(lookups.Length);
        FakeProvider provider = new(lookups);
        StringWriter output = new();
        await new BackdropEnricher(store, provider, TimeProvider.System, output).RunAsync(CancellationToken.None);
        return (store, provider, output.ToString());
    }

    private sealed class FakeStore(int films) : IBackdropStore
    {
        public List<(int FilmId, string? Url)> Saved { get; } = [];

        public Task<int> ExpireBackdropsAsync(DateTimeOffset madeBefore, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyList<Film>> GetFilmsNeedingBackdropAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Film>>(
            Enumerable.Range(1, films).Select(id => new Film { Id = id, Title = $"Film {id}", TitleNormalized = $"Film {id}" }).ToList());

        public Task SaveBackdropAsync(int filmId, string? backdropUrl, DateTimeOffset madeAt, CancellationToken cancellationToken)
        {
            Saved.Add((filmId, backdropUrl));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProvider(BackdropLookup[] lookups) : IBackdropProvider
    {
        public List<string> Asked { get; } = [];

        public Task<BackdropLookup> FindAsync(string title, CancellationToken cancellationToken)
        {
            Asked.Add(title);
            return Task.FromResult(lookups[Asked.Count - 1]);
        }
    }
}
