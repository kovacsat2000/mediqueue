namespace MediQueue.Domain.Auditing;

/// <summary>
/// What happened to a record, in business terms.
/// </summary>
/// <remarks>
/// <para>
/// A soft delete is <see cref="Delete"/>, not <see cref="Update"/>. The audit
/// log records what happened to the <em>record</em>, not what happened to a
/// column: "Horváth Anna withdrew this visit" is the fact worth keeping, and
/// "IsDeleted went from false to true" is an implementation detail of how the
/// system remembers it.
/// </para>
/// <para>
/// The values are explicit because they are persisted, and mirrored in
/// <c>MediQueue.Contracts.Auditing.AuditAction</c> with a test pinning the
/// numbers on both sides.
/// </para>
/// </remarks>
public enum AuditAction
{
    /// <summary>The record was brought into existence.</summary>
    Create = 1,

    /// <summary>One or more of the record's fields changed.</summary>
    Update = 2,

    /// <summary>The record was withdrawn. Logically, in this system — the row survives.</summary>
    Delete = 3,
}
