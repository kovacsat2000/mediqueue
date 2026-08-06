using MediQueue.Application.Abstractions;
using MediQueue.Domain.Visits;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>Visits, straight out of the database.</summary>
/// <remarks>
/// Every query here inherits the global soft-delete filter, so a deleted visit
/// is simply not found — which is what makes a second delete a 404 rather than
/// a special case anybody had to write.
/// </remarks>
public sealed class VisitRepository(MediQueueDbContext database) : IVisitRepository
{
    /// <inheritdoc />
    public Task<Visit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        database.Visits.SingleOrDefaultAsync(visit => visit.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasOpenVisitAsync(Guid patientId, CancellationToken cancellationToken) =>
        database.Visits.AnyAsync(
            visit => visit.PatientId == patientId && visit.Status != VisitStatus.Done,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Visit>> GetQueueAsync(Guid doctorId, CancellationToken cancellationToken) =>
        await database.Visits
            .Where(visit => visit.DoctorId == doctorId
                && (visit.Status == VisitStatus.Waiting || visit.Status == VisitStatus.InTreatment))
            // Ordered by the same field the client displays, so the order shown
            // can never contradict the times shown.
            .OrderBy(visit => visit.QueuedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Visit>> GetAllOpenVisitsAsync(CancellationToken cancellationToken) =>
        await database.Visits
            .Where(visit => visit.Status != VisitStatus.Done)
            .OrderBy(visit => visit.QueuedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Visit visit) => database.Visits.Add(visit);
}
