using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    private string? _connectionString;

    /// <summary>
    /// Points this application at a database of its own.
    /// </summary>
    /// <remarks>
    /// The shared one accumulates whatever the other HTTP tests wrote, which is
    /// fine for tests that create their own data and assert on it. It is not
    /// fine for a test whose subject is what the application wrote <em>on its
    /// own</em> — "the seeder produced no audit entries" can only be asked of a
    /// database nothing else has touched.
    /// </remarks>
    /// <param name="connectionString">An empty database from the fixture.</param>
    /// <returns>This factory, for chaining.</returns>
    public MediQueueApiFactory WithOwnDatabase(string connectionString)
    {
        _connectionString = connectionString;

        return this;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development, so migrations run and the practice is seeded — the tests
        // sign in as the seeded accounts.
        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("ConnectionStrings:Default", _connectionString ?? postgres.ApiConnectionString);

        builder.ConfigureServices(services =>
        {
            // Endpoints that exist only to make the cross-cutting rules
            // observable. They live in this assembly, so no build of the API
            // can ever contain them.
            services
                .AddControllers()
                .AddApplicationPart(typeof(TestOnlyController).Assembly);

            // Same arrangement, one transport along: a hub that exists only to
            // make the DI scope of a hub invocation observable.
            services.AddSingleton<IStartupFilter, ScopeProbeHubStartupFilter>();

            if (_clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_clock);
            }
        });
    }

    private TimeProvider? _clock;

    /// <summary>Substitutes the clock the whole application reads.</summary>
    /// <param name="clock">The clock, usually a <c>FakeTimeProvider</c>.</param>
    /// <returns>This factory, for chaining.</returns>
    public MediQueueApiFactory WithClock(TimeProvider clock)
    {
        _clock = clock;

        return this;
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
