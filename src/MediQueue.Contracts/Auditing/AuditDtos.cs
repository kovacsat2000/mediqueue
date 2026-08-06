namespace MediQueue.Contracts.Auditing;

/// <summary>
/// One field's movement, as the caller is entitled to see it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the one place in the system where what a role may see is
/// decided by a runtime branch rather than by the shape of a type.</strong>
/// Everywhere else — <c>VisitSummaryDto</c> above all — the field an assistant
/// may not see simply does not exist on the type they receive, which makes the
/// leak unwriteable. That mechanism cannot work here: a doctor and an assistant
/// read the <em>same</em> audit entry, and the same field must be present for
/// one and withheld from the other.
/// </para>
/// <para>
/// What compensates: the branch is written in exactly one place
/// (<c>AuditMapper</c>), and it is pinned by an integration test that asserts on
/// the raw JSON rather than on a deserialised object — because deserialising
/// into this type would discard or default the field and pass happily against a
/// leaking server.
/// </para>
/// </remarks>
/// <param name="FieldName">The property that changed.</param>
/// <param name="OldValue">What it held before, or <c>***</c> if withheld.</param>
/// <param name="NewValue">What it holds now, or <c>***</c> if withheld.</param>
/// <param name="Redacted">
/// Whether the values were withheld from this caller. A real field rather than
/// an inference from the value being <c>***</c>: a client must be able to render
/// "hidden" instead of displaying three asterisks as though they were the data.
/// </param>
public sealed record AuditFieldChangeDto(string FieldName, string? OldValue, string? NewValue, bool Redacted);

/// <summary>One data modification, with the fields that moved.</summary>
/// <param name="Id">The entry.</param>
/// <param name="OccurredAt">When the change was committed, in UTC.</param>
/// <param name="UserId">Who made it, or <c>null</c> for a system operation.</param>
/// <param name="Action">What happened, in business terms.</param>
/// <param name="EntityType">The kind of record that changed.</param>
/// <param name="EntityId">Which record of that kind.</param>
/// <param name="PatientId">The patient concerned, if any.</param>
/// <param name="Changes">The fields that moved.</param>
public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? UserId,
    AuditAction Action,
    string EntityType,
    Guid EntityId,
    Guid? PatientId,
    IReadOnlyList<AuditFieldChangeDto> Changes);

/// <summary>One page of audit entries, newest first.</summary>
/// <remarks>
/// The total travels with the page so a client can render "page 2 of 7" without
/// a second call, and so a caller can tell an empty page from an empty log.
/// </remarks>
/// <param name="Items">The entries on this page.</param>
/// <param name="Page">Which page this is, one-based.</param>
/// <param name="PageSize">How many entries a page holds, after clamping.</param>
/// <param name="TotalCount">How many entries match the filter in total.</param>
public sealed record AuditPageDto(
    IReadOnlyList<AuditEntryDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
