using MediQueue.Domain.Scheduling;
using MediQueue.Domain.Users;

namespace MediQueue.Application.Abstractions;

/// <summary>Reads doctors, for routing and for listing.</summary>
/// <remarks>
/// <strong>Both methods return active doctors only.</strong> A deactivated
/// doctor has left the practice: they must not appear in a list and must not be
/// handed a patient.
/// </remarks>
public interface IDoctorDirectory
{
    /// <summary>How busy each active doctor in a specialty currently is.</summary>
    /// <param name="specialtyId">The specialty.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One workload per active doctor. Empty if the specialty has none.</returns>
    Task<IReadOnlyList<DoctorWorkload>> GetWorkloadsAsync(Guid specialtyId, CancellationToken cancellationToken);

    /// <summary>The active doctors, optionally narrowed to one specialty.</summary>
    /// <param name="specialtyId">A specialty, or <c>null</c> for all of them.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Active doctors, ordered by name.</returns>
    Task<IReadOnlyList<User>> GetActiveAsync(Guid? specialtyId, CancellationToken cancellationToken);
}
