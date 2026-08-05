using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// Boots the real application against the test container.
/// </summary>
/// <remarks>
/// The application is started as it actually runs — real authentication, real
/// authorization, real exception handling, real controllers. Only the database
/// connection string is redirected. Substituting any of the middleware would
/// mean testing a different application from the one that ships.
/// </remarks>
public sealed class MediQueueApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development, so migrations run and the practice is seeded — the tests
        // sign in as the seeded accounts.
        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("ConnectionStrings:Default", postgres.ApiConnectionString);

        builder.ConfigureServices(services =>
        {
            // Endpoints that exist only to make the cross-cutting rules
            // observable. They live in this assembly, so no build of the API
            // can ever contain them.
            services
                .AddControllers()
                .AddApplicationPart(typeof(TestOnlyController).Assembly);
        });
    }

    /// <summary>Applies migrations and seeds, then hands back a client.</summary>
    public async Task<HttpClient> CreateReadyClientAsync()
    {
        var client = CreateClient();

        // Touching the app forces the host to build, which runs migrate + seed.
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MediQueueDbContext>().Database.MigrateAsync();

        return client;
    }
}
