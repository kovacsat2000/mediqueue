using MediQueue.Application.Abstractions;
using MediQueue.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>Reads the audit trail.</summary>
public sealed class AuditRepository(MediQueueDbContext database) : IAuditRepository
{
    /// <inheritdoc />
    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = Filtered(query);

        // Counted before paging, so the caller can render "page 2 of 7" and can
        // tell an empty page from an empty log.
        var total = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        var entries = await filtered
            .OrderByDescending(entry => entry.OccurredAt)

            // Ties are possible: one business action writes several entries and
            // they share an instant to the tick. Without a second key the page
            // boundary is undefined and an entry can appear on two pages or on
            // none. Version-7 ids order the same way as the timestamp they were
            // built from, so this agrees with the primary sort rather than
            // fighting it.
            .ThenByDescending(entry => entry.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)

            // One query, not one per entry. Split off deliberately: with a
            // collection include, Skip and Take would page the joined rows
            // rather than the entries, so a single entry with many changes
            // would swallow the page.
            .Include(entry => entry.Changes)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AuditPage(entries, total);
    }

    /// <summary>Applies the filters the specification asks for.</summary>
    /// <remarks>
    /// A deleted visit's history stays readable here, and the test that proves
    /// it is <c>A_soft_delete_is_recorded_as_a_deletion_and_its_history_survives</c>
    /// — it withdraws a visit through the API, confirms the visit itself now
    /// answers 404, and reads its whole life back out of this query. Nothing in
    /// this method has to opt out of anything to make that true: no audit table
    /// carries a query filter, and neither has a navigation to a visit (D-57).
    /// </remarks>
    private IQueryable<AuditEntry> Filtered(AuditQuery query)
    {
        var entries = database.AuditEntries.AsQueryable();

        if (query.PatientId is { } patientId)
        {
            entries = entries.Where(entry => entry.PatientId == patientId);
        }

        if (query.UserId is { } userId)
        {
            entries = entries.Where(entry => entry.UserId == userId);
        }

        if (query.From is { } from)
        {
            entries = entries.Where(entry => entry.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(entry => entry.OccurredAt <= to);
        }

        return entries;
    }
}
