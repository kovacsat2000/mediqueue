using MediQueue.Application.Exceptions;
using MediQueue.Contracts.Visits;
using MediQueue.Domain.Visits;
using NSubstitute;

namespace MediQueue.Application.Tests.Visits;

/// <summary>Reading visits and queues, projected by role.</summary>
public class VisitQueryServiceTests
{
    private readonly VisitServiceFixture _fixture = new();

    [Fact]
    public async Task An_assistant_gets_the_summary_projection_for_anybody_s_visit()
    {
        var visit = _fixture.AQueuedVisit(doctorId: VisitServiceFixture.OtherDoctorId);
        _fixture.SignedInAsAssistant();

        var result = await _fixture.Query.GetAsync(visit.Id, default);

        result.Summary.ShouldNotBeNull();
        result.Detail.ShouldBeNull();

        // The type itself is the guarantee: there is no member to leak through.
        typeof(VisitSummaryDto).GetProperty("Diagnosis").ShouldBeNull();
    }

    [Fact]
    public async Task The_treating_doctor_gets_the_detail_projection()
    {
        var visit = _fixture.AQueuedVisit();
        visit.CallIn(VisitServiceFixture.Now.AddMinutes(10));
        visit.RecordDiagnosis("Migrén");

        var result = await _fixture.Query.GetAsync(visit.Id, default);

        result.Detail.ShouldNotBeNull();
        result.Summary.ShouldBeNull();
        result.Detail.Diagnosis.ShouldBe("Migrén");
    }

    [Fact]
    public async Task A_doctor_asking_about_a_colleagues_visit_is_refused_rather_than_downgraded()
    {
        // Not the summary projection: handing back a lesser view would be a
        // quieter answer and a worse one.
        var visit = _fixture.AQueuedVisit(doctorId: VisitServiceFixture.OtherDoctorId);
        _fixture.SignedInAsDoctor(VisitServiceFixture.DoctorId);

        await Should.ThrowAsync<ForbiddenException>(() => _fixture.Query.GetAsync(visit.Id, default));
    }

    [Fact]
    public async Task An_unknown_visit_is_not_found()
    {
        await Should.ThrowAsync<NotFoundException>(
            () => _fixture.Query.GetAsync(Guid.CreateVersion7(VisitServiceFixture.Now), default));
    }

    [Fact]
    public async Task The_unrouted_list_projects_visits_that_are_in_no_queue()
    {
        // It reads visits, not queues, which is why it lives here. The projection
        // is the summary type: this is an assistant-facing listing.
        _fixture.SignedInAsAssistant();

        var patient = _fixture.APatient("123-456-788", "Kis Elemér");
        var unrouted = Visit.Register(patient.Id, "Fejfájás", VisitServiceFixture.Now);

        _fixture.Visits.GetUnassignedAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Visit>>([unrouted]);

        var result = await _fixture.Query.GetUnassignedAsync(default);

        var only = result.ShouldHaveSingleItem();
        only.Id.ShouldBe(unrouted.Id);
        only.PatientFullName.ShouldBe("Kis Elemér");

        // In nobody's queue is the whole point of the list.
        only.DoctorId.ShouldBeNull();
        only.SpecialtyId.ShouldBeNull();
        only.QueuedAt.ShouldBeNull();
    }

    [Fact]
    public async Task An_unrouted_visit_whose_patient_is_missing_fails_loudly_too()
    {
        // The same guard as the queue projection, because it is now the same
        // batched loader rather than a second copy of one.
        _fixture.SignedInAsAssistant();

        var orphan = Visit.Register(
            Guid.CreateVersion7(VisitServiceFixture.Now), "Fejfájás", VisitServiceFixture.Now);

        _fixture.Visits.GetUnassignedAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Visit>>([orphan]);

        var exception = await Should.ThrowAsync<NotFoundException>(
            () => _fixture.Query.GetUnassignedAsync(default));

        exception.Message.ShouldContain(orphan.PatientId.ToString());
    }

    [Fact]
    public async Task A_doctor_may_read_only_their_own_queue()
    {
        _fixture.SignedInAsDoctor(VisitServiceFixture.DoctorId);

        await Should.ThrowAsync<ForbiddenException>(
            () => _fixture.Queues.GetQueueForDoctorAsync(VisitServiceFixture.OtherDoctorId, default));
    }

    [Fact]
    public async Task An_assistant_may_read_any_doctors_queue()
    {
        _fixture.SignedInAsAssistant();
        _fixture.Visits.GetQueueAsync(VisitServiceFixture.OtherDoctorId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Visit>>([]);

        var queue = await _fixture.Queues.GetQueueForDoctorAsync(VisitServiceFixture.OtherDoctorId, default);

        queue.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_queue_comes_back_in_the_order_of_the_timestamp_it_displays()
    {
        var early = _fixture.APatient("123-456-788", "Kis Elemér");
        var late = _fixture.APatient("234-567-898", "Nagy Piroska");

        var first = Visit.Register(early.Id, "Fejfájás", VisitServiceFixture.Now);
        first.AssignToQueue(VisitServiceFixture.SpecialtyId, VisitServiceFixture.DoctorId,
            VisitServiceFixture.Now.AddMinutes(5));

        var second = Visit.Register(late.Id, "Szédülés", VisitServiceFixture.Now);
        second.AssignToQueue(VisitServiceFixture.SpecialtyId, VisitServiceFixture.DoctorId,
            VisitServiceFixture.Now.AddMinutes(1));

        // Deliberately handed back out of order, as a database without an ORDER BY
        // would.
        _fixture.Visits.GetQueueAsync(VisitServiceFixture.DoctorId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Visit>>([first, second]);

        var queue = await _fixture.Queues.GetQueueForDoctorAsync(VisitServiceFixture.DoctorId, default);

        queue.Select(visit => visit.Id).ShouldBe([second.Id, first.Id]);

        // The order shown and the times shown must agree, or the list reads as
        // a bug on screen.
        queue.Select(visit => visit.QueuedAt).ShouldBeInOrder();
    }

    [Fact]
    public async Task A_visit_whose_patient_is_missing_fails_loudly_rather_than_rendering_blank()
    {
        // The batched lookup replaced a per-visit loop. A dictionary that
        // silently yields nothing would produce rows with empty names, which
        // looks like a data problem and hides a broken foreign key.
        _fixture.SignedInAsAssistant();

        var orphan = Visit.Register(Guid.CreateVersion7(VisitServiceFixture.Now), "Fejfájás", VisitServiceFixture.Now);
        orphan.AssignToQueue(VisitServiceFixture.SpecialtyId, VisitServiceFixture.DoctorId,
            VisitServiceFixture.Now.AddMinutes(1));

        _fixture.Visits.GetAllOpenVisitsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Visit>>([orphan]);

        var exception = await Should.ThrowAsync<NotFoundException>(
            () => _fixture.Queues.GetAllQueuesAsync(default));

        exception.Message.ShouldContain(orphan.PatientId.ToString());
    }

    [Fact]
    public async Task The_projection_loads_every_patient_in_one_query()
    {
        _fixture.SignedInAsAssistant();

        var first = _fixture.APatient("123-456-788", "Kis Elemér");
        var second = _fixture.APatient("234-567-898", "Nagy Piroska");

        IReadOnlyList<Visit> open =
        [
            AQueuedVisitFor(first),
            AQueuedVisitFor(second),
        ];
        _fixture.Visits.GetAllOpenVisitsAsync(Arg.Any<CancellationToken>()).Returns(open);

        await _fixture.Queues.GetAllQueuesAsync(default);

        // One batched call, and never the per-visit lookup it replaced.
        await _fixture.Patients.Received(1)
            .GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _fixture.Patients.DidNotReceive().FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static Visit AQueuedVisitFor(MediQueue.Domain.Patients.Patient patient)
    {
        var visit = Visit.Register(patient.Id, "Fejfájás", VisitServiceFixture.Now);
        visit.AssignToQueue(VisitServiceFixture.SpecialtyId, VisitServiceFixture.DoctorId,
            VisitServiceFixture.Now.AddMinutes(1));

        return visit;
    }

    [Fact]
    public async Task Every_active_doctor_appears_in_the_queue_list_even_with_nothing_waiting()
    {
        _fixture.SignedInAsAssistant();
        _fixture.Visits.GetAllOpenVisitsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<Visit>>([]);

        var queues = await _fixture.Queues.GetAllQueuesAsync(default);

        // An empty queue is how an assistant sees that somebody is free.
        queues.Count.ShouldBe(2);
        queues.ShouldAllBe(queue => queue.Visits.Count == 0);
        queues.Select(queue => queue.DoctorFullName)
            .ShouldBe(["Dr. Nagy Péter", "Dr. Kovács István"], ignoreOrder: true);
    }
}
