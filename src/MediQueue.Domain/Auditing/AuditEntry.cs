using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Validation;

namespace MediQueue.Domain.Auditing;

/// <summary>
/// One data modification: who changed what, when, and from what to what.
/// </summary>
/// <remarks>
/// <para>
/// One entry per entity per transaction, carrying one
/// <see cref="AuditFieldChange"/> per property that actually moved. A visit
/// update that touches two properties is one entry with two changes, not two
/// entries — because it was one action.
/// </para>
/// <para>
/// <strong><see cref="UserId"/> is nullable, and that is a deliberate risk
/// accepted rather than a gap.</strong> An entry whose actor could not be
/// determined is still written, and the interceptor logs a warning. The
/// alternative — skipping the entry — would turn a broken identity pipeline
/// into an audit log that is silently *empty* rather than silently anonymous,
/// which is strictly worse in a system whose whole point is to record who did
/// what.
/// </para>
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>The longest entity type name the system records. The database column is sized from this.</summary>
    public const int MaxEntityTypeLength = 100;

    private readonly List<AuditFieldChange> _changes = [];

    private AuditEntry(
        Guid id,
        DateTimeOffset occurredAt,
        Guid? userId,
        AuditAction action,
        string entityType,
        Guid entityId,
        Guid? patientId)
    {
        Id = id;
        OccurredAt = occurredAt;
        UserId = userId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        PatientId = patientId;
    }

    /// <summary>The identifier. Time-ordered, so index pages stay dense as rows are inserted.</summary>
    public Guid Id { get; private set; }

    /// <summary>When the change was committed, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// Who made the change, or <c>null</c> for a system operation and for a
    /// request whose identity could not be read.
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>What happened, in business terms.</summary>
    public AuditAction Action { get; private set; }

    /// <summary>The kind of record that changed, by its domain type name.</summary>
    public string EntityType { get; private set; }

    /// <summary>Which record of that kind changed.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>
    /// The patient this change concerns, denormalised.
    /// </summary>
    /// <remarks>
    /// The patient's own id for a change to a patient, the visit's patient for a
    /// change to a visit, and <c>null</c> for anything that concerns no patient.
    /// "Show me everything that happened to this patient" is a filter the
    /// specification asks for by name; carrying the id here makes it one indexed
    /// query instead of a join whose shape depends on the entity type.
    /// </remarks>
    public Guid? PatientId { get; private set; }

    /// <summary>The fields that moved. Never empty in practice — an entry with no changes is not written.</summary>
    public IReadOnlyList<AuditFieldChange> Changes => _changes;

    /// <summary>Opens an entry for one entity's change.</summary>
    /// <param name="action">What happened.</param>
    /// <param name="entityType">The kind of record.</param>
    /// <param name="entityId">Which record.</param>
    /// <param name="userId">Who did it, if known.</param>
    /// <param name="patientId">The patient concerned, if any.</param>
    /// <param name="now">The current time, supplied by the caller so the result is deterministic.</param>
    /// <returns>The entry, with no changes attached yet.</returns>
    /// <exception cref="ValidationException"><paramref name="entityType"/> is blank or too long.</exception>
    /// <exception cref="DomainException"><paramref name="entityId"/> is the default value.</exception>
    public static AuditEntry For(
        AuditAction action,
        string entityType,
        Guid entityId,
        Guid? userId,
        Guid? patientId,
        DateTimeOffset now)
    {
        // An entry that names no record is not evidence of anything, and it
        // would be indistinguishable from a row whose id was never written.
        if (entityId == Guid.Empty)
        {
            throw new DomainException("An audit entry must name the record it describes.");
        }

        return new AuditEntry(
            Guid.CreateVersion7(now),
            now,
            userId,
            action,
            TextRules.Required(entityType, nameof(EntityType), MaxEntityTypeLength),
            entityId,
            patientId);
    }

    /// <summary>Attaches one field's movement to this entry.</summary>
    /// <param name="change">The change.</param>
    /// <exception cref="ArgumentNullException"><paramref name="change"/> is null.</exception>
    public void Add(AuditFieldChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        change.BelongsTo(Id);
        _changes.Add(change);
    }
}
