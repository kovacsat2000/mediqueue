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
    /// The requirement is real: a deleted visit's history is the history most
    /// worth having, and a record that vanishes from the audit trail the moment
    /// somebody withdraws it is not an audit trail. It is asserted by
    /// <c>A_soft_delete_is_recorded_as_a_deletion_and_its_history_survives</c>.
    /// </para>
    /// <para>
    /// <strong><c>IgnoreQueryFilters</c> is not what satisfies it, and the
    /// comment here used to claim otherwise.</strong> Measured by deleting the
    /// call: no test changes. The only query filter in this system is on
    /// <c>Visit</c>, and this query touches <c>AuditEntry</c> and
    /// <c>AuditFieldChange</c> — neither is filtered, and neither has a
    /// navigation to a visit, because an audit entry deliberately carries no
    /// foreign keys so that it outlives what it describes. Nothing filtered
    /// participates, so nothing is being ignored.
    /// </para>
    /// <para>
    /// The call is kept because <c>plan.md</c> §5 states the requirement in
    /// these terms and reversing that is the controller session's decision, not
    /// this one's. Recorded here so the next reader inherits the measurement
    /// rather than the assumption.
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
