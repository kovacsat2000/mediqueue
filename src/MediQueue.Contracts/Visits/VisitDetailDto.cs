namespace MediQueue.Contracts.Visits;

/// <summary>
/// A visit as the doctor treating it may see it.
/// </summary>
/// <remarks>
/// Every member of <see cref="VisitSummaryDto"/> is spelled out again here,
/// plus the two an assistant may not have. That duplication is the point rather
/// than an oversight — see the remarks on the summary type. This one is returned
/// only from doctor-scoped endpoints, and only for a visit assigned to the
/// caller.
/// </remarks>
/// <param name="Id">The visit.</param>
/// <param name="PatientId">The patient this visit belongs to.</param>
/// <param name="PatientFullName">The patient's name.</param>
/// <param name="Taj">The patient's TAJ number, in the dashed form.</param>
/// <param name="PatientAddress">Where the patient lives.</param>
/// <param name="Complaint">What the patient came in with.</param>
/// <param name="SpecialtyId">The specialty routed to, or <c>null</c> before assignment.</param>
/// <param name="SpecialtyName">That specialty's name, or <c>null</c> before assignment.</param>
/// <param name="DoctorId">The doctor whose queue this is in, or <c>null</c> before assignment.</param>
/// <param name="DoctorFullName">That doctor's name, or <c>null</c> before assignment.</param>
/// <param name="Status">How far the visit has progressed.</param>
/// <param name="Diagnosis">What the doctor found. Never leaves the server for an assistant.</param>
/// <param name="RegisteredAt">When the assistant recorded the visit.</param>
/// <param name="QueuedAt">When it joined a doctor's queue.</param>
/// <param name="CalledInAt">When the doctor called the patient in.</param>
/// <param name="CompletedAt">When the patient was released.</param>
public sealed record VisitDetailDto(
    Guid Id,
    Guid PatientId,
    string PatientFullName,
    string Taj,
    string PatientAddress,
    string Complaint,
    Guid? SpecialtyId,
    string? SpecialtyName,
    Guid? DoctorId,
    string? DoctorFullName,
    VisitStatus Status,
    string? Diagnosis,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? QueuedAt,
    DateTimeOffset? CalledInAt,
    DateTimeOffset? CompletedAt);
