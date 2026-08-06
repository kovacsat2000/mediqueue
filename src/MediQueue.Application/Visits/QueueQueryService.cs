using MediQueue.Application.Abstractions;
using MediQueue.Application.Exceptions;
using MediQueue.Contracts.Visits;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Visits;

namespace MediQueue.Application.Visits;

/// <summary>
/// Reading the waiting lists.
/// </summary>
/// <remarks>
/// Every projection here is the summary type. A queue is exactly the sort of
/// place a diagnosis would leak from, and the type that cannot carry one is
/// what stops it.
/// </remarks>
public sealed class QueueQueryService(
    IVisitRepository visits,
    IPatientRepository patients,
    IDoctorDirectory doctors,
    ISpecialtyDirectory specialties,
    ICurrentUser currentUser)
{
    /// <summary>Every active doctor's queue, for an assistant.</summary>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>One entry per active doctor, including those with nothing waiting.</returns>
    public async Task<IReadOnlyList<QueueDto>> GetAllQueuesAsync(CancellationToken cancellationToken)
    {
        var activeDoctors = await doctors.GetActiveAsync(null, cancellationToken).ConfigureAwait(false);
        var specialtyNames = await SpecialtyNamesAsync(cancellationToken).ConfigureAwait(false);
        var openVisits = await visits.GetAllOpenVisitsAsync(cancellationToken).ConfigureAwait(false);
        var patientsById = await PatientsForAsync(openVisits, cancellationToken).ConfigureAwait(false);
        var doctorNames = activeDoctors.ToNameLookup();

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
                Project(
                    byDoctor.TryGetValue(doctor.Id, out var queued) ? InQueueOrder(queued) : [],
                    patientsById,
                    specialtyNames,
                    doctorNames))),
        ];
    }

    /// <summary>
    /// Visits that have arrived but have not been routed to anybody.
    /// </summary>
    /// <remarks>
    /// The one listing here that is not a queue: these visits are in nobody's.
    /// It lives beside the queues because it needs exactly the same projection
    /// machinery, and because from an assistant's seat it is the same screen —
    /// the work that still has to be given to someone.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>Registered visits, oldest arrival first.</returns>
    public async Task<IReadOnlyList<VisitSummaryDto>> GetUnassignedAsync(CancellationToken cancellationToken)
    {
        var unassigned = await visits.GetUnassignedAsync(cancellationToken).ConfigureAwait(false);
        var patientsById = await PatientsForAsync(unassigned, cancellationToken).ConfigureAwait(false);
        var specialtyNames = await SpecialtyNamesAsync(cancellationToken).ConfigureAwait(false);
        var doctorNames = (await doctors.GetActiveAsync(null, cancellationToken).ConfigureAwait(false))
            .ToNameLookup();

        return Project(unassigned, patientsById, specialtyNames, doctorNames);
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
        var patientsById = await PatientsForAsync(queue, cancellationToken).ConfigureAwait(false);
        var specialtyNames = await SpecialtyNamesAsync(cancellationToken).ConfigureAwait(false);
        var doctorNames = (await doctors.GetActiveAsync(null, cancellationToken).ConfigureAwait(false))
            .ToNameLookup();

        return Project(InQueueOrder(queue), patientsById, specialtyNames, doctorNames);
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

    private static IReadOnlyList<VisitSummaryDto> Project(
        IReadOnlyList<Visit> queue,
        IReadOnlyDictionary<Guid, Patient> patientsById,
        IReadOnlyDictionary<Guid, string> specialtyNames,
        IReadOnlyDictionary<Guid, string> doctorNames) =>
        [
            .. queue.Select(visit => visit.ToSummary(
                patientsById[visit.PatientId],
                specialtyNames.NameOf(visit.SpecialtyId),
                doctorNames.NameOf(visit.DoctorId))),
        ];

    private async Task<IReadOnlyDictionary<Guid, string>> SpecialtyNamesAsync(CancellationToken cancellationToken) =>
        (await specialties.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(specialty => specialty.Id, specialty => specialty.Name);

    /// <summary>
    /// Loads every patient the projection needs, in one query.
    /// </summary>
    /// <remarks>
    /// A visit holds a patient id and no navigation property, so there is
    /// nothing to Include. One batched query is what that costs; the loop this
    /// replaced issued one per open visit, on the assistant's main screen.
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, Patient>> PatientsForAsync(
        IEnumerable<Visit> queue,
        CancellationToken cancellationToken)
    {
        var wanted = queue.Select(visit => visit.PatientId).Distinct().ToList();

        var found = await patients.GetByIdsAsync(wanted, cancellationToken).ConfigureAwait(false);

        // A visit whose patient is missing is a broken foreign key, not an empty
        // name to render. Failing here is loud; rendering a blank row is not.
        var missing = wanted.Where(id => !found.ContainsKey(id)).ToList();

        if (missing.Count > 0)
        {
            throw new NotFoundException(
                $"{missing.Count} visit(s) reference a patient that does not exist: {string.Join(", ", missing)}.");
        }

        return found;
    }
}
