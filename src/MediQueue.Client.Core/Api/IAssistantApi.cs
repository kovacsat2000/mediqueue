using MediQueue.Contracts.Directory;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.Api;

/// <summary>
/// Everything an assistant's application is allowed to ask the server for.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No member of this interface returns <c>VisitDetailDto</c>, and none
/// may ever be added.</strong> D-10 keeps a diagnosis off the wire by giving
/// the assistant a type that cannot carry one; this is the same guarantee
/// brought onto the client, one layer earlier. The assistant shell registers
/// only this interface, so its application does not merely avoid asking for a
/// diagnosis — it has no expressible way to ask. A reflection test asserts it,
/// the way P6 asserts the notifier's payload types.
/// </para>
/// <para>
/// The endpoints behind it are already assistant-only at the server
/// (<c>plan.md</c> §4), so this adds nothing to the authorization story. What
/// it adds is that a mistake in the client is a compile error rather than a
/// 403 discovered during the demo.
/// </para>
/// </remarks>
public interface IAssistantApi : ILoginApi
{
    /// <summary>The specialties a visit can be routed to.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Every specialty the practice has.</returns>
    Task<IReadOnlyList<SpecialtyDto>> GetSpecialtiesAsync(CancellationToken cancellationToken);

    /// <summary>Every active doctor's waiting list, including the empty ones.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One entry per active doctor.</returns>
    Task<IReadOnlyList<QueueDto>> GetAllQueuesAsync(CancellationToken cancellationToken);

    /// <summary>Visits that have arrived but are in nobody's queue.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Registered visits, oldest arrival first.</returns>
    Task<IReadOnlyList<VisitSummaryDto>> GetUnassignedAsync(CancellationToken cancellationToken);

    /// <summary>Registers a patient's arrival, optionally routing them straight to a queue.</summary>
    /// <param name="request">The patient's details and their complaint.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new visit.</returns>
    /// <exception cref="ApiException">A field failed validation, or the patient already has a visit open.</exception>
    Task<VisitSummaryDto> RegisterVisitAsync(RegisterVisitRequest request, CancellationToken cancellationToken);

    /// <summary>Routes a registered visit to a specialty. The server picks the doctor.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="specialtyId">The specialty to route to.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit, now waiting.</returns>
    /// <exception cref="ApiException">The specialty has no active doctor, or the visit cannot be routed.</exception>
    Task<VisitSummaryDto> AssignSpecialtyAsync(Guid visitId, Guid specialtyId, CancellationToken cancellationToken);

    /// <summary>Withdraws a visit. A logical delete.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ApiException">No such visit, or it was already withdrawn.</exception>
    Task DeleteVisitAsync(Guid visitId, CancellationToken cancellationToken);
}
