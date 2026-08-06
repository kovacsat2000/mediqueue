using MediQueue.Application.Abstractions;
using MediQueue.Infrastructure.Auditing;
using MediQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace MediQueue.Api.IntegrationTests.Persistence;

/// <summary>
/// A real PostgreSQL 17 container, started once and shared by every persistence
/// test.
/// </summary>
/// <remarks>
/// Deliberately not the in-memory provider. In-memory hides exactly the things
/// these tests exist to check — unique violations, the concurrency token,
/// column types, and whether a value converter survives a round trip through
/// actual SQL. A test suite that passes against a fake and fails against
/// PostgreSQL is worse than no suite.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    // The same image tag docker-compose.yml uses, so the tests and the demo run
    // against the same database.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>How long the container took to start, reported by the suite.</summary>
    public TimeSpan StartupDuration { get; private set; }

    /// <summary>
    /// A database of its own for the HTTP tests, left empty so the application's
    /// own start-up migrates and seeds it.
    /// </summary>
    /// <remarks>
    /// It cannot share the mapping tests' database. The seeder stops as soon as
    /// any specialty exists, so a shared schema would leave the practice
    /// unseeded and every sign-in test would fail for the wrong reason.
    /// </remarks>
    public string ApiConnectionString { get; private set; } = string.Empty;

    /// <summary>Starts the container and applies the migrations.</summary>
    public async Task InitializeAsync()
    {
        var startedAt = DateTimeOffset.UtcNow;

        await _container.StartAsync();

        await using var database = CreateContext();
        await database.Database.MigrateAsync();

        ApiConnectionString = await CreateEmptyDatabaseAsync().ConfigureAwait(false);

        StartupDuration = DateTimeOffset.UtcNow - startedAt;
    }

    /// <summary>A context over the shared, already-migrated database.</summary>
    public MediQueueDbContext CreateContext() => CreateContext(_container.GetConnectionString());

    /// <summary>
    /// A context over a brand new, migrated, empty database inside the same
    /// container — for tests that need to observe an empty schema, such as the
    /// seeder.
    /// </summary>
    public async Task<MediQueueDbContext> CreateIsolatedDatabaseAsync()
    {
        var database = CreateContext(await CreateEmptyDatabaseAsync().ConfigureAwait(false));
        await database.Database.MigrateAsync();

        return database;
    }

    /// <summary>
    /// A context over its own empty database with the audit interceptor wired,
    /// and with the actor and the clock under the test's control.
    /// </summary>
    /// <remarks>
    /// The other factories deliberately leave the interceptor out, so the
    /// mapping tests observe rows without an audit trail forming behind them.
    /// This one exists for the tests whose subject <em>is</em> the interceptor:
    /// it is the only way to drive a missing actor, or a second save after a
    /// failed one, without an HTTP request in the way.
    /// </remarks>
    /// <param name="actor">Who the interceptor should record, or <c>null</c> for none.</param>
    /// <param name="suppression">The opt-out, so a test can seed without auditing.</param>
    /// <param name="clock">The clock the entries are stamped from.</param>
    /// <param name="logger">Supplied when a test asserts on what was logged.</param>
    /// <returns>The migrated, auditing context.</returns>
    public async Task<MediQueueDbContext> CreateAuditedDatabaseAsync(
        Guid? actor,
        AuditSuppression? suppression = null,
        TimeProvider? clock = null,
        ILogger<AuditSaveChangesInterceptor>? logger = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(actor);
        currentUser.IsAuthenticated.Returns(actor is not null);

        var interceptor = new AuditSaveChangesInterceptor(
            currentUser,
            suppression ?? new AuditSuppression(),
            clock ?? TimeProvider.System,
            logger ?? NullLogger<AuditSaveChangesInterceptor>.Instance);

        var database = new MediQueueDbContext(
            new DbContextOptionsBuilder<MediQueueDbContext>()
                .UseNpgsql(await CreateEmptyDatabaseAsync().ConfigureAwait(false))
                .AddInterceptors(interceptor)
                .Options);

        await database.Database.MigrateAsync();

        return database;
    }

    /// <summary>Creates an empty database in the container and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var databaseName = $"mq_{Guid.NewGuid():N}"[..20];

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($@"CREATE DATABASE ""{databaseName}""", admin);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;
    }

    /// <summary>Opens a raw connection, for inserting rows the domain would refuse to create.</summary>
    public NpgsqlConnection OpenRawConnection()
    {
        var connection = new NpgsqlConnection(_container.GetConnectionString());
        connection.Open();
        return connection;
    }

    /// <summary>Stops and removes the container.</summary>
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static MediQueueDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<MediQueueDbContext>()
            .UseNpgsql(connectionString)
            .Options);
}

/// <summary>
/// Binds every persistence test class to the one container, so it starts once
/// per run rather than once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name test classes reference.</summary>
    public const string Name = "postgres";
}
