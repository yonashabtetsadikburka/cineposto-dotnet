using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CinePosto.Infrastructure.Persistence;

/// <summary>Builds connection settings and contexts for the SQLite database file.</summary>
public static class CinePostoDatabase
{
    private const int BusyTimeoutSeconds = 30;

    /// <summary>Builds the SQLite connection string for the given database file path.</summary>
    public static string BuildConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = BusyTimeoutSeconds,
            ForeignKeys = true,
        }.ToString();
    }

    /// <summary>Creates context options for the given database file path.</summary>
    public static DbContextOptions<CinePostoDbContext> CreateOptions(string databasePath)
    {
        return new DbContextOptionsBuilder<CinePostoDbContext>()
            .UseSqlite(BuildConnectionString(databasePath))
            .Options;
    }

    /// <summary>Creates a context for the given database file path, creating the parent folder when missing.</summary>
    public static CinePostoDbContext CreateContext(string databasePath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new CinePostoDbContext(CreateOptions(databasePath));
    }
}
