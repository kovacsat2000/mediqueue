namespace MediQueue.Domain.Visits;

/// <summary>
/// Where a visit has got to. Exactly four states, and the only legal moves
/// between them are the three described by <see cref="VisitStateMachine"/>.
/// </summary>
/// <remarks>
/// Deletion is deliberately absent: it is a flag on the visit, orthogonal to
/// status, so that a deleted visit still remembers how far it had progressed.
/// The values are explicit because they are persisted.
/// </remarks>
public enum VisitStatus
{
    /// <summary>Recorded by an assistant; no specialty chosen yet, so nobody is waiting for anyone.</summary>
    Registered = 1,

    /// <summary>Assigned to a doctor's queue and waiting to be called in.</summary>
    Waiting = 2,

    /// <summary>The doctor has called the patient in and the consultation is under way.</summary>
    InTreatment = 3,

    /// <summary>The consultation is finished and the patient has been released. Terminal.</summary>
    Done = 4,
}
