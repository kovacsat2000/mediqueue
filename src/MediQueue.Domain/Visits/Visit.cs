using MediQueue.Domain.Auditing;
using MediQueue.Domain.Exceptions;

namespace MediQueue.Domain.Visits;

/// <summary>
/// One episode of care: a patient arriving with a complaint, being routed to a
/// doctor, seen, and released.
/// </summary>
/// <remarks>
/// <para>
/// This is the aggregate that owns the state machine. <see cref="Status"/> has a
/// private setter and is written <strong>only</strong> by the transition methods
/// below, each of which asks <see cref="VisitStateMachine"/> for permission
/// first. Nothing else in the system may move a visit between states.
/// </para>
/// <para>
/// The current time is always a parameter, never read from the clock. That is
/// what lets every rule here be tested without waiting, freezing time, or
/// injecting a clock abstraction.
/// </para>
/// </remarks>
public sealed class Visit
{
    private Visit(Guid id, Guid patientId, string complaint, DateTimeOffset registeredAt)
    {
        Id = id;
        PatientId = patientId;
        Complaint = complaint;
        Status = VisitStatus.Registered;
        RegisteredAt = registeredAt;
    }

    /// <summary>The identifier. Time-ordered, so index pages stay dense as rows are inserted.</summary>
    public Guid Id { get; private set; }

    /// <summary>The patient this visit belongs to.</summary>
    public Guid PatientId { get; private set; }

    /// <summary>What the patient came in with, in their own words.</summary>
    public string Complaint { get; private set; }

    /// <summary>The specialty the visit was routed to. <c>null</c> until it is assigned.</summary>
    public Guid? SpecialtyId { get; private set; }

    /// <summary>The doctor whose queue the visit is in. <c>null</c> until it is assigned.</summary>
    public Guid? DoctorId { get; private set; }

    /// <summary>How far the visit has progressed. Written only by the transition methods.</summary>
    public VisitStatus Status { get; private set; }

    /// <summary>
    /// What the doctor found. Never leaves the server for an assistant, and
    /// redacted in the audit log for anyone not allowed to see it.
    /// </summary>
    [SensitiveAudit]
    public string? Diagnosis { get; private set; }

    /// <summary>When the assistant recorded the visit.</summary>
    public DateTimeOffset RegisteredAt { get; private set; }

    /// <summary>
    /// When the visit joined a doctor's queue. This is what the waiting list is
    /// ordered by and what it displays, so the order shown can never contradict
    /// the times shown.
    /// </summary>
    public DateTimeOffset? QueuedAt { get; private set; }

    /// <summary>When the doctor called the patient in.</summary>
    public DateTimeOffset? CalledInAt { get; private set; }

    /// <summary>When the patient was released.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Whether the visit has been logically deleted. Orthogonal to <see cref="Status"/>.</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>When the visit was logically deleted.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Who logically deleted the visit.</summary>
    public Guid? DeletedByUserId { get; private set; }

    /// <summary>Records a patient's arrival. The visit starts in <see cref="VisitStatus.Registered"/>.</summary>
    /// <param name="patientId">The patient this visit belongs to.</param>
    /// <param name="complaint">What the patient came in with.</param>
    /// <param name="now">The current time, supplied by the caller so the result is deterministic.</param>
    /// <returns>The new visit.</returns>
    public static Visit Register(Guid patientId, string complaint, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), patientId, complaint, now);

    /// <summary>Puts the visit into a doctor's queue.</summary>
    /// <param name="specialtyId">The specialty the visit was routed to.</param>
    /// <param name="doctorId">The doctor chosen by the assignment strategy.</param>
    /// <param name="now">The current time, supplied by the caller so the result is deterministic.</param>
    /// <exception cref="DomainException">The visit has been deleted.</exception>
    /// <exception cref="InvalidVisitTransitionException">The visit is not <see cref="VisitStatus.Registered"/>.</exception>
    public void AssignToQueue(Guid specialtyId, Guid doctorId, DateTimeOffset now)
    {
        EnsureNotDeleted();
        VisitStateMachine.EnsureCanTransition(Status, VisitStatus.Waiting);

        SpecialtyId = specialtyId;
        DoctorId = doctorId;
        QueuedAt = now;
        Status = VisitStatus.Waiting;
    }

    /// <summary>Calls the patient in from the waiting list.</summary>
    /// <param name="now">The current time, supplied by the caller so the result is deterministic.</param>
    /// <exception cref="DomainException">The visit has been deleted.</exception>
    /// <exception cref="InvalidVisitTransitionException">The visit is not <see cref="VisitStatus.Waiting"/>.</exception>
    public void CallIn(DateTimeOffset now)
    {
        EnsureNotDeleted();
        VisitStateMachine.EnsureCanTransition(Status, VisitStatus.InTreatment);

        CalledInAt = now;
        Status = VisitStatus.InTreatment;
    }

    /// <summary>Records what the doctor found. Does not move the visit on.</summary>
    /// <param name="diagnosis">The finding. Must not be blank.</param>
    /// <exception cref="DomainException">
    /// The visit has been deleted, is not <see cref="VisitStatus.InTreatment"/>, or the diagnosis is blank.
    /// </exception>
    public void RecordDiagnosis(string diagnosis)
    {
        EnsureNotDeleted();

        if (Status != VisitStatus.InTreatment)
        {
            throw new DomainException(
                $"A diagnosis can only be recorded while the visit is '{VisitStatus.InTreatment}'; this visit is '{Status}'.");
        }

        if (string.IsNullOrWhiteSpace(diagnosis))
        {
            throw new DomainException("A diagnosis cannot be blank.");
        }

        Diagnosis = diagnosis;
    }

    /// <summary>Releases the patient and completes the visit.</summary>
    /// <param name="now">The current time, supplied by the caller so the result is deterministic.</param>
    /// <exception cref="DomainException">The visit has been deleted.</exception>
    /// <exception cref="InvalidVisitTransitionException">The visit is not <see cref="VisitStatus.InTreatment"/>.</exception>
    public void Release(DateTimeOffset now)
    {
        EnsureNotDeleted();
        VisitStateMachine.EnsureCanTransition(Status, VisitStatus.Done);

        CompletedAt = now;
        Status = VisitStatus.Done;
    }

    /// <summary>
    /// Logically deletes the visit, from any state, leaving <see cref="Status"/> alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Medical records under an audit-trail requirement are not physically
    /// deleted; and a deleted visit that forgot how far it had progressed would
    /// make the audit log harder to read, not easier.
    /// </para>
    /// <para>
    /// Deleting twice throws rather than succeeding quietly. A second delete
    /// would overwrite <see cref="DeletedByUserId"/> and <see cref="DeletedAt"/>,
    /// losing the identity of whoever deleted the record first — which is the
    /// single worst thing to lose in a system built around an audit trail.
    /// </para>
    /// </remarks>
    /// <param name="byUserId">Who deleted it.</param>
    /// <param name="now">The current time, supplied by the caller so the result is deterministic.</param>
    /// <exception cref="DomainException">The visit has already been deleted.</exception>
    public void SoftDelete(Guid byUserId, DateTimeOffset now)
    {
        EnsureNotDeleted();

        IsDeleted = true;
        DeletedAt = now;
        DeletedByUserId = byUserId;
    }

    /// <summary>
    /// A deleted visit is frozen: it is no longer a live episode of care, so it
    /// accepts no further changes.
    /// </summary>
    /// <remarks>
    /// The persistence layer also filters deleted visits out of every query,
    /// which means in practice a deleted visit is rarely even loaded. That is
    /// the reason this guard exists rather than a reason it does not: "it
    /// cannot happen in practice" is the sort of reasoning that leaves an
    /// aggregate unable to defend its own invariants.
    /// </remarks>
    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new DomainException($"Visit '{Id}' has been deleted and can no longer be modified.");
        }
    }
}
