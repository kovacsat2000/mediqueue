namespace MediQueue.Contracts.Visits;

/// <summary>
/// The payload of the <c>VisitDeleted</c> push message.
/// </summary>
/// <remarks>
/// Identifiers only. The visit is gone, so there is nothing left to project and
/// nothing a client needs beyond which row to remove and which queue to remove
/// it from. It lives in <c>Contracts</c> rather than beside the hub because it
/// is a wire type: the desktop clients deserialise it, and they depend on this
/// project without dragging in the server.
/// </remarks>
/// <param name="VisitId">The visit that was withdrawn.</param>
/// <param name="DoctorId">Whose queue it was in, or <c>null</c> if it was in nobody's.</param>
public sealed record VisitDeletedPayload(Guid VisitId, Guid? DoctorId);
