using CinePosto.Api;
using CinePosto.Api.Endpoints;
using CinePosto.Infrastructure;
using CinePosto.Infrastructure.Persistence;

// The runtime image has no curl, so the container healthcheck probes the app through the app itself.
if (args.Contains(HealthProbe.Flag))
{
    return await HealthProbe.RunAsync();
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// There is deliberately no default token: without a usable one the app refuses to start.
string? adminToken = builder.Configuration["ADMIN_TOKEN"];
if (!AdminOptions.IsValidToken(adminToken))
{
    Console.Error.WriteLine($"ADMIN_TOKEN is missing or shorter than {AdminOptions.MinimumTokenLength} characters. Refusing to start.");
    return 1;
}

string databasePath = builder.Configuration["CINEPOSTO_DB_PATH"] ?? "cineposto.db";

builder.Services.AddSingleton(new AdminOptions(adminToken!));
builder.Services.AddCinePostoPersistence(databasePath);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

WebApplication app = builder.Build();

// Create the database or apply pending migrations before serving traffic.
using (IServiceScope scope = app.Services.CreateScope())
{
    CinePostoDbContext db = scope.ServiceProvider.GetRequiredService<CinePostoDbContext>();
    await DatabaseInitializer.MigrateAsync(db, app.Lifetime.ApplicationStopping);
}

app.MapHealthChecks("/health");
app.MapCatalogEndpoints();

await app.RunAsync();
return 0;
