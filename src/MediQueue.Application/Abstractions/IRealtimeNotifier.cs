using MediQueue.Contracts.Visits;

namespace MediQueue.Application.Abstractions;

/// <summary>
/// Publishes state changes to the connected desktop clients.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every payload is <see cref="VisitSummaryDto"/> — the type that
/// declares no diagnosis member — and no overload here may ever take
/// <c>VisitDetailDto</c>.</strong> The push channel therefore inherits D-10's
/// guarantee from the type system: a diagnosis cannot reach a client over this
/// channel because there is no shape in which it could travel, not because
/// somebody remembered to strip it. A push goes to every connected assistant,
/// so this is exactly the channel where a runtime filter would eventually be
/// forgotten.
/// </para>
/// <para>
/// Implementations are free to throw. Callers do not reach this interface
/// directly — they go through <see cref="Visits.VisitAnnouncer"/>, which is
/// where the guarantee that a failed push never fails a committed write is
/// implemented, once.
/// </para>
/// </remarks>
public interface IRealtimeNotifier
{
    /// <summary>A patient has arrived but has not been routed to anybody.</summary>
    /// <param name="visit">The visit, as an assistant may see it.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    Task VisitRegisteredAsync(VisitSummaryDto visit, CancellationToken cancellationToken);

    /// <summary>A visit has entered a doctor's queue.</summary>
    /// <param name="visit">The visit, as an assistant may see it.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    Task VisitQueuedAsync(VisitSummaryDto visit, CancellationToken cancellationToken);

    /// <summary>A doctor has called the patient in.</summary>
    /// <param name="visit">The visit, as an assistant may see it.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    Task VisitCalledInAsync(VisitSummaryDto visit, CancellationToken cancellationToken);

    /// <summary>A patient has been released and the visit is finished.</summary>
    /// <param name="visit">The visit, as an assistant may see it.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    Task VisitReleasedAsync(VisitSummaryDto visit, CancellationToken cancellationToken);

    /// <summary>
    /// A visit has been withdrawn.
    /// </summary>
    /// <remarks>
    /// Identifiers only: the visit is gone, so there is nothing left to project
    /// and nothing a client needs beyond which row to remove and whose queue to
    /// remove it from.
    /// </remarks>
    /// <param name="visitId">Which visit.</param>
    /// <param name="doctorId">Whose queue it was in, or <c>null</c> if it was in nobody's.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    Task VisitDeletedAsync(Guid visitId, Guid? doctorId, CancellationToken cancellationToken);
}
