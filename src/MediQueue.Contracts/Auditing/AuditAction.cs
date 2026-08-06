namespace MediQueue.Contracts.Auditing;

/// <summary>
/// What happened to a record, as it travels on the wire.
/// </summary>
/// <remarks>
/// Mirrors <c>MediQueue.Domain.Auditing.AuditAction</c> deliberately, with the
/// same members and the same numeric values, pinned by a test. Same arrangement
/// and same reason as <see cref="UserRole"/>: a desktop client depends on the
/// contract without dragging in the domain model.
/// </remarks>
public enum AuditAction
{
    /// <summary>The record was brought into existence.</summary>
    Create = 1,

    /// <summary>One or more of the record's fields changed.</summary>
    Update = 2,

    /// <summary>The record was withdrawn.</summary>
    Delete = 3,
}
