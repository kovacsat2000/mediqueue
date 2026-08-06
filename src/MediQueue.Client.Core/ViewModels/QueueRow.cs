namespace MediQueue.Client.Core.ViewModels;

/// <summary>One row of a doctor's waiting list, ready to render.</summary>
/// <remarks>
/// The time is already a string, formatted in the configured zone by the view
/// model. A view that formatted it would be the second place a time zone is
/// applied, and the first place is the only one that can be tested.
/// </remarks>
/// <param name="PatientFullName">Who is waiting.</param>
/// <param name="Taj">Their TAJ number, in the dashed form.</param>
/// <param name="Complaint">What they came in with.</param>
/// <param name="QueuedAtDisplay">When they joined the queue, in local time.</param>
/// <param name="Status">How far the visit has progressed, for display.</param>
public sealed record QueueRow(
    string PatientFullName,
    string Taj,
    string Complaint,
    string QueuedAtDisplay,
    string Status);
