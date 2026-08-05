namespace MediQueue.Contracts;

/// <summary>
/// The role a signed-in user holds, as it travels on the wire.
/// </summary>
/// <remarks>
/// This deliberately duplicates <c>MediQueue.Domain.Users.UserRole</c>. The two
/// have identical members and identical numeric values, and a test asserts that
/// — but a desktop client must be able to depend on the contract without
/// dragging in the domain model. A contract and a domain concept are allowed to
/// look the same; they are not allowed to be the same type.
/// </remarks>
public enum UserRole
{
    /// <summary>Registers patients and routes them to a specialty. Never sees a diagnosis.</summary>
    Assistant = 1,

    /// <summary>Calls in patients from their own queue, records diagnoses, and releases them.</summary>
    Doctor = 2,
}
