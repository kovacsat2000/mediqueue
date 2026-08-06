namespace MediQueue.Contracts.Visits;

/// <summary>
/// How far a visit has progressed, as it travels on the wire.
/// </summary>
/// <remarks>
/// Mirrors <c>MediQueue.Domain.Visits.VisitStatus</c> deliberately, with the
/// same members and the same numeric values, pinned by a test. Same arrangement
/// and same reason as <see cref="UserRole"/>: a desktop client depends on the
/// contract without dragging in the domain model.
/// </remarks>
public enum VisitStatus
{
    /// <summary>Recorded by an assistant; no specialty chosen yet.</summary>
    Registered = 1,

    /// <summary>In a doctor's queue, waiting to be called in.</summary>
    Waiting = 2,

    /// <summary>Called in; the consultation is under way.</summary>
    InTreatment = 3,

    /// <summary>Finished and released. Terminal.</summary>
    Done = 4,
}
