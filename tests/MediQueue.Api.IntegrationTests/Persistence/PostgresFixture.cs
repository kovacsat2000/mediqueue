using MediQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    /// <summary>Starts the container and applies the migrations.</summary>
    public async Task InitializeAsync()
    {
        var startedAt = DateTimeOffset.UtcNow;

        await _container.StartAsync();

        await using var database = CreateContext();
        await database.Database.MigrateAsync();

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
        var databaseName = $"mq_{Guid.NewGuid():N}"[..20];

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($@"CREATE DATABASE ""{databaseName}""", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;

        var database = CreateContext(connectionString);
        await database.Database.MigrateAsync();

        return database;
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
