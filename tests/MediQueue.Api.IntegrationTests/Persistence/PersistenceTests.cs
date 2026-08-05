using System.Text;
using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Specialties;
using MediQueue.Domain.Users;
using MediQueue.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediQueue.Api.IntegrationTests.Persistence;

/// <summary>
/// The mapping tests, aimed squarely at the things that fail <em>silently</em>.
/// A converter that drops a value, a unique index that was never created, a
/// concurrency token that is not actually checked — none of these break a build
/// or fail a domain test. They surface as corrupt data months later.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    // Every test makes its own TAJ and username so they can share one database
    // without colliding on the unique indexes.
    private static string UniqueTaj() => Random.Shared.Next(100_000_000, 999_999_999).ToString();

    private static TajNumber ATaj()
    {
        var digits = UniqueTaj();
        return TajNumber.Create($"{digits[..3]}-{digits[3..6]}-{digits[6..]}");
    }

    [Fact]
    public async Task A_patient_survives_a_round_trip_with_both_value_objects_intact()
    {
        // Proves two things at once that nothing else does: EF can materialise
        // through the private constructor, and both converters work in both
        // directions.
        var taj = ATaj();
        var patient = Patient.Create(
            PatientName.Create("Kovács Anna"),
            "1052 Budapest, Váci utca 12.",
            taj,
            Now);

        await using (var write = postgres.CreateContext())
        {
            write.Patients.Add(patient);
            await write.SaveChangesAsync();
        }

        await using var read = postgres.CreateContext();
        var loaded = await read.Patients.SingleAsync(candidate => candidate.Id == patient.Id);

        loaded.FullName.ShouldBe(PatientName.Create("Kovács Anna"));
        loaded.FullName.Value.ShouldBe("Kovács Anna");
        loaded.Taj.ShouldBe(taj);
        loaded.Taj.Digits.Length.ShouldBe(9);
        loaded.Taj.ToString().ShouldBe(taj.ToString());
        loaded.Address.ShouldBe("1052 Budapest, Váci utca 12.");
        loaded.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Two_patients_cannot_share_a_TAJ_number()
    {
        // The unique index is what makes a returning patient reuse their record
        // rather than quietly becoming a second person with the same identity.
        var taj = ATaj();

        await using var database = postgres.CreateContext();
        database.Patients.Add(Patient.Create(PatientName.Create("Nagy Péter"), "Budapest", taj, Now));
        await database.SaveChangesAsync();

        database.Patients.Add(Patient.Create(PatientName.Create("Más Ember"), "Debrecen", taj, Now));

        var exception = await Should.ThrowAsync<DbUpdateException>(() => database.SaveChangesAsync());

        exception.InnerException.ShouldBeOfType<PostgresException>()
            .SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_name_reaches_the_database_composed_however_it_was_typed()
    {
        // "á" can arrive as one character or as "a" plus a combining acute, and
        // macOS produces the second form. If canonicalisation stopped at the
        // domain boundary, the two spellings would be different bytes in the
        // column and the same patient could be stored twice.
        // Distinct from every other test's data: the database is shared, and the
        // round-trip test also stores a "Kovács Anna". A generated suffix is not
        // an option here — PatientName rejects digits, correctly.
        var composed = "Ürmösné Ódor Ágnes";
        var decomposed = composed.Normalize(NormalizationForm.FormD);
        decomposed.ShouldNotBe(composed, "the test proves nothing unless the two forms really differ");

        var patient = Patient.Create(PatientName.Create(decomposed), "Budapest", ATaj(), Now);

        await using (var write = postgres.CreateContext())
        {
            write.Patients.Add(patient);
            await write.SaveChangesAsync();
        }

        await using var read = postgres.CreateContext();

        // Queried with the composed spelling, matched in SQL, not in memory.
        var found = await read.Patients
            .Where(candidate => candidate.FullName == PatientName.Create(composed))
            .ToListAsync();

        found.ShouldHaveSingleItem().Id.ShouldBe(patient.Id);

        var storedBytes = await read.Database
            .SqlQuery<int>($@"SELECT octet_length(""FullName"") AS ""Value"" FROM ""Patients"" WHERE ""Id"" = {patient.Id}")
            .SingleAsync();

        storedBytes.ShouldBe(Encoding.UTF8.GetByteCount(composed));
    }

    [Fact]
    public async Task A_soft_deleted_visit_disappears_from_queries_unless_they_opt_out()
    {
        var (_, visit) = await ASavedVisitAsync();

        await using (var delete = postgres.CreateContext())
        {
            var loaded = await delete.Visits.SingleAsync(candidate => candidate.Id == visit.Id);
            loaded.SoftDelete(Guid.CreateVersion7(Now), Now);
            await delete.SaveChangesAsync();
        }

        await using var read = postgres.CreateContext();

        (await read.Visits.AnyAsync(candidate => candidate.Id == visit.Id)).ShouldBeFalse();

        // The audit query in P5 needs deleted rows, and this is how it gets them.
        (await read.Visits.IgnoreQueryFilters().AnyAsync(candidate => candidate.Id == visit.Id))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Two_doctors_calling_in_the_same_patient_lose_the_race_deterministically()
    {
        // The state machine cannot catch this: both requests read Waiting, and
        // each transition looks legal on its own. xmin is what makes the second
        // write fail instead of silently winning.
        var (_, visit) = await ASavedVisitAsync();

        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();

        var firstCopy = await first.Visits.SingleAsync(candidate => candidate.Id == visit.Id);
        var secondCopy = await second.Visits.SingleAsync(candidate => candidate.Id == visit.Id);

        firstCopy.CallIn(Now.AddMinutes(1));
        await first.SaveChangesAsync();

        secondCopy.CallIn(Now.AddMinutes(1));

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var read = postgres.CreateContext();
        var settled = await read.Visits.SingleAsync(candidate => candidate.Id == visit.Id);
        settled.Status.ShouldBe(VisitStatus.InTreatment);
    }

    [Fact]
    public async Task An_assistant_row_carrying_a_specialty_refuses_to_materialise()
    {
        // The invariant lives in User's private constructor, which nothing could
        // reach in P1 because the factories make the state unrepresentable. EF
        // materialises through that constructor, so a corrupt row now reaches it
        // — which is what finally puts the guard under test.
        Guid specialtyId;
        await using (var setup = postgres.CreateContext())
        {
            var specialty = Specialty.Create($"Szakma {Guid.NewGuid():N}"[..30], Now);
            setup.Specialties.Add(specialty);
            await setup.SaveChangesAsync();
            specialtyId = specialty.Id;
        }

        var corruptId = Guid.CreateVersion7(Now);

        await using (var raw = postgres.OpenRawConnection())
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO "Users" ("Id", "Username", "FullName", "PasswordHash", "Role", "SpecialtyId", "IsActive")
                VALUES (@id, @username, 'Rossz Adat', 'hash', 1, @specialtyId, true)
                """,
                raw);
            insert.Parameters.AddWithValue("id", corruptId);
            insert.Parameters.AddWithValue("username", $"corrupt.{Guid.NewGuid():N}"[..20]);
            insert.Parameters.AddWithValue("specialtyId", specialtyId);
            await insert.ExecuteNonQueryAsync();
        }

        await using var read = postgres.CreateContext();

        // EF surfaces the constructor's exception as-is rather than wrapping it,
        // so the domain's own message is what a developer sees.
        var exception = await Should.ThrowAsync<DomainException>(
            () => read.Users.SingleAsync(candidate => candidate.Id == corruptId));

        exception.Message.ShouldBe("An assistant must not belong to a specialty.");
    }

    private async Task<(Patient Patient, Visit Visit)> ASavedVisitAsync()
    {
        var patient = Patient.Create(PatientName.Create("Teszt Elek"), "Budapest", ATaj(), Now);
        var visit = Visit.Register(patient.Id, "Fejfájás", Now);

        Guid specialtyId;
        Guid doctorId;

        await using var database = postgres.CreateContext();

        var specialty = Specialty.Create($"Szakma {Guid.NewGuid():N}"[..30], Now);
        database.Specialties.Add(specialty);
        await database.SaveChangesAsync();
        specialtyId = specialty.Id;

        var doctor = User.CreateDoctor(
            $"doktor.{Guid.NewGuid():N}"[..20],
            "Dr. Teszt",
            "hash",
            specialtyId,
            Now);
        database.Users.Add(doctor);
        database.Patients.Add(patient);
        await database.SaveChangesAsync();
        doctorId = doctor.Id;

        visit.AssignToQueue(specialtyId, doctorId, Now);
        database.Visits.Add(visit);
        await database.SaveChangesAsync();

        return (patient, visit);
    }
}
