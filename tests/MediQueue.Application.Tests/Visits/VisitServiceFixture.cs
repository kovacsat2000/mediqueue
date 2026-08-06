using MediQueue.Application.Abstractions;
using MediQueue.Application.Visits;
using MediQueue.Contracts.Directory;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Scheduling;
using MediQueue.Domain.Users;
using MediQueue.Domain.Visits;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MediQueue.Application.Tests.Visits;

/// <summary>
/// The substituted world the visit use cases run in.
/// </summary>
/// <remarks>
/// No host, no database, no container. If one of these tests ever needs either,
/// the seam is in the wrong place.
/// </remarks>
public sealed class VisitServiceFixture
{
    public static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    public static readonly Guid SpecialtyId = Guid.CreateVersion7(Now);
    public static readonly Guid EmptySpecialtyId = Guid.CreateVersion7(Now.AddSeconds(1));
    public static readonly Guid DoctorId = Guid.CreateVersion7(Now.AddSeconds(2));
    public static readonly Guid OtherDoctorId = Guid.CreateVersion7(Now.AddSeconds(3));
    public static readonly Guid AssistantId = Guid.CreateVersion7(Now.AddSeconds(4));

    public IPatientRepository Patients { get; } = Substitute.For<IPatientRepository>();
    public IVisitRepository Visits { get; } = Substitute.For<IVisitRepository>();
    public IDoctorDirectory Doctors { get; } = Substitute.For<IDoctorDirectory>();
    public ISpecialtyDirectory Specialties { get; } = Substitute.For<ISpecialtyDirectory>();
    public IDoctorAssignmentStrategy Strategy { get; } = Substitute.For<IDoctorAssignmentStrategy>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();
    public FakeTimeProvider Clock { get; } = new(Now);

    public VisitServiceFixture()
    {
        Specialties.ListAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<SpecialtyDto>>(
        [
            new SpecialtyDto(SpecialtyId, "Belgyógyászat"),
            new SpecialtyDto(EmptySpecialtyId, "Reumatológia"),
        ]);

        Doctors.GetActiveAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<User>>(
        [
            TheDoctor(),
            TheOtherDoctor(),
        ]);

        // The interesting specialty has candidates; the empty one has none.
        Doctors.GetWorkloadsAsync(SpecialtyId, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<DoctorWorkload>>(
            [new DoctorWorkload(DoctorId, 0, 0, null)]);
        Doctors.GetWorkloadsAsync(EmptySpecialtyId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<DoctorWorkload>>([]);

        Strategy.SelectDoctor(SpecialtyId, Arg.Any<IReadOnlyCollection<DoctorWorkload>>()).Returns(DoctorId);
        Strategy.SelectDoctor(EmptySpecialtyId, Arg.Any<IReadOnlyCollection<DoctorWorkload>>()).Returns((Guid?)null);

        Patients.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => PatientsById.GetValueOrDefault(callInfo.Arg<Guid>()));

        // A repository that forgets what was just added to it is not behaving
        // like a repository, and every test built on it would be lying.
        Patients.When(repository => repository.Add(Arg.Any<Patient>()))
            .Do(callInfo =>
            {
                if (callInfo.Arg<Patient>() is { } added)
                {
                    PatientsById[added.Id] = added;
                }
            });

        SignedInAsDoctor();
    }

    public Dictionary<Guid, Patient> PatientsById { get; } = [];

    public VisitContextLoader Context => new(Patients, Specialties, Doctors);

    public VisitAssignmentService Assignment =>
        new(Visits, Doctors, Specialties, Strategy, UnitOfWork, Context, Clock);

    public VisitRegistrationService Registration =>
        new(Patients, Visits, UnitOfWork, Assignment, Context, Clock);

    public VisitLifecycleService Lifecycle =>
        new(Visits, UnitOfWork, Context, CurrentUser, Clock);

    public VisitQueryService Query => new(Visits, Context, CurrentUser);

    public QueueQueryService Queues => new(Visits, Patients, Doctors, Specialties, CurrentUser);

    public static User TheDoctor() => Rehydrate(
        User.CreateDoctor("nagy.peter", "Dr. Nagy Péter", "hash", SpecialtyId, Now), DoctorId);

    public static User TheOtherDoctor() => Rehydrate(
        User.CreateDoctor("kovacs.istvan", "Dr. Kovács István", "hash", SpecialtyId, Now), OtherDoctorId);

    public void SignedInAsDoctor(Guid? doctorId = null)
    {
        CurrentUser.Role.Returns(Contracts.UserRole.Doctor);
        CurrentUser.UserId.Returns(doctorId ?? DoctorId);
        CurrentUser.IsAuthenticated.Returns(true);
    }

    public void SignedInAsAssistant()
    {
        CurrentUser.Role.Returns(Contracts.UserRole.Assistant);
        CurrentUser.UserId.Returns(AssistantId);
        CurrentUser.IsAuthenticated.Returns(true);
    }

    /// <summary>Registers a patient with the substituted repository so lookups find them.</summary>
    public Patient APatient(string taj = "123-456-788", string name = "Kis Elemér")
    {
        var patient = Patient.Create(PatientName.Create(name), "Budapest", TajNumber.Create(taj), Now);
        PatientsById[patient.Id] = patient;
        Patients.FindByTajAsync(
                Arg.Is<TajNumber>(candidate => candidate != null && candidate.Digits == patient.Taj.Digits),
                Arg.Any<CancellationToken>())
            .Returns(patient);

        return patient;
    }

    /// <summary>A visit already in a doctor's queue, findable by the substituted repository.</summary>
    public Visit AQueuedVisit(Guid? doctorId = null, Patient? patient = null)
    {
        patient ??= APatient();
        var visit = Visit.Register(patient.Id, "Fejfájás", Now);
        visit.AssignToQueue(SpecialtyId, doctorId ?? DoctorId, Now.AddMinutes(5));

        Visits.GetByIdAsync(visit.Id, Arg.Any<CancellationToken>()).Returns(visit);

        return visit;
    }

    /// <summary>
    /// Gives an entity the identifier the substitutes are keyed on.
    /// </summary>
    /// <remarks>
    /// Identifiers are generated inside the factories, so a test that needs to
    /// pin one has to set it afterwards. This is the one place that happens, and
    /// it exists because <c>ICurrentUser.UserId</c> must match a doctor the
    /// directory returns.
    /// </remarks>
    private static User Rehydrate(User user, Guid id)
    {
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, id);

        return user;
    }
}
