using CinePosto.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CinePosto.Api;

/// <summary>Reports healthy only when the database is reachable and fully migrated.</summary>
internal sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly CinePostoDbContext _db;

    public DatabaseHealthCheck(CinePostoDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                return HealthCheckResult.Unhealthy("database-unreachable");
            }

            IEnumerable<string> pending = await _db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
            return pending.Any() ? HealthCheckResult.Unhealthy("migrations-pending") : HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("database-error", ex);
        }
    }
}
