using MediQueue.Application.Abstractions;
using MediQueue.Application.Exceptions;
using MediQueue.Contracts.Visits;
using MediQueue.Domain.Scheduling;
using MediQueue.Domain.Visits;

namespace MediQueue.Application.Visits;

/// <summary>
/// Routes a registered visit into a doctor's queue.
/// </summary>
/// <remarks>
/// The customer put this on the system rather than the assistant: the assistant
/// chooses a specialty and the server chooses the doctor. Which doctor is the
/// strategy's decision, injected, so an alternative policy is a registration
/// change rather than an edit here.
/// </remarks>
public sealed class VisitAssignmentService(
    IVisitRepository visits,
    IDoctorDirectory doctors,
    ISpecialtyDirectory specialties,
    IDoctorAssignmentStrategy strategy,
    IUnitOfWork unitOfWork,
    VisitContextLoader context,
    VisitAnnouncer announcer,
    TimeProvider timeProvider)
{
    /// <summary>Routes a visit to a specialty and puts it in the chosen doctor's queue.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="specialtyId">The specialty to route to.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The visit as an assistant sees it.</returns>
    /// <exception cref="NotFoundException">There is no such visit.</exception>
    /// <exception cref="ConflictException">The specialty has no active doctor.</exception>
    public async Task<VisitSummaryDto> AssignAsync(
        Guid visitId,
        Guid specialtyId,
        CancellationToken cancellationToken)
    {
        var visit = await visits.GetByIdAsync(visitId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Visit '{visitId}' was not found.");

        await AssignAsync(visit, specialtyId, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var (patient, specialtyName, doctorName) =
            await context.LoadAsync(visit, cancellationToken).ConfigureAwait(false);

        var summary = visit.ToSummary(patient, specialtyName, doctorName);

        // After the commit, and only from this overload. The internal one below
        // does not commit — registration owns that transaction, and announcing
        // from inside it would publish a queue entry that may still roll back.
        await announcer.QueuedAsync(summary, cancellationToken).ConfigureAwait(false);

        return summary;
    }

    /// <summary>
    /// Routes a visit without committing, so registration can create the patient,
    /// the visit and the assignment inside one transaction.
    /// </summary>
    /// <param name="visit">The visit.</param>
    /// <param name="specialtyId">The specialty to route to.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ConflictException">The specialty has no active doctor.</exception>
    internal async Task AssignAsync(Visit visit, Guid specialtyId, CancellationToken cancellationToken)
    {
        var candidates = await doctors.GetWorkloadsAsync(specialtyId, cancellationToken).ConfigureAwait(false);

        var doctorId = strategy.SelectDoctor(specialtyId, candidates);

        if (doctorId is null)
        {
            // Deliberately not silent. Queuing the visit nowhere would leave a
            // patient in a waiting list no doctor is looking at, which is worse
            // than telling the assistant to choose another specialty.
            //
            // Named, not identified: the person reading this is an assistant at
            // a desk, and a GUID tells them nothing they can act on.
            var name = await SpecialtyNameAsync(specialtyId, cancellationToken).ConfigureAwait(false);

            throw new ConflictException(
                $"No doctor is currently available in {name}. The visit remains registered — "
                + "route it to another specialty, or leave it until a doctor is back.");
        }

        visit.AssignToQueue(specialtyId, doctorId.Value, timeProvider.GetUtcNow());
    }

    private async Task<string> SpecialtyNameAsync(Guid specialtyId, CancellationToken cancellationToken) =>
        (await specialties.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(specialty => specialty.Id == specialtyId)?.Name
        ?? $"specialty '{specialtyId}'";
}
