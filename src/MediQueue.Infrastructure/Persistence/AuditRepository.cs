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
    /// <para>
    /// <c>IgnoreQueryFilters</c> is required rather than incidental. The global
    /// filter hides soft-deleted visits, and a deleted visit's history is the
    /// history most worth having — a record that vanishes from the audit trail
    /// the moment somebody withdraws it is not an audit trail.
    /// </para>
    /// <para>
    /// It reads oddly here because <c>AuditEntry</c> has no filter of its own.
    /// It is needed all the same: the filter applies to every entity type in
    /// the query, and the <c>Changes</c> include drags one along.
    /// </para>
    /// </remarks>
    private IQueryable<AuditEntry> Filtered(AuditQuery query)
    {
        var entries = database.AuditEntries.IgnoreQueryFilters();

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
