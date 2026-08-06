namespace MediQueue.Contracts.Visits;

/// <summary>
/// A visit as an assistant may see it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This type declares no diagnosis member, and must never gain one.</strong>
/// It is what every assistant-facing endpoint returns and, from P6, the payload
/// of every push message. The absence of the field is the security mechanism:
/// "remember to strip the diagnosis" is a rule that can be forgotten in a new
/// endpoint, a new event or a new query, whereas a type that cannot carry the
/// field cannot leak it. That converts a policy into a compile-time guarantee.
/// </para>
/// <para>
/// Do not make this a base of <see cref="VisitDetailDto"/> and do not embed it
/// there. The duplication is deliberate: with inheritance, a member added here
/// would silently appear on the detail type, and — far worse — the arrangement
/// invites someone to try it the other way round.
/// </para>
/// <para>
/// The complaint is here on purpose. The assistant wrote it, so it is not
/// something they are being shown; it is something they are being shown back.
/// </para>
/// </remarks>
/// <param name="Id">The visit.</param>
/// <param name="PatientId">The patient this visit belongs to.</param>
/// <param name="PatientFullName">The patient's name.</param>
/// <param name="Taj">The patient's TAJ number, in the dashed form.</param>
/// <param name="Complaint">What the patient came in with.</param>
/// <param name="SpecialtyId">The specialty routed to, or <c>null</c> before assignment.</param>
/// <param name="SpecialtyName">That specialty's name, or <c>null</c> before assignment.</param>
/// <param name="DoctorId">The doctor whose queue this is in, or <c>null</c> before assignment.</param>
/// <param name="DoctorFullName">That doctor's name, or <c>null</c> before assignment.</param>
/// <param name="Status">How far the visit has progressed.</param>
/// <param name="RegisteredAt">When the assistant recorded the visit.</param>
/// <param name="QueuedAt">When it joined a doctor's queue. Queues are ordered by this.</param>
/// <param name="CalledInAt">When the doctor called the patient in.</param>
/// <param name="CompletedAt">When the patient was released.</param>
public sealed record VisitSummaryDto(
    Guid Id,
    Guid PatientId,
    string PatientFullName,
    string Taj,
    string Complaint,
    Guid? SpecialtyId,
    string? SpecialtyName,
    Guid? DoctorId,
    string? DoctorFullName,
    VisitStatus Status,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? QueuedAt,
    DateTimeOffset? CalledInAt,
    DateTimeOffset? CompletedAt);
