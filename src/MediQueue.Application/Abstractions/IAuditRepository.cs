using MediQueue.Domain.Auditing;

namespace MediQueue.Application.Abstractions;

/// <summary>
/// A page of the audit trail, and how many entries the filter matched in total.
/// </summary>
/// <param name="Entries">The page, newest first.</param>
/// <param name="TotalCount">How many entries match the filter, ignoring paging.</param>
public sealed record AuditPage(IReadOnlyList<AuditEntry> Entries, int TotalCount);

/// <summary>Reads the audit trail.</summary>
/// <remarks>
/// Entities in, entities out. The redaction that makes this log safe to expose
/// happens in <c>Application</c> when the entities become DTOs, because that
/// projection is a security boundary and belongs where it can be read in one
/// file (D-19, D-38).
/// </remarks>
public interface IAuditRepository
{
    /// <summary>Reads one page of the trail.</summary>
    /// <param name="query">The filter and the page, already validated and clamped.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The page and the total.</returns>
    Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken);
}
