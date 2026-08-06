using MediQueue.Application.Abstractions;
using MediQueue.Application.Exceptions;
using MediQueue.Contracts.Visits;

namespace MediQueue.Application.Visits;

/// <summary>
/// Reading one visit, projected to whatever the caller is allowed to see.
/// </summary>
public sealed class VisitQueryService(
    IVisitRepository visits,
    VisitContextLoader context,
    ICurrentUser currentUser)
{
    /// <summary>The result of a role-scoped read: exactly one of the two is set.</summary>
    /// <param name="Summary">Set for an assistant. Cannot carry a diagnosis.</param>
    /// <param name="Detail">Set for the doctor treating the visit.</param>
    public sealed record RoleScopedVisit(VisitSummaryDto? Summary, VisitDetailDto? Detail);

    /// <summary>Reads one visit, projected for the caller's role.</summary>
    /// <remarks>
    /// An assistant may read any visit, and receives the summary. A doctor
    /// receives the detail — including the diagnosis — but only for a visit in
    /// their own queue; anyone else's is refused rather than downgraded.
    /// Silently handing a doctor the assistant's view of a colleague's patient
    /// would be a quieter answer and a worse one.
    /// </remarks>
    /// <param name="visitId">The visit.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The projection the caller is entitled to.</returns>
    /// <exception cref="NotFoundException">There is no such visit, or it has been deleted.</exception>
    /// <exception cref="ForbiddenException">A doctor asked for a visit that is not theirs.</exception>
    public async Task<RoleScopedVisit> GetAsync(Guid visitId, CancellationToken cancellationToken)
    {
        var visit = await visits.GetByIdAsync(visitId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Visit '{visitId}' was not found.");

        var (patient, specialtyName, doctorName) =
            await context.LoadAsync(visit, cancellationToken).ConfigureAwait(false);

        if (currentUser.Role != Contracts.UserRole.Doctor)
        {
            return new RoleScopedVisit(visit.ToSummary(patient, specialtyName, doctorName), null);
        }

        if (currentUser.UserId != visit.DoctorId)
        {
            throw new ForbiddenException("This visit is not in your queue.");
        }

        return new RoleScopedVisit(null, visit.ToDetail(patient, specialtyName, doctorName));
    }
}
