using MediQueue.Application.Abstractions;
using MediQueue.Application.Exceptions;
using MediQueue.Contracts.Visits;
using MediQueue.Domain.Visits;

namespace MediQueue.Application.Visits;

/// <summary>
/// Reading the waiting lists.
/// </summary>
/// <remarks>
/// <para>
/// Every projection here is the summary type. A queue is exactly the sort of
/// place a diagnosis would leak from, and the type that cannot carry one is
/// what stops it.
/// </para>
/// <para>
/// This class reads queues. Visits that are in no queue at all are read by
/// <see cref="VisitQueryService"/> — the split is by what is read, not by which
/// screen happens to show it.
/// </para>
/// </remarks>
public sealed class QueueQueryService(
    IVisitRepository visits,
    IDoctorDirectory doctors,
    VisitContextLoader context,
    ICurrentUser currentUser)
{
    /// <summary>Every active doctor's queue, for an assistant.</summary>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>One entry per active doctor, including those with nothing waiting.</returns>
    public async Task<IReadOnlyList<QueueDto>> GetAllQueuesAsync(CancellationToken cancellationToken)
    {
        var activeDoctors = await doctors.GetActiveAsync(null, cancellationToken).ConfigureAwait(false);
        var openVisits = await visits.GetAllOpenVisitsAsync(cancellationToken).ConfigureAwait(false);

        var patientsById = await context.LoadPatientsAsync(openVisits, cancellationToken).ConfigureAwait(false);
        var (specialtyNames, doctorNames) = await context.LoadLookupsAsync(cancellationToken).ConfigureAwait(false);

        var byDoctor = openVisits
            .Where(visit => visit.DoctorId is not null)
            .GroupBy(visit => visit.DoctorId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        return
        [
            .. activeDoctors.Select(doctor => new QueueDto(
                doctor.Id,
                doctor.FullName,
                doctor.SpecialtyId!.Value,
                specialtyNames.NameOf(doctor.SpecialtyId) ?? string.Empty,
                // An empty queue is information: it is how an assistant sees
                // that a doctor is free. Doctors with nothing waiting stay in
                // the list rather than vanishing from it.
                (byDoctor.TryGetValue(doctor.Id, out var queued) ? InQueueOrder(queued) : [])
                    .ToSummaries(patientsById, specialtyNames, doctorNames))),
        ];
    }

    /// <summary>One doctor's queue.</summary>
    /// <param name="doctorId">Whose queue to read.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>Their waiting and in-treatment visits, in arrival order.</returns>
    /// <exception cref="ForbiddenException">A doctor asked for somebody else's queue.</exception>
    public async Task<IReadOnlyList<VisitSummaryDto>> GetQueueForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken)
    {
        // An assistant may read any queue; a doctor may read only their own.
        if (currentUser.Role == Contracts.UserRole.Doctor && currentUser.UserId != doctorId)
        {
            throw new ForbiddenException("This is not your queue.");
        }

        var queue = await visits.GetQueueAsync(doctorId, cancellationToken).ConfigureAwait(false);

        var patientsById = await context.LoadPatientsAsync(queue, cancellationToken).ConfigureAwait(false);
        var (specialtyNames, doctorNames) = await context.LoadLookupsAsync(cancellationToken).ConfigureAwait(false);

        return InQueueOrder(queue).ToSummaries(patientsById, specialtyNames, doctorNames);
    }

    /// <summary>
    /// Arrival order, which is the order the list is also displayed by.
    /// </summary>
    /// <remarks>
    /// Ordering and display must be the same field. A patient can be registered
    /// at 09:00 and only routed at 09:20, so ordering by one timestamp while
    /// showing another produces a list that looks incorrectly sorted — which
    /// reads as a bug during a demonstration.
    /// </remarks>
    private static IReadOnlyList<Visit> InQueueOrder(IEnumerable<Visit> queue) =>
        [.. queue.OrderBy(visit => visit.QueuedAt)];
}
