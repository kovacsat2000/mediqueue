namespace MediQueue.Contracts.Visits;

/// <summary>One doctor's waiting list.</summary>
/// <remarks>
/// Carries the summary projection, never the detail one — this is an
/// assistant-facing shape, and a queue is exactly the sort of place a diagnosis
/// would leak from if the type allowed it.
/// </remarks>
/// <param name="DoctorId">Whose queue this is.</param>
/// <param name="DoctorFullName">Their name.</param>
/// <param name="SpecialtyId">The specialty they practise.</param>
/// <param name="SpecialtyName">That specialty's name.</param>
/// <param name="Visits">The queue, in arrival order. May be empty; an empty queue is information.</param>
public sealed record QueueDto(
    Guid DoctorId,
    string DoctorFullName,
    Guid SpecialtyId,
    string SpecialtyName,
    IReadOnlyList<VisitSummaryDto> Visits);
