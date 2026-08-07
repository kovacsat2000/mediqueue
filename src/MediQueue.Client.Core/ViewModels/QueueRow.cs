namespace MediQueue.Client.Core.ViewModels;

/// <summary>One row of a doctor's waiting list, ready to render.</summary>
/// <remarks>
/// The time is already a string, formatted in the configured zone by the view
/// model. A view that formatted it would be the second place a time zone is
/// applied, and the first place is the only one that can be tested.
/// </remarks>
/// <param name="VisitId">
/// Which visit this row is. Carried so that a pushed update can find the row it
/// concerns without matching on a displayed string.
/// </param>
/// <param name="QueuedAt">
/// The raw instant the visit joined the queue, kept beside the formatted one
/// solely as the sort key — a pushed row has to be inserted in the right place,
/// and sorting formatted "HH:mm" strings would put yesterday before today.
/// </param>
/// <param name="PatientFullName">Who is waiting.</param>
/// <param name="Taj">Their TAJ number, in the dashed form.</param>
/// <param name="Complaint">What they came in with.</param>
/// <param name="QueuedAtDisplay">When they joined the queue, in local time.</param>
/// <param name="Status">How far the visit has progressed, for display.</param>
public sealed record QueueRow(
    Guid VisitId,
    DateTimeOffset? QueuedAt,
    string PatientFullName,
    string Taj,
    string Complaint,
    string QueuedAtDisplay,
    string Status);
