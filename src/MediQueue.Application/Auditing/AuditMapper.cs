using MediQueue.Contracts;
using MediQueue.Contracts.Auditing;
using DomainAuditAction = MediQueue.Domain.Auditing.AuditAction;
using DomainAuditEntry = MediQueue.Domain.Auditing.AuditEntry;
using DomainFieldChange = MediQueue.Domain.Auditing.AuditFieldChange;
using WireAuditAction = MediQueue.Contracts.Auditing.AuditAction;

namespace MediQueue.Application.Auditing;

/// <summary>
/// Projects audit entries for the role reading them, and redacts what that role
/// may not see.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This file contains the only runtime security branch in the
/// system.</strong> Everywhere else the guarantee is structural: an assistant
/// receives <c>VisitSummaryDto</c>, which declares no diagnosis member, so the
/// leak cannot be written. That mechanism cannot reach the audit log, because a
/// doctor and an assistant read the <em>same</em> entry and the same field must
/// be present for one and withheld from the other. No shape of type decides
/// that; only a branch does.
/// </para>
/// <para>
/// Three things compensate, and all three are load-bearing:
/// </para>
/// <list type="number">
/// <item>
/// The branch is written <strong>once</strong>, in <see cref="Reveal"/>. If
/// redaction appeared in two methods, one of them would eventually be edited
/// and the other would not.
/// </item>
/// <item>
/// It is pinned by an integration test asserting on <strong>raw JSON</strong>,
/// not on a deserialised object — deserialising into
/// <see cref="AuditFieldChangeDto"/> would discard or default the very field
/// that leaked, and pass cheerfully against a broken server.
/// </item>
/// <item>
/// The README says so plainly, so nobody has to discover it.
/// </item>
/// </list>
/// <para>
/// Doctors see sensitive values. The specification's role split is about
/// <em>assistants</em> not seeing diagnoses, and clinical staff reading a
/// patient's history is what a medical record is for. Per-visit ownership was
/// considered and rejected: a patient's history spans doctors, and this query
/// filters by patient and date, not by whose queue a visit sits in.
/// </para>
/// </remarks>
public static class AuditMapper
{
    /// <summary>What a caller who may not see a value is shown instead.</summary>
    public const string Redaction = "***";

    /// <summary>Projects one page for the caller's role.</summary>
    /// <param name="entries">The page, already ordered and paged.</param>
    /// <param name="totalCount">How many entries matched the filter in total.</param>
    /// <param name="page">Which page this is.</param>
    /// <param name="pageSize">The clamped page size.</param>
    /// <param name="role">The caller's role, or <c>null</c> if unknown.</param>
    /// <returns>The page, redacted where the role requires it.</returns>
    public static AuditPageDto ToPage(
        IEnumerable<DomainAuditEntry> entries,
        int totalCount,
        int page,
        int pageSize,
        UserRole? role)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return new AuditPageDto(
            [.. entries.Select(entry => entry.ToDto(role))],
            page,
            pageSize,
            totalCount);
    }

    /// <summary>Projects one entry for the caller's role.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="role">The caller's role, or <c>null</c> if unknown.</param>
    /// <returns>The entry, redacted where the role requires it.</returns>
    public static AuditEntryDto ToDto(this DomainAuditEntry entry, UserRole? role)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new AuditEntryDto(
            entry.Id,
            entry.OccurredAt,
            entry.UserId,
            entry.Action.ToWire(),
            entry.EntityType,
            entry.EntityId,
            entry.PatientId,
            [.. entry.Changes.Select(change => change.ToDto(role))]);
    }

    /// <summary>Projects one field change for the caller's role.</summary>
    private static AuditFieldChangeDto ToDto(this DomainFieldChange change, UserRole? role)
    {
        var withhold = change.IsSensitive && !Reveal(role);

        return new AuditFieldChangeDto(
            change.FieldName,
            withhold ? Redaction : change.OldValue,
            withhold ? Redaction : change.NewValue,
            withhold);
    }

    /// <summary>
    /// Whether this role may read clinical values verbatim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single sentence the whole guarantee rests on, deliberately given its
    /// own name so that it is greppable and so that the answer is written in
    /// exactly one place.
    /// </para>
    /// <para>
    /// Stated as "only a doctor may" rather than "an assistant may not", so an
    /// unrecognised or absent role fails closed. A future third role would be
    /// redacted until somebody decided otherwise, which is the correct default
    /// for the field this protects.
    /// </para>
    /// </remarks>
    private static bool Reveal(UserRole? role) => role == UserRole.Doctor;

    /// <summary>Maps the domain action onto the wire action.</summary>
    /// <remarks>
    /// A switch rather than a cast, so adding an action to the domain is a
    /// compile error here instead of a number quietly reinterpreted on the wire.
    /// </remarks>
    /// <param name="action">The domain action.</param>
    /// <returns>The wire action.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The action is not one this system knows.</exception>
    public static WireAuditAction ToWire(this DomainAuditAction action) => action switch
    {
        DomainAuditAction.Create => WireAuditAction.Create,
        DomainAuditAction.Update => WireAuditAction.Update,
        DomainAuditAction.Delete => WireAuditAction.Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown audit action."),
    };
}
