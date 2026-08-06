using MediQueue.Application.Exceptions;
using MediQueue.Contracts.Visits;
using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Scheduling;
using MediQueue.Domain.Visits;
using NSubstitute;
using DomainVisitStatus = MediQueue.Domain.Visits.VisitStatus;
using WireVisitStatus = MediQueue.Contracts.Visits.VisitStatus;

namespace MediQueue.Application.Tests.Visits;

public class VisitRegistrationServiceTests
{
    private readonly VisitServiceFixture _fixture = new();

    private static RegisterVisitRequest ARequest(
        string taj = "123-456-788",
        string name = "Kis Elemér",
        Guid? specialtyId = null) =>
        new(name, "1052 Budapest, Váci utca 12.", taj, "Fejfájás", specialtyId);

    [Fact]
    public async Task An_unknown_TAJ_creates_a_patient()
    {
        var result = await _fixture.Registration.RegisterAsync(ARequest(), default);

        _fixture.Patients.Received(1).Add(Arg.Any<Patient>());
        result.PatientFullName.ShouldBe("Kis Elemér");
        result.Taj.ShouldBe("123-456-788");
        result.Status.ShouldBe(WireVisitStatus.Registered);
    }

    [Fact]
    public async Task A_known_TAJ_reuses_the_patient_and_leaves_their_details_alone()
    {
        var existing = _fixture.APatient(name: "Kis Elemér");

        // The same person, re-registered by somebody typing in a hurry.
        await _fixture.Registration.RegisterAsync(
            ARequest(name: "Kis Elemérné") with { Address = "Somewhere else entirely" },
            default);

        _fixture.Patients.DidNotReceive().Add(Arg.Any<Patient>());

        // A registration form is not the place to silently correct a record.
        existing.FullName.Value.ShouldBe("Kis Elemér");
        existing.Address.ShouldBe("Budapest");
    }

    [Fact]
    public async Task A_patient_who_is_already_in_a_queue_cannot_be_registered_twice()
    {
        var existing = _fixture.APatient();
        _fixture.Visits.HasOpenVisitAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(true);

        var exception = await Should.ThrowAsync<ConflictException>(
            () => _fixture.Registration.RegisterAsync(ARequest(), default));

        exception.Message.ShouldContain("Kis Elemér");
        _fixture.Visits.DidNotReceive().Add(Arg.Any<Visit>());
    }

    [Fact]
    public async Task A_returning_patient_whose_last_visit_finished_is_registered_again()
    {
        var existing = _fixture.APatient();
        _fixture.Visits.HasOpenVisitAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _fixture.Registration.RegisterAsync(ARequest(), default);

        result.PatientId.ShouldBe(existing.Id);
        _fixture.Visits.Received(1).Add(Arg.Any<Visit>());
    }

    [Fact]
    public async Task Supplying_a_specialty_routes_the_visit_in_the_same_call()
    {
        var result = await _fixture.Registration.RegisterAsync(
            ARequest(specialtyId: VisitServiceFixture.SpecialtyId), default);

        result.Status.ShouldBe(WireVisitStatus.Waiting);
        result.DoctorId.ShouldBe(VisitServiceFixture.DoctorId);
        result.SpecialtyName.ShouldBe("Belgyógyászat");
        result.QueuedAt.ShouldBe(VisitServiceFixture.Now);
    }

    [Fact]
    public async Task Omitting_a_specialty_leaves_the_visit_registered_and_unassigned()
    {
        var result = await _fixture.Registration.RegisterAsync(ARequest(specialtyId: null), default);

        result.Status.ShouldBe(WireVisitStatus.Registered);
        result.DoctorId.ShouldBeNull();
        result.SpecialtyId.ShouldBeNull();
        result.QueuedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_specialty_with_no_active_doctor_is_refused_and_nothing_is_committed()
    {
        var exception = await Should.ThrowAsync<ConflictException>(
            () => _fixture.Registration.RegisterAsync(
                ARequest(specialtyId: VisitServiceFixture.EmptySpecialtyId), default));

        // Named rather than identified: an assistant cannot act on a GUID.
        exception.Message.ShouldContain("Reumatológia");

        // Nothing is written, so the visit does not exist half-routed.
        await _fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_strategy_is_asked_with_the_candidates_the_directory_returned()
    {
        IReadOnlyList<DoctorWorkload> candidates =
            [new DoctorWorkload(VisitServiceFixture.DoctorId, 3, 1, VisitServiceFixture.Now)];
        _fixture.Doctors
            .GetWorkloadsAsync(VisitServiceFixture.SpecialtyId, Arg.Any<CancellationToken>())
            .Returns(candidates);

        await _fixture.Registration.RegisterAsync(
            ARequest(specialtyId: VisitServiceFixture.SpecialtyId), default);

        _fixture.Strategy.Received(1).SelectDoctor(
            VisitServiceFixture.SpecialtyId,
            Arg.Is<IReadOnlyCollection<DoctorWorkload>>(passed => passed != null && passed.SequenceEqual(candidates)));
    }

    [Fact]
    public async Task The_doctor_the_strategy_names_is_the_doctor_stored()
    {
        _fixture.Strategy
            .SelectDoctor(VisitServiceFixture.SpecialtyId, Arg.Any<IReadOnlyCollection<DoctorWorkload>>())
            .Returns(VisitServiceFixture.OtherDoctorId);

        var result = await _fixture.Registration.RegisterAsync(
            ARequest(specialtyId: VisitServiceFixture.SpecialtyId), default);

        result.DoctorId.ShouldBe(VisitServiceFixture.OtherDoctorId);
    }

    [Theory]
    [InlineData("12-123-123", nameof(TajNumber))]
    [InlineData("123123123", nameof(TajNumber))]
    public async Task A_malformed_TAJ_is_refused_by_the_domain(string taj, string field)
    {
        var exception = await Should.ThrowAsync<ValidationException>(
            () => _fixture.Registration.RegisterAsync(ARequest(taj: taj), default));

        exception.FieldName.ShouldBe(field);
    }

    [Fact]
    public async Task A_name_containing_a_digit_is_refused_by_the_domain()
    {
        var exception = await Should.ThrowAsync<ValidationException>(
            () => _fixture.Registration.RegisterAsync(ARequest(name: "Kis Elemér2"), default));

        exception.FieldName.ShouldBe(nameof(PatientName));
    }

    [Fact]
    public async Task Timestamps_come_from_the_injected_clock()
    {
        var result = await _fixture.Registration.RegisterAsync(ARequest(), default);

        result.RegisteredAt.ShouldBe(new DateTimeOffset(2026, 8, 6, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task The_patient_and_the_visit_are_committed_once_together()
    {
        // One business action, one transaction — and from P5, one audit boundary.
        await _fixture.Registration.RegisterAsync(
            ARequest(specialtyId: VisitServiceFixture.SpecialtyId), default);

        await _fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_registered_visit_starts_with_only_its_registration_timestamp()
    {
        var result = await _fixture.Registration.RegisterAsync(ARequest(), default);

        result.CalledInAt.ShouldBeNull();
        result.CompletedAt.ShouldBeNull();
        result.Status.ShouldBe(WireVisitStatus.Registered);
        DomainVisitStatus.Registered.ToString().ShouldBe(result.Status.ToString());
    }
}
