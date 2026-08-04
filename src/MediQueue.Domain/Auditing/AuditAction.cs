namespace MediQueue.Domain.Auditing;

/// <summary>
/// The kind of modification an audit entry records.
/// </summary>
/// <remarks>The values are explicit because they are persisted.</remarks>
public enum AuditAction
{
    /// <summary>A new entity was added.</summary>
    Create = 1,

    /// <summary>An existing entity had one or more fields changed.</summary>
    Update = 2,

    /// <summary>An entity was logically deleted.</summary>
    Delete = 3,
}
