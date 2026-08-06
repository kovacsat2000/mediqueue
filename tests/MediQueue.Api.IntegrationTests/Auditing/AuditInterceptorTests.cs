using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Domain.Auditing;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Specialties;
using MediQueue.Domain.Users;
using MediQueue.Domain.Visits;
using MediQueue.Infrastructure.Auditing;
using MediQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MediQueue.Api.IntegrationTests.Auditing;

/// <summary>
/// What the interceptor writes, against real PostgreSQL.
/// </summary>
/// <remarks>
/// Driven through a <c>DbContext</c> rather than over HTTP, because the rules
/// under test here are about the change tracker: a missing actor, a save that
/// follows a failed one, a property that did not move. Those are unreachable
/// from a request, and an HTTP test asserting them would really be asserting
/// something else.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class AuditInterceptorTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Actor = Guid.CreateVersion7(Now);

    private static Task<List<AuditEntry>> EntriesAsync(MediQueueDbContext database) =>
        database.AuditEntries
            .Include(entry => entry.Changes)
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync();

    /// <summary>A patient and their visit, written by one save.</summary>
    private static async Task<(Patient Patient, Visit Visit)> ArriveAsync(MediQueueDbContext database)
    {
        var patient = Patient.Create(
            PatientName.Create("Kovács Anna"),
            "1052 Budapest, Váci utca 12.",
            TajNumber.Create("123-456-788"),
            Now);

        var visit = Visit.Register(patient.Id, "Fejfájás", Now);

        database.Patients.Add(patient);
        database.Visits.Add(visit);
        await database.SaveChangesAsync();

        return (patient, visit);
    }

    /// <summary>
    /// A specialty and a doctor a visit can legally be routed to, written with
    /// auditing suppressed so they are background rather than subject.
    /// </summary>
    /// <remarks>
    /// Every foreign key in this schema is RESTRICT, so a visit cannot be
    /// assigned to a specialty and a doctor that do not exist. Creating them
    /// under suppression keeps the entry counts in these tests about the action
    /// each one is actually testing.
    /// </remarks>
    private static async Task<(Guid SpecialtyId, Guid DoctorId)> APlaceToRouteToAsync(
        MediQueueDbContext database,
        AuditSuppression suppression)
    {
        var specialty = Specialty.Create("Belgyógyászat", Now);
        var doctor = User.CreateDoctor("kovacs.istvan", "Dr. Kovács István", "hash", specialty.Id, Now);

        database.Specialties.Add(specialty);
        database.Users.Add(doctor);

        using (suppression.Suppress())
        {
            await database.SaveChangesAsync();
        }

        return (specialty.Id, doctor.Id);
    }

    [Fact]
    public async Task One_business_action_produces_one_entry_per_entity_it_touched()
    {
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        var (patient, visit) = await ArriveAsync(database);

        var entries = await EntriesAsync(database);

        entries.Count.ShouldBe(2);
        entries.ShouldAllBe(entry => entry.Action == AuditAction.Create);
        entries.ShouldAllBe(entry => entry.UserId == Actor);

        entries.Select(entry => entry.EntityType).ShouldBe(["Patient", "Visit"], ignoreOrder: true);
        entries.Select(entry => entry.EntityId).ShouldBe([patient.Id, visit.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Every_entry_carries_the_patient_it_concerns()
    {
        // Denormalised so "everything that happened to this patient" is one
        // indexed query rather than a join whose shape depends on the entity
        // type in the row.
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        var (patient, _) = await ArriveAsync(database);

        (await EntriesAsync(database)).ShouldAllBe(entry => entry.PatientId == patient.Id);
    }

    [Fact]
    public async Task A_change_to_something_that_is_not_about_a_patient_carries_no_patient()
    {
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        database.Specialties.Add(Specialty.Create("Belgyógyászat", Now));
        await database.SaveChangesAsync();

        (await EntriesAsync(database)).ShouldHaveSingleItem().PatientId.ShouldBeNull();
    }

    [Fact]
    public async Task Value_objects_are_recorded_the_way_the_domain_spells_them()
    {
        // The values come from the PropertyEntry, so a converted property gives
        // its model value rather than its column value. A TAJ stored as nine
        // bare digits must still read as 123-456-788 in the log.
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        await ArriveAsync(database);

        var patientEntry = (await EntriesAsync(database)).Single(entry => entry.EntityType == "Patient");

        patientEntry.Changes.Single(change => change.FieldName == "Taj").NewValue.ShouldBe("123-456-788");
        patientEntry.Changes.Single(change => change.FieldName == "FullName").NewValue.ShouldBe("Kovács Anna");
    }

    [Fact]
    public async Task A_diagnosis_is_recorded_and_marked_sensitive()
    {
        var suppression = new AuditSuppression();
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor, suppression);
        var (specialtyId, doctorId) = await APlaceToRouteToAsync(database, suppression);

        var (_, visit) = await ArriveAsync(database);
        visit.AssignToQueue(specialtyId, doctorId, Now.AddMinutes(1));
        visit.CallIn(Now.AddMinutes(2));
        await database.SaveChangesAsync();

        visit.RecordDiagnosis("Migrén");
        await database.SaveChangesAsync();

        var change = (await EntriesAsync(database))
            .SelectMany(entry => entry.Changes)
            .Single(candidate => candidate.FieldName == "Diagnosis");

        change.NewValue.ShouldBe("Migrén");

        // The flag is what carries the redaction rule to the reader. It comes
        // from the [SensitiveAudit] attribute on Visit.Diagnosis, so the rule
        // travels with the property rather than being restated at the query.
        change.IsSensitive.ShouldBeTrue();
    }

    [Fact]
    public async Task Only_the_diagnosis_is_sensitive_within_the_same_entry()
    {
        // Sensitivity is per field. An assistant is entitled to see that a visit
        // changed and who changed it; marking the whole entry would hide the
        // ordinary fields along with the clinical one.
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        var (_, visit) = await ArriveAsync(database);

        (await EntriesAsync(database))
            .Single(entry => entry.EntityType == "Visit")
            .Changes
            .ShouldAllBe(change => !change.IsSensitive);
    }

    [Fact]
    public async Task A_soft_delete_is_recorded_as_a_deletion_rather_than_an_update()
    {
        // The log says what happened to the record, not what happened to a
        // column. "Withdrew this visit" is the fact worth keeping.
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        var (_, visit) = await ArriveAsync(database);

        visit.SoftDelete(Actor, Now.AddMinutes(5));
        await database.SaveChangesAsync();

        var entry = (await EntriesAsync(database)).Last();

        entry.Action.ShouldBe(AuditAction.Delete);
        entry.EntityId.ShouldBe(visit.Id);
        entry.Changes.Select(change => change.FieldName)
            .ShouldContain(nameof(Visit.IsDeleted));
    }

    [Fact]
    public async Task One_update_touching_two_properties_is_one_entry_with_two_changes()
    {
        // Not two entries: it was one action.
        var suppression = new AuditSuppression();
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor, suppression);
        var (specialtyId, doctorId) = await APlaceToRouteToAsync(database, suppression);

        var (_, visit) = await ArriveAsync(database);

        visit.AssignToQueue(specialtyId, doctorId, Now.AddMinutes(1));
        visit.CallIn(Now.AddMinutes(2));
        await database.SaveChangesAsync();

        var update = (await EntriesAsync(database)).Last();

        update.Action.ShouldBe(AuditAction.Update);

        // Status, SpecialtyId, DoctorId, QueuedAt, CalledInAt — and nothing else.
        update.Changes.Select(change => change.FieldName).ShouldBe(
            [
                nameof(Visit.SpecialtyId),
                nameof(Visit.DoctorId),
                nameof(Visit.Status),
                nameof(Visit.QueuedAt),
                nameof(Visit.CalledInAt),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task A_property_that_did_not_move_gets_no_row()
    {
        // An update that buried its one real change under a dozen unchanged
        // fields would grow the log without making it more informative.
        var suppression = new AuditSuppression();
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor, suppression);
        var (specialtyId, doctorId) = await APlaceToRouteToAsync(database, suppression);

        var (_, visit) = await ArriveAsync(database);

        visit.AssignToQueue(specialtyId, doctorId, Now.AddMinutes(1));
        await database.SaveChangesAsync();

        var update = (await EntriesAsync(database)).Last();
        var fields = update.Changes.Select(change => change.FieldName).ToList();

        fields.ShouldNotContain(nameof(Visit.Complaint));
        fields.ShouldNotContain(nameof(Visit.PatientId));
        fields.ShouldNotContain(nameof(Visit.RegisteredAt));
        fields.ShouldNotContain(nameof(Visit.Id));
    }

    [Fact]
    public async Task Saving_nothing_writes_nothing()
    {
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        await database.SaveChangesAsync();

        (await EntriesAsync(database)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_actor_produces_an_entry_and_a_warning_rather_than_silence()
    {
        // The rule that matters most in this file. D-37's failure mode was every
        // actor silently becoming null; "no user, no entry" would have turned
        // that into an audit log that was silently empty, which is worse.
        var logger = new CapturingLogger<AuditSaveChangesInterceptor>();

        await using var database = await postgres.CreateAuditedDatabaseAsync(actor: null, logger: logger);

        await ArriveAsync(database);

        var entries = await EntriesAsync(database);

        entries.Count.ShouldBe(2);
        entries.ShouldAllBe(entry => entry.UserId == null);

        logger.Warnings.ShouldNotBeEmpty();
        logger.Warnings.ShouldContain(message => message.Contains("no actor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_explicitly_suppressed_save_writes_no_entries_at_all()
    {
        // The seeder's opt-out. Deliberately not "skip when the user is null":
        // that inference is what would empty the log the day identity broke.
        var suppression = new AuditSuppression();

        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor, suppression);

        using (suppression.Suppress())
        {
            await ArriveAsync(database);
        }

        (await EntriesAsync(database)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Auditing_resumes_once_the_suppression_is_disposed()
    {
        var suppression = new AuditSuppression();

        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor, suppression);

        using (suppression.Suppress())
        {
            database.Specialties.Add(Specialty.Create("Belgyógyászat", Now));
            await database.SaveChangesAsync();
        }

        await ArriveAsync(database);

        (await EntriesAsync(database)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_audit_trail_never_audits_itself_when_a_save_is_retried()
    {
        // The scenario the type exclusion actually defends. A failed save leaves
        // the entries it created tracked as Added; without the exclusion the
        // retry would audit them, and each pass would add more.
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        // A visit whose patient does not exist violates the foreign key.
        var orphan = Visit.Register(Guid.CreateVersion7(Now), "Fejfájás", Now);
        database.Visits.Add(orphan);

        await Should.ThrowAsync<DbUpdateException>(() => database.SaveChangesAsync());

        // The audit entries from the failed attempt are still tracked as Added.
        database.ChangeTracker.Entries<AuditEntry>()
            .Count(entry => entry.State == EntityState.Added)
            .ShouldBe(1);

        // Give the visit a patient and save again.
        database.Patients.Add(Patient.Create(
            PatientName.Create("Kovács Anna"),
            "1052 Budapest, Váci utca 12.",
            TajNumber.Create("123-456-788"),
            Now));

        database.Entry(orphan).Property(nameof(Visit.PatientId)).CurrentValue =
            database.ChangeTracker.Entries<Patient>().Single().Entity.Id;

        await database.SaveChangesAsync();

        var entries = await EntriesAsync(database);

        entries.ShouldNotBeEmpty();
        entries.Select(entry => entry.EntityType)
            .ShouldNotContain(nameof(AuditEntry), "the audit trail must never describe itself");
        entries.Select(entry => entry.EntityType)
            .ShouldNotContain(nameof(AuditFieldChange), "the audit trail must never describe itself");
    }

    [Fact]
    public async Task Entries_are_stamped_from_the_injected_clock()
    {
        var clock = new FakeTimeProvider(Now);

        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor, clock: clock);

        await ArriveAsync(database);

        (await EntriesAsync(database)).ShouldAllBe(entry => entry.OccurredAt == Now);
    }

    [Fact]
    public async Task A_synchronous_save_is_audited_too()
    {
        // The system's only write path is asynchronous, but an unaudited
        // SaveChanges() would be exactly the silent hole this phase exists to
        // close, so both overloads are intercepted.
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        database.Specialties.Add(Specialty.Create("Belgyógyászat", Now));
        database.SaveChanges();

        (await EntriesAsync(database)).ShouldHaveSingleItem().EntityType.ShouldBe(nameof(Specialty));
    }

    [Fact]
    public async Task A_user_is_audited_without_anybody_adding_them_to_a_list()
    {
        // Exclusion is by type, so a new entity is audited by default. Nothing
        // names User anywhere in the interceptor.
        await using var database = await postgres.CreateAuditedDatabaseAsync(Actor);

        database.Users.Add(User.CreateAssistant("horvath.anna", "Horváth Anna", "hash", Now));
        await database.SaveChangesAsync();

        (await EntriesAsync(database)).ShouldHaveSingleItem().EntityType.ShouldBe(nameof(User));
    }
}

/// <summary>Keeps what was logged, so a test can assert the warning happened.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _warnings = [];

    /// <summary>Everything logged at warning level or above.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (logLevel >= LogLevel.Warning)
        {
            _warnings.Add(formatter(state, exception));
        }
    }
}
