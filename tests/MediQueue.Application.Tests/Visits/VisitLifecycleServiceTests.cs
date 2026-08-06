using MediQueue.Application.Exceptions;
using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Visits;
using NSubstitute;

namespace MediQueue.Application.Tests.Visits;

/// <summary>
/// The doctor actions, and the ownership rule that guards all three.
/// </summary>
public class VisitLifecycleServiceTests
{
    private readonly VisitServiceFixture _fixture = new();

    public static TheoryData<string> DoctorActions() => ["call-in", "diagnosis", "release"];

    private Task Invoke(string action, Guid visitId) => action switch
    {
        "call-in" => _fixture.Lifecycle.CallInAsync(visitId, default),
        "diagnosis" => _fixture.Lifecycle.RecordDiagnosisAsync(visitId, "Migrén", default),
        _ => _fixture.Lifecycle.ReleaseAsync(visitId, default),
    };

    [Theory]
    [MemberData(nameof(DoctorActions))]
    public async Task Another_doctors_visit_is_refused_and_left_untouched(string action)
    {
        var visit = _fixture.AQueuedVisit(doctorId: VisitServiceFixture.OtherDoctorId);
        var before = (visit.Status, visit.Diagnosis, visit.CalledInAt, visit.CompletedAt);

        _fixture.SignedInAsDoctor(VisitServiceFixture.DoctorId);

        var exception = await Should.ThrowAsync<ForbiddenException>(() => Invoke(action, visit.Id));

        // The message names nobody. Confirming that a visit belongs to a named
        // colleague is itself a disclosure, and the caller cannot act on it.
        exception.Message.ShouldNotContain("Kovács");
        exception.Message.ShouldNotContain(VisitServiceFixture.OtherDoctorId.ToString());

        (visit.Status, visit.Diagnosis, visit.CalledInAt, visit.CompletedAt).ShouldBe(before);
        await _fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(DoctorActions))]
    public async Task An_unknown_visit_is_not_found(string action)
    {
        await Should.ThrowAsync<NotFoundException>(() => Invoke(action, Guid.CreateVersion7(VisitServiceFixture.Now)));
    }

    [Fact]
    public async Task The_owning_doctor_can_call_the_patient_in()
    {
        var visit = _fixture.AQueuedVisit();

        var result = await _fixture.Lifecycle.CallInAsync(visit.Id, default);

        result.Status.ShouldBe(Contracts.Visits.VisitStatus.InTreatment);
        result.CalledInAt.ShouldBe(VisitServiceFixture.Now);
        await _fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_owning_doctor_sees_the_diagnosis_they_recorded()
    {
        var visit = _fixture.AQueuedVisit();
        await _fixture.Lifecycle.CallInAsync(visit.Id, default);

        var result = await _fixture.Lifecycle.RecordDiagnosisAsync(visit.Id, "Migrén", default);

        result.Diagnosis.ShouldBe("Migrén");
    }

    [Fact]
    public async Task An_invalid_transition_reaches_the_caller_unchanged()
    {
        // The service must not catch and rewrap this: the API's mapping is what
        // turns it into a 409 carrying the states, and a rewrap would lose them.
        var visit = _fixture.AQueuedVisit();

        var exception = await Should.ThrowAsync<InvalidVisitTransitionException>(
            () => _fixture.Lifecycle.ReleaseAsync(visit.Id, default));

        exception.From.ShouldBe(VisitStatus.Waiting);
        exception.To.ShouldBe(VisitStatus.Done);
        exception.AllowedAlternatives.ShouldBe([VisitStatus.InTreatment]);
    }

    [Fact]
    public async Task Timestamps_come_from_the_injected_clock()
    {
        var visit = _fixture.AQueuedVisit();
        _fixture.Clock.SetUtcNow(VisitServiceFixture.Now.AddHours(2));

        var result = await _fixture.Lifecycle.CallInAsync(visit.Id, default);

        result.CalledInAt.ShouldBe(new DateTimeOffset(2026, 8, 6, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Soft_deleting_records_the_acting_user_from_the_token()
    {
        var visit = _fixture.AQueuedVisit();
        _fixture.SignedInAsAssistant();

        await _fixture.Lifecycle.SoftDeleteAsync(visit.Id, default);

        visit.IsDeleted.ShouldBeTrue();
        // Never from the request body: who deleted it is the token's word.
        visit.DeletedByUserId.ShouldBe(VisitServiceFixture.AssistantId);
        visit.DeletedAt.ShouldBe(VisitServiceFixture.Now);
    }

    [Fact]
    public async Task Soft_deleting_an_unknown_visit_is_not_found()
    {
        _fixture.SignedInAsAssistant();

        await Should.ThrowAsync<NotFoundException>(
            () => _fixture.Lifecycle.SoftDeleteAsync(Guid.CreateVersion7(VisitServiceFixture.Now), default));
    }
}
