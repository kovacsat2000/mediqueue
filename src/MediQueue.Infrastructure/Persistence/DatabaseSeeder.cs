using MediQueue.Domain.Patients;
using MediQueue.Domain.Specialties;
using MediQueue.Domain.Users;
using MediQueue.Domain.Visits;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// Fills an empty database with a practice that looks like a real morning.
/// Development only.
/// </summary>
/// <remarks>
/// <para>
/// The data is Hungarian because the evaluators are, and a demo full of
/// "Patient 1" reads like a test fixture rather than a working system.
/// Everything around it — identifiers, comments, log messages — stays English.
/// </para>
/// <para>
/// Timestamps are spread across a morning rather than all set to "now". A queue
/// where every patient arrived at the same instant makes arrival ordering
/// impossible to demonstrate and looks wrong on screen.
/// </para>
/// </remarks>
public sealed class DatabaseSeeder(
    MediQueueDbContext database,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider,
    ILogger<DatabaseSeeder> logger)
{
    /// <summary>
    /// The password every seeded account shares. A demo credential, documented
    /// in the README; the only reason it is acceptable in source control is that
    /// it unlocks nothing but a local container.
    /// </summary>
    public const string DemoPassword = "MediQueue123!";

    /// <summary>Seeds the database if it is empty. Running it again does nothing.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // One guard on one table is enough because the whole graph is written by
        // a single SaveChanges: either the practice exists or none of it does.
        if (await database.Specialties.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("Database already seeded; leaving it alone.");
            return;
        }

        // A plausible morning, anchored to today so the demo never shows a stale
        // date. The practice opens at 08:00.
        var opening = new DateTimeOffset(timeProvider.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero).AddHours(8);

        // PasswordHasher<T> takes a user only to satisfy its interface: the default
        // implementation salts per call and never reads it. One throwaway carrier
        // therefore serves every account, and each still gets its own salt.
        var hashCarrier = User.CreateAssistant("seed", "seed", string.Empty, opening);
        string Hash() => passwordHasher.HashPassword(hashCarrier, DemoPassword);

        User Doctor(string username, string fullName, Guid specialtyId) =>
            User.CreateDoctor(username, fullName, Hash(), specialtyId, opening);

        User Assistant(string username, string fullName) =>
            User.CreateAssistant(username, fullName, Hash(), opening);

        var internalMedicine = Specialty.Create("Belgyógyászat", opening);
        var dermatology = Specialty.Create("Bőrgyógyászat", opening);
        var ophthalmology = Specialty.Create("Szemészet", opening);
        database.Specialties.AddRange(internalMedicine, dermatology, ophthalmology);

        // Two doctors share internal medicine, so the assignment strategy has a
        // visible choice to make during the demo rather than a foregone one.
        var kovacs = Doctor("kovacs.istvan", "Dr. Kovács István", internalMedicine.Id);
        var nagy = Doctor("nagy.peter", "Dr. Nagy Péter", internalMedicine.Id);
        var szabo = Doctor("szabo.maria", "Dr. Szabó Mária", dermatology.Id);
        var toth = Doctor("toth.gabor", "Dr. Tóth Gábor", ophthalmology.Id);
        database.Users.AddRange(kovacs, nagy, szabo, toth);

        database.Users.AddRange(
            Assistant("horvath.anna", "Horváth Anna"),
            Assistant("kiss.eva", "Kiss Éva"));

        // Every TAJ below is checksum-valid, not merely well-formed, so the seed
        // data still loads if Validation:TajChecksumEnabled is ever turned on.
        var (erzsebet, registered) = Arrival(
            opening, 5, "Tóth Erzsébet", "1052 Budapest, Váci utca 12.", "123-456-788", "Fejfájás és szédülés");

        var (varga, waitingForKovacs) = Arrival(
            opening, 12, "Varga László", "1077 Budapest, Wesselényi utca 4.", "234-567-898", "Mellkasi szorító érzés");
        waitingForKovacs.AssignToQueue(internalMedicine.Id, kovacs.Id, opening.AddMinutes(14));

        var (balogh, waitingForNagy) = Arrival(
            opening, 20, "Balogh Katalin", "1136 Budapest, Hollán Ernő utca 21.", "345-678-915", "Magas vérnyomás kontroll");
        waitingForNagy.AssignToQueue(internalMedicine.Id, nagy.Id, opening.AddMinutes(22));

        var (molnar, inTreatment) = Arrival(
            opening, 28, "Molnár Zoltán", "1024 Budapest, Margit körút 8.", "456-789-128", "Viszkető kiütés a karon");
        inTreatment.AssignToQueue(dermatology.Id, szabo.Id, opening.AddMinutes(30));
        inTreatment.CallIn(opening.AddMinutes(41));

        var (fekete, completed) = Arrival(
            opening, 35, "Fekete Júlia", "1085 Budapest, József körút 33.", "567-891-235", "Homályos látás olvasásnál");
        completed.AssignToQueue(ophthalmology.Id, toth.Id, opening.AddMinutes(37));
        completed.CallIn(opening.AddMinutes(44));
        completed.RecordDiagnosis("Presbyopia. Olvasószemüveg felírva, kontroll egy év múlva.");
        completed.Release(opening.AddMinutes(58));

        var (simon, secondForToth) = Arrival(
            opening, 47, "Simon András", "1119 Budapest, Etele út 55.", "678-912-348", "Szemszárazság, égő érzés");
        secondForToth.AssignToQueue(ophthalmology.Id, toth.Id, opening.AddMinutes(49));

        database.Patients.AddRange(erzsebet, varga, balogh, molnar, fekete, simon);
        database.Visits.AddRange(registered, waitingForKovacs, waitingForNagy, inTreatment, completed, secondForToth);

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded the development database: {Specialties} specialties, {Users} users, {Patients} patients, {Visits} visits.",
            3,
            6,
            6,
            6);
    }

    private static (Patient Patient, Visit Visit) Arrival(
        DateTimeOffset opening,
        int minutesAfterOpening,
        string fullName,
        string address,
        string taj,
        string complaint)
    {
        var arrivedAt = opening.AddMinutes(minutesAfterOpening);

        var patient = Patient.Create(
            PatientName.Create(fullName),
            address,
            TajNumber.Create(taj),
            arrivedAt);

        return (patient, Visit.Register(patient.Id, complaint, arrivedAt));
    }
}
