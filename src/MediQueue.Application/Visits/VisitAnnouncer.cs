using MediQueue.Application.Abstractions;
using MediQueue.Contracts.Visits;
using Microsoft.Extensions.Logging;

namespace MediQueue.Application.Visits;

/// <summary>
/// Announces a committed change to the connected clients, and guarantees that
/// failing to do so cannot undo it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every announcement is made after the commit, never before.</strong>
/// Publishing an event for a transaction that then fails is a lie the clients
/// have no way to detect — they would show a patient who is not in the database,
/// and only a manual refresh would ever correct it.
/// </para>
/// <para>
/// <strong>And a failed announcement never fails the action it describes.</strong>
/// The write succeeded; the caller is entitled to its 201. A push is a
/// convenience over a system that is still correct without one, so an exception
/// here is logged with the visit id and swallowed. The recovery is the client's
/// Refresh button, which is why that button stays in the client rather than
/// being replaced by the push channel. This is the same shape as D-48: a
/// failure *after* the commit leaves the client's view uncertain, and the
/// honest response is to record the gap rather than engineer around it.
/// </para>
/// <para>
/// <strong>Why this is a concrete class and not the interface itself.</strong>
/// The guarantee above has to hold for every implementation, including a
/// substituted one — so it cannot live in <see cref="IRealtimeNotifier"/>'s
/// implementation, or "does not throw" becomes an unenforceable line of
/// documentation that each new implementor must honour. Services depend on this
/// type directly, the same arrangement as <see cref="VisitContextLoader"/>, so
/// there is exactly one guarded path and no way to route around it.
/// </para>
/// </remarks>
public sealed class VisitAnnouncer(IRealtimeNotifier notifier, ILogger<VisitAnnouncer> logger)
{
    /// <summary>
    /// Announces a newly registered visit, naming what actually became of it.
    /// </summary>
    /// <remarks>
    /// One business action produces one event. A registration that carried a
    /// specialty arrives in a queue immediately, and announcing both
    /// <c>VisitRegistered</c> and <c>VisitQueued</c> for it would describe an
    /// unrouted state that never existed for an observable moment — an
    /// assistant's screen would flash the row into the unrouted list and out
    /// again. The event names the outcome; the payload's status says which list
    /// the row belongs in.
    /// </remarks>
    /// <param name="visit">The visit, as an assistant may see it.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    public Task RegisteredAsync(VisitSummaryDto visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        return visit.DoctorId is null
            ? PublishAsync("VisitRegistered", visit.Id, () => notifier.VisitRegisteredAsync(visit, cancellationToken))
            : QueuedAsync(visit, cancellationToken);
    }

    /// <summary>Announces that a visit has entered a doctor's queue.</summary>
    /// <param name="visit">The visit, as an assistant may see it.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    public Task QueuedAsync(VisitSummaryDto visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        return PublishAsync("VisitQueued", visit.Id, () => notifier.VisitQueuedAsync(visit, cancellationToken));
    }

    /// <summary>Announces that a doctor has called the patient in.</summary>
    /// <param name="visit">The visit, as an assistant may see it.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    public Task CalledInAsync(VisitSummaryDto visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        return PublishAsync("VisitCalledIn", visit.Id, () => notifier.VisitCalledInAsync(visit, cancellationToken));
    }

    /// <summary>Announces that a patient has been released.</summary>
    /// <param name="visit">The visit, as an assistant may see it.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    public Task ReleasedAsync(VisitSummaryDto visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        return PublishAsync("VisitReleased", visit.Id, () => notifier.VisitReleasedAsync(visit, cancellationToken));
    }

    /// <summary>Announces that a visit has been withdrawn.</summary>
    /// <param name="visitId">Which visit.</param>
    /// <param name="doctorId">Whose queue it was in, if any.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    public Task DeletedAsync(Guid visitId, Guid? doctorId, CancellationToken cancellationToken) =>
        PublishAsync("VisitDeleted", visitId, () => notifier.VisitDeletedAsync(visitId, doctorId, cancellationToken));

    /// <summary>Publishes one event, absorbing whatever the transport does to it.</summary>
    private async Task PublishAsync(string eventName, Guid visitId, Func<Task> publish)
    {
        try
        {
            await publish().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Deliberately every exception. The transport is a network and a
            // third-party client library; enumerating what it can throw would be
            // a guess, and a guess that is wrong once re-introduces exactly the
            // failure this method exists to prevent — a committed write reported
            // to the caller as an error.
            //
            // Never silent, though. A push channel that has quietly stopped
            // working looks identical to one with nothing to say, and this
            // warning is the only difference.
            logger.LogWarning(
                exception,
                "Could not publish {Event} for visit {VisitId}. The change is committed; "
                + "connected clients will not see it until they refresh.",
                eventName,
                visitId);
        }
    }
}
