using MediQueue.Api.Health;
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
    builder.Services.AddHealthChecks();

    var app = builder.Build();

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
