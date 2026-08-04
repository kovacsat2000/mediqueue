using MediQueue.Domain.Auditing;
using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Visits;

namespace MediQueue.Domain.Tests.Visits;

public class VisitTests
{
    private static readonly DateTimeOffset Registered = new(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Queued = new(2026, 8, 4, 9, 20, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CalledIn = new(2026, 8, 4, 10, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Completed = new(2026, 8, 4, 10, 30, 0, TimeSpan.Zero);

    private static readonly Guid PatientId = Guid.CreateVersion7(Registered);
    private static readonly Guid SpecialtyId = Guid.CreateVersion7(Registered);
    private static readonly Guid DoctorId = Guid.CreateVersion7(Registered);

    private static Visit ARegisteredVisit() => Visit.Register(PatientId, "Fejfájás", Registered);

    private static Visit AWaitingVisit()
    {
        var visit = ARegisteredVisit();
        visit.AssignToQueue(SpecialtyId, DoctorId, Queued);
        return visit;
    }

    private static Visit AVisitInTreatment()
    {
        var visit = AWaitingVisit();
        visit.CallIn(CalledIn);
        return visit;
    }

    private static Visit ACompletedVisit()
    {
        var visit = AVisitInTreatment();
        visit.Release(Completed);
        return visit;
    }

    [Fact]
    public void Register_starts_the_visit_with_only_the_registration_timestamp_set()
    {
        var visit = ARegisteredVisit();

        visit.Status.ShouldBe(VisitStatus.Registered);
        visit.PatientId.ShouldBe(PatientId);
        visit.Complaint.ShouldBe("Fejfájás");
        visit.RegisteredAt.ShouldBe(Registered);

        visit.QueuedAt.ShouldBeNull();
        visit.CalledInAt.ShouldBeNull();
        visit.CompletedAt.ShouldBeNull();
        visit.SpecialtyId.ShouldBeNull();
        visit.DoctorId.ShouldBeNull();
        visit.Diagnosis.ShouldBeNull();
        visit.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public void The_whole_lifecycle_sets_each_timestamp_exactly_once_and_ends_in_Done()
    {
        var visit = ARegisteredVisit();

        visit.AssignToQueue(SpecialtyId, DoctorId, Queued);
        visit.CallIn(CalledIn);
        visit.RecordDiagnosis("Migrén");
        visit.Release(Completed);

        visit.Status.ShouldBe(VisitStatus.Done);
        visit.SpecialtyId.ShouldBe(SpecialtyId);
        visit.DoctorId.ShouldBe(DoctorId);
        visit.Diagnosis.ShouldBe("Migrén");

        visit.RegisteredAt.ShouldBe(Registered);
        visit.QueuedAt.ShouldBe(Queued);
        visit.CalledInAt.ShouldBe(CalledIn);
        visit.CompletedAt.ShouldBe(Completed);
    }

    [Fact]
    public void CallIn_is_refused_before_the_visit_is_queued()
    {
        var visit = ARegisteredVisit();

        var exception = Should.Throw<InvalidVisitTransitionException>(() => visit.CallIn(CalledIn));

        exception.From.ShouldBe(VisitStatus.Registered);
        exception.To.ShouldBe(VisitStatus.InTreatment);
        visit.Status.ShouldBe(VisitStatus.Registered);
        visit.CalledInAt.ShouldBeNull();
    }

    [Fact]
    public void Release_is_refused_while_the_patient_is_still_waiting()
    {
        var visit = AWaitingVisit();

        Should.Throw<InvalidVisitTransitionException>(() => visit.Release(Completed));

        visit.Status.ShouldBe(VisitStatus.Waiting);
        visit.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void AssignToQueue_is_refused_once_the_visit_is_done()
    {
        var visit = ACompletedVisit();

        var exception = Should.Throw<InvalidVisitTransitionException>(
            () => visit.AssignToQueue(SpecialtyId, DoctorId, Queued));

        exception.AllowedAlternatives.ShouldBeEmpty();
        visit.Status.ShouldBe(VisitStatus.Done);
    }

    [Fact]
    public void A_visit_cannot_be_queued_twice()
    {
        var visit = AWaitingVisit();

        Should.Throw<InvalidVisitTransitionException>(() => visit.AssignToQueue(SpecialtyId, DoctorId, Queued));
    }

    [Fact]
    public void A_patient_cannot_be_called_in_twice()
    {
        var visit = AVisitInTreatment();

        Should.Throw<InvalidVisitTransitionException>(() => visit.CallIn(CalledIn));
    }

    [Theory]
    [InlineData(VisitStatus.Registered)]
    [InlineData(VisitStatus.Waiting)]
    [InlineData(VisitStatus.Done)]
    public void RecordDiagnosis_is_refused_outside_treatment(VisitStatus status)
    {
        var visit = status switch
        {
            VisitStatus.Registered => ARegisteredVisit(),
            VisitStatus.Waiting => AWaitingVisit(),
            _ => ACompletedVisit(),
        };

        var exception = Should.Throw<DomainException>(() => visit.RecordDiagnosis("Migrén"));

        exception.Message.ShouldContain(status.ToString());
        visit.Diagnosis.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordDiagnosis_refuses_a_blank_diagnosis(string diagnosis)
    {
        var visit = AVisitInTreatment();

        Should.Throw<DomainException>(() => visit.RecordDiagnosis(diagnosis));

        visit.Diagnosis.ShouldBeNull();
    }

    [Fact]
    public void The_diagnosis_is_marked_sensitive_so_the_audit_log_will_redact_it()
    {
        // The audit interceptor reads this attribute; without it an assistant
        // could read a diagnosis out of the audit trail.
        typeof(Visit).GetProperty(nameof(Visit.Diagnosis))!
            .GetCustomAttributes(typeof(SensitiveAuditAttribute), inherit: false)
            .ShouldNotBeEmpty();
    }

    [Fact]
    public void SoftDelete_sets_the_flags_and_leaves_the_status_alone()
    {
        var visit = AWaitingVisit();
        var deletedBy = Guid.CreateVersion7(Completed);

        visit.SoftDelete(deletedBy, Completed);

        visit.IsDeleted.ShouldBeTrue();
        visit.DeletedAt.ShouldBe(Completed);
        visit.DeletedByUserId.ShouldBe(deletedBy);
        visit.Status.ShouldBe(VisitStatus.Waiting);
    }

    [Fact]
    public void SoftDelete_works_from_any_state()
    {
        foreach (var visit in new[] { ARegisteredVisit(), AWaitingVisit(), AVisitInTreatment(), ACompletedVisit() })
        {
            var statusBefore = visit.Status;

            visit.SoftDelete(Guid.CreateVersion7(Completed), Completed);

            visit.IsDeleted.ShouldBeTrue();
            visit.Status.ShouldBe(statusBefore);
        }
    }

    [Fact]
    public void Identifiers_are_version_7_so_they_sort_by_creation_time()
    {
        ARegisteredVisit().Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Identifiers_generated_from_increasing_times_sort_in_that_order()
    {
        var earlier = Visit.Register(PatientId, "Fejfájás", Registered);
        var later = Visit.Register(PatientId, "Fejfájás", Completed);

        earlier.Id.CompareTo(later.Id).ShouldBeLessThan(0);
    }
}
