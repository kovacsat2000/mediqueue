namespace MediQueue.Domain.Scheduling;

/// <summary>
/// A snapshot of how busy one doctor is, which is all the assignment strategy
/// needs to know about them.
/// </summary>
/// <remarks>
/// The strategy is handed these rather than entities or a database query, which
/// is what keeps it a pure function and therefore trivially testable.
/// </remarks>
/// <param name="DoctorId">The doctor.</param>
/// <param name="WaitingCount">How many visits are queued for them.</param>
/// <param name="InTreatmentCount">How many visits they currently have in treatment.</param>
/// <param name="LastAssignedAt">When they were last given a patient, or <c>null</c> if never.</param>
public sealed record DoctorWorkload(
    Guid DoctorId,
    int WaitingCount,
    int InTreatmentCount,
    DateTimeOffset? LastAssignedAt);
