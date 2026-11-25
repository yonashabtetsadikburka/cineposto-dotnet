using CinePosto.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CinePosto.Infrastructure.Tests;

/// <summary>A migrated SQLite database in a temporary folder, deleted on dispose.</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    private TestDatabase(string directory, CinePostoDbContext context)
    {
        _directory = directory;
        Context = context;
        Store = new SqliteShowtimeStore(context);
    }

    public CinePostoDbContext Context { get; }

    public SqliteShowtimeStore Store { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cineposto-tests", Guid.NewGuid().ToString("N"));
        CinePostoDbContext context = CinePostoDatabase.CreateContext(Path.Combine(directory, "test.db"));
        await DatabaseInitializer.MigrateAsync(context, CancellationToken.None);
        return new TestDatabase(directory, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
