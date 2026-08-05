using MediQueue.Api.Health;
using MediQueue.Infrastructure;
using MediQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

// The bootstrap logger covers the window before the host exists. Without it, a
// failure during host construction — a bad connection string, a DI mistake —
// would be reported by nothing at all.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // The real logger, configured from appsettings so log levels and sinks are
    // an operational concern rather than a recompile.
    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    // The composition root, and the only place the API touches Infrastructure.
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services
        .AddHealthChecks()
        // Reports whether the API can actually reach PostgreSQL, not merely that
        // the process is up. A health check that is green while the database is
        // unreachable is worse than no health check.
        .AddDbContextCheck<MediQueueDbContext>("database");

    var app = builder.Build();

    // Migrate and seed before serving traffic. Development only: applying
    // migrations automatically is convenient locally and unacceptable in
    // production, where a schema change is a deliberate, reviewed step.
    if (app.Environment.IsDevelopment())
    {
        await using var scope = app.Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<MediQueueDbContext>()
            .Database.MigrateAsync();

        await scope.ServiceProvider
            .GetRequiredService<DatabaseSeeder>()
            .SeedAsync();
    }

    // One structured event per request carrying the method, path, status code
    // and elapsed time, in place of the several ASP.NET Core emits by default.
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        // The .NET 10 template generates the OpenAPI document but deliberately
        // ships no UI, so Scalar renders that same document. Development only —
        // the schema is an information-disclosure surface in production.
        app.MapOpenApi();
        app.MapScalarApiReference(options => options.WithTitle("MediQueue API"));
    }

    // HTTPS redirection is a deployment concern. Locally the API is served over
    // plain HTTP so that neither the demo nor the desktop clients need a trusted
    // development certificate; enabling it here only logs a warning per request,
    // because there is no HTTPS port to redirect to.
    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    app.UseAuthorization();

    app.MapControllers();
    app.MapMediQueueHealthChecks();

    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "MediQueue API terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
