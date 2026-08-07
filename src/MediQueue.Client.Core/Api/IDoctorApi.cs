using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.Api;

/// <summary>
/// Everything a doctor's application asks the server for.
/// </summary>
/// <remarks>
/// <para>
/// The queue stays <see cref="VisitSummaryDto"/> — the list type still cannot
/// carry a diagnosis, so a screenful of waiting patients is not a screenful of
/// clinical records. Detail is fetched deliberately, for <strong>one</strong>
/// visit, and only the one being treated.
/// </para>
/// <para>
/// Every action here is refused by the server for a visit in another doctor's
/// queue (D-46). Nothing in this interface re-states that rule: the client
/// sends the request and renders the 403's message.
/// </para>
/// </remarks>
public interface IDoctorApi : ILoginApi
{
    /// <summary>The signed-in doctor's own queue, in arrival order.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Their waiting and in-treatment visits.</returns>
    Task<IReadOnlyList<VisitSummaryDto>> GetMyQueueAsync(CancellationToken cancellationToken);

    /// <summary>One visit in full, including the diagnosis.</summary>
    /// <param name="visitId">The visit. Must be in the calling doctor's queue.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit as its treating doctor may see it.</returns>
    /// <exception cref="ApiException">The visit is not this doctor's, or does not exist.</exception>
    Task<VisitDetailDto> GetVisitAsync(Guid visitId, CancellationToken cancellationToken);

    /// <summary>Calls the patient in from the waiting list.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit, now in treatment.</returns>
    /// <exception cref="ApiException">The visit is not this doctor's, or is not waiting.</exception>
    Task<VisitDetailDto> CallInAsync(Guid visitId, CancellationToken cancellationToken);

    /// <summary>Records what the doctor found.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="diagnosis">The finding.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit.</returns>
    /// <exception cref="ApiException">The visit is not this doctor's, or is not in treatment.</exception>
    Task<VisitDetailDto> RecordDiagnosisAsync(Guid visitId, string diagnosis, CancellationToken cancellationToken);

    /// <summary>Releases the patient and completes the visit.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit, now done.</returns>
    /// <exception cref="ApiException">The visit is not this doctor's, or is not in treatment.</exception>
    Task<VisitDetailDto> ReleaseAsync(Guid visitId, CancellationToken cancellationToken);
}
