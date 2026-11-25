using CinePosto.Domain.Abstractions;
using CinePosto.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CinePosto.Infrastructure;

/// <summary>Registers the persistence services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the SQLite context and the stores that read and write the catalog.</summary>
    public static IServiceCollection AddCinePostoPersistence(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.AddDbContext<CinePostoDbContext>(options => options.UseSqlite(CinePostoDatabase.BuildConnectionString(databasePath)));
        services.AddScoped<SqliteShowtimeStore>();
        services.AddScoped<IShowtimeReader>(provider => provider.GetRequiredService<SqliteShowtimeStore>());
        services.AddScoped<IScrapeResultWriter>(provider => provider.GetRequiredService<SqliteShowtimeStore>());
        return services;
    }
}
