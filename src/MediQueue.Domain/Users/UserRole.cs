namespace MediQueue.Domain.Users;

/// <summary>
/// The two roles the practice recognises. A user has exactly one.
/// </summary>
/// <remarks>
/// The values are explicit because they are persisted: renumbering them would
/// silently change the meaning of every row already in the database.
/// </remarks>
public enum UserRole
{
    /// <summary>Registers patients and routes them to a specialty. Never sees a diagnosis.</summary>
    Assistant = 1,

    /// <summary>Calls in patients from their own queue, records diagnoses, and releases them.</summary>
    Doctor = 2,
}
