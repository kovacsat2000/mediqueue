using MediQueue.Application.Abstractions;
using MediQueue.Domain.Scheduling;
using MediQueue.Domain.Users;
using MediQueue.Domain.Visits;
using MediQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Directory;

/// <summary>Doctors, for routing and for listing.</summary>
public sealed class DoctorDirectory(MediQueueDbContext database) : IDoctorDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DoctorWorkload>> GetWorkloadsAsync(
        Guid specialtyId,
        CancellationToken cancellationToken)
    {
        // Active only. A doctor who has left the practice must not be handed a
        // patient, and an empty result here is what produces the 409 rather
        // than a visit queued where nobody is looking.
        var candidates = await database.Users
            .Where(user => user.Role == UserRole.Doctor && user.IsActive && user.SpecialtyId == specialtyId)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return [];
        }

        var openVisits = await database.Visits
            .Where(visit => visit.DoctorId != null
                && candidates.Contains(visit.DoctorId!.Value)
                && visit.Status != VisitStatus.Done)
            .Select(visit => new { DoctorId = visit.DoctorId!.Value, visit.Status, visit.QueuedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. candidates.Select(doctorId =>
            {
                var theirs = openVisits.Where(visit => visit.DoctorId == doctorId).ToList();

                return new DoctorWorkload(
                    doctorId,
                    theirs.Count(visit => visit.Status == VisitStatus.Waiting),
                    theirs.Count(visit => visit.Status == VisitStatus.InTreatment),
                    theirs.Count == 0 ? null : theirs.Max(visit => visit.QueuedAt));
            }),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetActiveAsync(Guid? specialtyId, CancellationToken cancellationToken)
    {
        var doctors = database.Users.Where(user => user.Role == UserRole.Doctor && user.IsActive);

        if (specialtyId is { } wanted)
        {
            doctors = doctors.Where(user => user.SpecialtyId == wanted);
        }

        return await doctors
            .OrderBy(user => user.FullName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
