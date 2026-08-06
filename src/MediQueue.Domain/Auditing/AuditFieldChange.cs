using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Validation;

namespace MediQueue.Domain.Auditing;

/// <summary>
/// One field moving from one value to another, inside a single
/// <see cref="AuditEntry"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Sensitivity is per field, not per entry.</strong> A doctor who
/// records a diagnosis while the visit is already in treatment produces one
/// entry whose <c>Diagnosis</c> change is sensitive and whose other changes are
/// not. Marking the whole entry would hide the ordinary fields along with the
/// clinical one, and an assistant is entitled to see that a visit changed and
/// who changed it.
/// </para>
/// <para>
/// This type lives in the domain because <see cref="IsSensitive"/> is a business
/// rule about clinical data — <em>an assistant may see that a diagnosis
/// changed, and by whom, but not to what</em> — and not a storage detail.
/// </para>
/// </remarks>
public sealed class AuditFieldChange
{
    /// <summary>The longest field name the system records. The database column is sized from this.</summary>
    public const int MaxFieldNameLength = 100;

    private AuditFieldChange(Guid id, string fieldName, string? oldValue, string? newValue, bool isSensitive)
    {
        Id = id;
        FieldName = fieldName;
        OldValue = oldValue;
        NewValue = newValue;
        IsSensitive = isSensitive;
    }

    /// <summary>The identifier. Time-ordered, so index pages stay dense as rows are inserted.</summary>
    public Guid Id { get; private set; }

    /// <summary>The entry this change belongs to.</summary>
    public Guid AuditEntryId { get; private set; }

    /// <summary>The property that changed, by its domain name.</summary>
    public string FieldName { get; private set; }

    /// <summary>What it held before. <c>null</c> on a create, or if the value itself was null.</summary>
    public string? OldValue { get; private set; }

    /// <summary>What it holds now. <c>null</c> if the value itself is null.</summary>
    public string? NewValue { get; private set; }

    /// <summary>
    /// Whether the values must be withheld from a caller not entitled to the
    /// property itself.
    /// </summary>
    /// <remarks>
    /// Set from the <see cref="SensitiveAuditAttribute"/> on the property when
    /// the change is captured, so the rule travels with the property it
    /// describes rather than being restated wherever the log is read.
    /// </remarks>
    public bool IsSensitive { get; private set; }

    /// <summary>Records one field's movement.</summary>
    /// <param name="fieldName">The property that changed.</param>
    /// <param name="oldValue">What it held before.</param>
    /// <param name="newValue">What it holds now.</param>
    /// <param name="isSensitive">Whether the values are clinical data.</param>
    /// <param name="now">The current time, supplied by the caller so the result is deterministic.</param>
    /// <returns>The change.</returns>
    /// <exception cref="ValidationException"><paramref name="fieldName"/> is blank or too long.</exception>
    public static AuditFieldChange Record(
        string fieldName,
        string? oldValue,
        string? newValue,
        bool isSensitive,
        DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(now),
            TextRules.Required(fieldName, nameof(FieldName), MaxFieldNameLength),
            oldValue,
            newValue,
            isSensitive);

    /// <summary>Attaches this change to its entry.</summary>
    /// <remarks>
    /// Called by <see cref="AuditEntry.Add"/> rather than by the caller, so a
    /// change cannot be recorded against an entry it was not added to.
    /// </remarks>
    /// <param name="auditEntryId">The owning entry.</param>
    internal void BelongsTo(Guid auditEntryId) => AuditEntryId = auditEntryId;
}
