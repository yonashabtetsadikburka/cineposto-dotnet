using Microsoft.EntityFrameworkCore.Design;

namespace CinePosto.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> create the context without a startup project. Used for tooling only.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CinePostoDbContext>
{
    /// <inheritdoc />
    public CinePostoDbContext CreateDbContext(string[] args)
    {
        return new CinePostoDbContext(CinePostoDatabase.CreateOptions(Path.Combine(Path.GetTempPath(), "cineposto-design-time.db")));
    }
}
