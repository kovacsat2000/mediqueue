using MediQueue.Application.Abstractions;
using MediQueue.Contracts.Visits;
using MediQueue.Domain.Visits;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MediQueue.Application.Tests.Visits;

/// <summary>
/// What the use cases publish, when they publish it, and what happens when the
/// transport is broken.
/// </summary>
public class VisitAnnouncementTests
{
    private readonly VisitServiceFixture _fixture = new();

    private RegisterVisitRequest ARequest(Guid? specialtyId = null) =>
        new("Kovács Anna", "1052 Budapest, Váci utca 12.", "123-456-788", "Fejfájás", specialtyId);

    [Fact]
    public async Task Registering_an_unrouted_visit_announces_it_as_registered()
    {
        await _fixture.Registration.RegisterAsync(ARequest(), default);

        await _fixture.Notifier.Received(1).VisitRegisteredAsync(
            Arg.Any<VisitSummaryDto>(), Arg.Any<CancellationToken>());

        await _fixture.Notifier.DidNotReceive().VisitQueuedAsync(
            Arg.Any<VisitSummaryDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registering_straight_into_a_queue_announces_it_as_queued_and_only_once()
    {
        // One business action, one event. Announcing both Registered and Queued
        // would describe an unrouted state that never existed for an observable
        // moment, and an assistant's screen would flash the row in and out of
        // the unrouted list.
        await _fixture.Registration.RegisterAsync(ARequest(VisitServiceFixture.SpecialtyId), default);

        await _fixture.Notifier.Received(1).VisitQueuedAsync(
            Arg.Any<VisitSummaryDto>(), Arg.Any<CancellationToken>());

        await _fixture.Notifier.DidNotReceive().VisitRegisteredAsync(
            Arg.Any<VisitSummaryDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assigning_a_visit_announces_it_once()
    {
        var visit = _fixture.AnUnroutedVisit();

        await _fixture.Assignment.AssignAsync(visit.Id, VisitServiceFixture.SpecialtyId, default);

        await _fixture.Notifier.Received(1).VisitQueuedAsync(
            Arg.Any<VisitSummaryDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Calling_a_patient_in_announces_it_once()
    {
        var visit = _fixture.AQueuedVisit();

        await _fixture.Lifecycle.CallInAsync(visit.Id, default);

        await _fixture.Notifier.Received(1).VisitCalledInAsync(
            Arg.Is<VisitSummaryDto>(summary => summary != null && summary.Id == visit.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Releasing_a_patient_announces_it_once()
    {
        var visit = _fixture.AQueuedVisit();
        visit.CallIn(VisitServiceFixture.Now.AddMinutes(10));

        await _fixture.Lifecycle.ReleaseAsync(visit.Id, default);

        await _fixture.Notifier.Received(1).VisitReleasedAsync(
            Arg.Is<VisitSummaryDto>(summary => summary != null && summary.Id == visit.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Withdrawing_a_visit_announces_the_identifiers_and_whose_queue_it_was_in()
    {
        var visit = _fixture.AQueuedVisit();
        _fixture.SignedInAsAssistant();

        await _fixture.Lifecycle.SoftDeleteAsync(visit.Id, default);

        await _fixture.Notifier.Received(1).VisitDeletedAsync(
            visit.Id, VisitServiceFixture.DoctorId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recording_a_diagnosis_announces_nothing_at_all()
    {
        // The one action with no event. It changes no queue, and it is the only
        // action carrying clinical information — so the channel that reaches
        // every connected assistant simply never carries it.
        var visit = _fixture.AQueuedVisit();
        visit.CallIn(VisitServiceFixture.Now.AddMinutes(10));

        await _fixture.Lifecycle.RecordDiagnosisAsync(visit.Id, "Migrén", default);

        _fixture.Notifier.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_announced_when_the_commit_fails()
    {
        // Publishing an event for a transaction that then failed is a lie the
        // clients have no way to detect.
        var visit = _fixture.AQueuedVisit();

        _fixture.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("the database is unreachable"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => _fixture.Lifecycle.CallInAsync(visit.Id, default));

        _fixture.Notifier.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_announced_when_registration_fails_its_business_rule()
    {
        var patient = _fixture.APatient();
        _fixture.Visits.HasOpenVisitAsync(patient.Id, Arg.Any<CancellationToken>()).Returns(true);

        await Should.ThrowAsync<Application.Exceptions.ConflictException>(
            () => _fixture.Registration.RegisterAsync(ARequest(), default));

        _fixture.Notifier.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_broken_transport_does_not_fail_the_action_it_describes()
    {
        // The write succeeded and the caller is entitled to its answer. A push
        // is a convenience over a system that is still correct without one, and
        // the recovery is the client's Refresh button.
        var visit = _fixture.AQueuedVisit();

        _fixture.Notifier
            .VisitCalledInAsync(Arg.Any<VisitSummaryDto>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("the hub is unreachable"));

        var result = await _fixture.Lifecycle.CallInAsync(visit.Id, default);

        result.Id.ShouldBe(visit.Id);
        result.Status.ShouldBe(Contracts.Visits.VisitStatus.InTreatment);
    }

    [Fact]
    public async Task A_broken_transport_still_leaves_the_change_committed()
    {
        var visit = _fixture.AQueuedVisit();

        _fixture.Notifier
            .VisitCalledInAsync(Arg.Any<VisitSummaryDto>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("the hub is unreachable"));

        await _fixture.Lifecycle.CallInAsync(visit.Id, default);

        // The commit happened before the publication was ever attempted, so the
        // failure cannot have prevented it.
        await _fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        visit.Status.ShouldBe(MediQueue.Domain.Visits.VisitStatus.InTreatment);
    }

    [Fact]
    public async Task A_transport_that_throws_something_unexpected_is_swallowed_too()
    {
        // Every exception, not a list of the ones a network was expected to
        // raise. A list that is wrong once re-introduces exactly the failure the
        // catch exists to prevent.
        var visit = _fixture.AQueuedVisit();

        _fixture.Notifier
            .VisitCalledInAsync(Arg.Any<VisitSummaryDto>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("the hub was disposed"));

        await Should.NotThrowAsync(() => _fixture.Lifecycle.CallInAsync(visit.Id, default));
    }

    [Fact]
    public void No_notifier_method_can_carry_a_diagnosis()
    {
        // The push channel inherits D-10 from the type system. This asserts the
        // shape of the seam rather than the behaviour behind it: a future
        // overload taking VisitDetailDto would be a compile-time route for a
        // diagnosis to reach every connected assistant.
        var payloadTypes = typeof(IRealtimeNotifier)
            .GetMethods()
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Distinct()
            .ToList();

        payloadTypes.ShouldNotContain(typeof(VisitDetailDto));
        payloadTypes.ShouldContain(typeof(VisitSummaryDto));
        typeof(VisitSummaryDto).GetProperty("Diagnosis").ShouldBeNull();
    }
}
