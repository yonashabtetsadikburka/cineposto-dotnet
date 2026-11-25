using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CinePosto.Infrastructure.Persistence;

/// <summary>Creates the database when missing and applies pending migrations.</summary>
public static class DatabaseInitializer
{
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    /// <summary>
    /// Applies pending migrations and switches the file to write-ahead logging, so the Api can read while the
    /// scraper writes. Retries when another process holds the database lock.
    /// </summary>
    public static async Task MigrateAsync(CinePostoDbContext context, CancellationToken cancellationToken, int maxAttempts = 5)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
