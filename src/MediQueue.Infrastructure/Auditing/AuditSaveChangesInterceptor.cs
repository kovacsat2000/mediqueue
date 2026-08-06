using System.Collections.Concurrent;
using System.Reflection;
using MediQueue.Application.Abstractions;
using MediQueue.Domain.Auditing;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MediQueue.Infrastructure.Auditing;

/// <summary>
/// Writes the audit trail, from the change tracker, in the same transaction as
/// the change it describes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is an interceptor and not a call in each use case because a use
/// case can forget, and this one must not.</strong> That is the entire
/// justification. A system whose specification requires a record of who changed
/// what cannot have that record depend on every future author remembering to
/// ask for it; here, a new use case is audited because it saves, and a new
/// entity is audited because it is not one of the two excluded types.
/// </para>
/// <para>
/// It runs inside the same <c>SaveChanges</c> as the business change, so the
/// entry and the thing it describes commit or roll back together. There is
/// exactly one save per business action (D-38), so there is exactly one audit
/// boundary per action.
/// </para>
/// </remarks>
public sealed class AuditSaveChangesInterceptor(
    ICurrentUser currentUser,
    AuditSuppression suppression,
    TimeProvider timeProvider,
    ILogger<AuditSaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    /// <summary>
    /// Whether a property's values are clinical data, cached per property.
    /// </summary>
    /// <remarks>
    /// The answer is a compile-time fact — an attribute on a property — so
    /// reading it once per property rather than once per row is free
    /// correctness. Concurrent because the interceptor is scoped and several
    /// requests hash into it at the same time.
    /// </remarks>
    private static readonly ConcurrentDictionary<(Type Entity, string Property), bool> SensitiveProperties = new();

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Capture(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Capture(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Turns everything the change tracker is about to write into audit entries,
    /// and adds them to the same save.
    /// </summary>
    private void Capture(DbContext? database)
    {
        if (database is null || suppression.IsSuppressed)
        {
            return;
        }

        var occurredAt = timeProvider.GetUtcNow();
        var actor = currentUser.UserId;

        // Never "no user, no entry". An entry with an unknown actor is worth far
        // more than no entry: it is loud, and something read it aloud. See
        // AuditSuppression for why the two must not be the same mechanism.
        if (actor is null)
        {
            logger.LogWarning(
                "Writing audit entries with no actor: the request carries no user identity. "
                + "This is expected only for anonymous or system-initiated writes.");
        }

        // Materialised before anything is added, because adding to the context
        // while enumerating its entries would invalidate the enumerator — and
        // because the entries created below must not be audited by this pass.
        var tracked = database.ChangeTracker.Entries().ToList();

        var entries = new List<AuditEntry>();

        foreach (var tracker in tracked)
        {
            if (Describe(tracker, actor, occurredAt) is { } entry)
            {
                entries.Add(entry);
            }
        }

        foreach (var entry in entries)
        {
            database.Add(entry);
        }
    }

    /// <summary>Builds the entry for one tracked entity, or nothing if it needs none.</summary>
    private static AuditEntry? Describe(EntityEntry tracker, Guid? actor, DateTimeOffset occurredAt)
    {
        // Excluded by type rather than by a list somebody maintains: a new
        // entity is audited by default, and forgetting to add it is impossible
        // rather than merely unlikely. These two are excluded because auditing
        // the audit trail is circular, and on a retried save it would be
        // recursive.
        if (tracker.Entity is AuditEntry or AuditFieldChange)
        {
            return null;
        }

        var action = ActionOf(tracker);

        if (action is null)
        {
            return null;
        }

        var changes = ChangedProperties(tracker, occurredAt).ToList();

        // A save that touched nothing is not an event. This also keeps a
        // no-op update — one that assigns a property its existing value — out
        // of the log entirely rather than as an entry with no changes.
        if (changes.Count == 0)
        {
            return null;
        }

        var entry = AuditEntry.For(
            action.Value,
            tracker.Metadata.ClrType.Name,
            IdentityOf(tracker),
            actor,
            PatientOf(tracker.Entity),
            occurredAt);

        foreach (var change in changes)
        {
            entry.Add(change);
        }

        return entry;
    }

    /// <summary>
    /// What happened, in business terms rather than in column terms.
    /// </summary>
    /// <remarks>
    /// A soft delete arrives as an update that sets <c>IsDeleted</c>, and is
    /// recorded as <see cref="AuditAction.Delete"/>. The log should say the
    /// visit was withdrawn, not that a boolean moved.
    /// </remarks>
    private static AuditAction? ActionOf(EntityEntry tracker) => tracker.State switch
    {
        EntityState.Added => AuditAction.Create,
        EntityState.Deleted => AuditAction.Delete,
        EntityState.Modified when IsBeingSoftDeleted(tracker) => AuditAction.Delete,
        EntityState.Modified => AuditAction.Update,
        _ => null,
    };

    private static bool IsBeingSoftDeleted(EntityEntry tracker) =>
        tracker.Metadata.FindProperty(nameof(Visit.IsDeleted)) is not null
        && tracker.Property(nameof(Visit.IsDeleted)) is { IsModified: true, CurrentValue: true };

    /// <summary>The properties that actually moved, as audit changes.</summary>
    /// <remarks>
    /// A property whose value did not change does not get a row: an update that
    /// sets one field would otherwise bury it under a dozen unchanged ones, and
    /// the log would grow without becoming more informative.
    /// </remarks>
    private static IEnumerable<AuditFieldChange> ChangedProperties(
        EntityEntry tracker,
        DateTimeOffset occurredAt)
    {
        // A create has nothing before it, and a row leaving the table has
        // nothing after it. A soft delete is neither: it is an update that the
        // log labels Delete, so its values are read the ordinary way and the
        // entry records that IsDeleted moved and who moved it.
        var isCreate = tracker.State == EntityState.Added;
        var isRemoval = tracker.State == EntityState.Deleted;

        foreach (var property in tracker.Properties)
        {
            // Shadow properties have no domain field to name. In this schema
            // that is the xmin concurrency token, which the database owns and
            // which changes on every write by definition.
            if (property.Metadata.IsShadowProperty())
            {
                continue;
            }

            var before = isCreate ? null : Render(property.OriginalValue);
            var after = isRemoval ? null : Render(property.CurrentValue);

            if (before == after)
            {
                continue;
            }

            yield return AuditFieldChange.Record(
                property.Metadata.Name,
                before,
                after,
                IsSensitive(tracker.Metadata.ClrType, property.Metadata.Name),
                occurredAt);
        }
    }

    /// <summary>
    /// Renders a value the way the domain spells it.
    /// </summary>
    /// <remarks>
    /// The values come from the <see cref="PropertyEntry"/>, so a converted
    /// property yields its model value and not its column value: a
    /// <c>TajNumber</c> renders as <c>123-456-788</c> and a <c>PatientName</c>
    /// in its composed form, which is what a human reading the log expects to
    /// see.
    /// </remarks>
    private static string? Render(object? value) => value?.ToString();

    /// <summary>Whether a property carries clinical data, from its attribute.</summary>
    private static bool IsSensitive(Type entityType, string propertyName) =>
        SensitiveProperties.GetOrAdd(
            (entityType, propertyName),
            key => key.Entity
                .GetProperty(key.Property, BindingFlags.Public | BindingFlags.Instance)
                ?.GetCustomAttribute<SensitiveAuditAttribute>() is not null);

    /// <summary>
    /// The patient a change concerns, denormalised onto the entry.
    /// </summary>
    /// <remarks>
    /// "Show me everything that happened to this patient" is a filter the
    /// specification asks for by name. Carrying the id here makes it one indexed
    /// query rather than a join whose shape depends on which entity type the
    /// row describes.
    /// </remarks>
    private static Guid? PatientOf(object entity) => entity switch
    {
        Patient patient => patient.Id,
        Visit visit => visit.PatientId,
        _ => null,
    };

    /// <summary>The primary key of the entity being described.</summary>
    private static Guid IdentityOf(EntityEntry tracker) =>
        tracker.Metadata.FindPrimaryKey()?.Properties is [{ } key]
        && tracker.Property(key.Name).CurrentValue is Guid id
            ? id
            : Guid.Empty;
}
