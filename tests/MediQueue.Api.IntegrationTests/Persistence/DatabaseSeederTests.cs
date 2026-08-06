using MediQueue.Domain.Users;
using MediQueue.Domain.Visits;
using MediQueue.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MediQueue.Api.IntegrationTests.Persistence;

/// <summary>
/// The seeder runs on every Development start-up, so "it is idempotent" is not
/// a nicety — a second run that duplicated the practice would break the unique
/// indexes and take the application down on restart.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DatabaseSeederTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset SeedTime = new(2026, 8, 5, 6, 30, 0, TimeSpan.Zero);

    private static DatabaseSeeder SeederFor(MediQueueDbContext database) =>
        new(
            database,
            new PasswordHasher<User>(),
            new FakeTimeProvider(SeedTime),
            NullLogger<DatabaseSeeder>.Instance);

    [Fact]
    public async Task Seeding_an_empty_database_produces_the_demo_practice()
    {
        await using var database = await postgres.CreateIsolatedDatabaseAsync();

        await SeederFor(database).SeedAsync();

        (await database.Specialties.CountAsync()).ShouldBe(4);
        (await database.Users.CountAsync()).ShouldBe(7);
        (await database.Patients.CountAsync()).ShouldBe(6);
        (await database.Visits.CountAsync()).ShouldBe(6);

        (await database.Users.CountAsync(user => user.Role == UserRole.Doctor)).ShouldBe(5);
        (await database.Users.CountAsync(user => user.Role == UserRole.Doctor && user.IsActive)).ShouldBe(4);

        // Exactly one specialty has no active doctor, which is what makes the
        // "nobody available" path reachable at all.
        var specialtiesWithNoActiveDoctor = await database.Specialties
            .Where(specialty => !database.Users.Any(user =>
                user.SpecialtyId == specialty.Id && user.Role == UserRole.Doctor && user.IsActive))
            .Select(specialty => specialty.Name)
            .ToListAsync();

        specialtiesWithNoActiveDoctor.ShouldBe(["Reumatológia"]);
        (await database.Users.CountAsync(user => user.Role == UserRole.Assistant)).ShouldBe(2);

        // At least two doctors must share a specialty, or the assignment strategy
        // has nothing to choose between during the demo.
        var doctorsPerSpecialty = await database.Users
            .Where(user => user.Role == UserRole.Doctor && user.IsActive)
            .GroupBy(user => user.SpecialtyId)
            .Select(group => group.Count())
            .ToListAsync();

        doctorsPerSpecialty.ShouldContain(count => count >= 2);
    }

    [Fact]
    public async Task Every_visit_status_appears_so_the_demo_has_something_to_show()
    {
        await using var database = await postgres.CreateIsolatedDatabaseAsync();

        await SeederFor(database).SeedAsync();

        var statuses = await database.Visits
            .IgnoreQueryFilters()
            .Select(visit => visit.Status)
            .Distinct()
            .ToListAsync();

        statuses.ShouldBe(Enum.GetValues<VisitStatus>(), ignoreOrder: true);
    }

    [Fact]
    public async Task Arrival_times_are_spread_across_the_morning_rather_than_identical()
    {
        // A queue where everyone arrived at the same instant cannot demonstrate
        // arrival ordering, and looks wrong on screen.
        await using var database = await postgres.CreateIsolatedDatabaseAsync();

        await SeederFor(database).SeedAsync();

        var arrivals = await database.Visits
            .IgnoreQueryFilters()
            .Select(visit => visit.RegisteredAt)
            .ToListAsync();

        arrivals.Distinct().Count().ShouldBe(arrivals.Count);
        (arrivals.Max() - arrivals.Min()).ShouldBeGreaterThan(TimeSpan.FromMinutes(30));

        // Anchored to the injected clock's day, not to whenever the test runs.
        arrivals.ShouldAllBe(arrival => arrival.UtcDateTime.Date == SeedTime.UtcDateTime.Date);
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var database = await postgres.CreateIsolatedDatabaseAsync();
        var seeder = SeederFor(database);

        await seeder.SeedAsync();
        var before = await CountsAsync(database);

        await seeder.SeedAsync();
        var after = await CountsAsync(database);

        after.ShouldBe(before);
    }

    [Fact]
    public async Task Every_seeded_account_signs_in_with_the_documented_demo_password()
    {
        // If the hash were wrong, nothing would fail until the live demo.
        await using var database = await postgres.CreateIsolatedDatabaseAsync();
        await SeederFor(database).SeedAsync();

        var hasher = new PasswordHasher<User>();

        foreach (var user in await database.Users.ToListAsync())
        {
            hasher.VerifyHashedPassword(user, user.PasswordHash, DatabaseSeeder.DemoPassword)
                .ShouldBe(PasswordVerificationResult.Success, $"account '{user.Username}' cannot sign in");
        }
    }

    private static async Task<(int Specialties, int Users, int Patients, int Visits)> CountsAsync(
        MediQueueDbContext database) =>
        (await database.Specialties.CountAsync(),
         await database.Users.CountAsync(),
         await database.Patients.CountAsync(),
         await database.Visits.IgnoreQueryFilters().CountAsync());
}
