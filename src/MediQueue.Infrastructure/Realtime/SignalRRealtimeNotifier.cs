using MediQueue.Application.Abstractions;
using MediQueue.Contracts.Visits;
using Microsoft.AspNetCore.SignalR;

namespace MediQueue.Infrastructure.Realtime;

/// <summary>
/// Publishes queue events over SignalR, to the groups <c>plan.md</c> §6 names.
/// </summary>
/// <remarks>
/// <para>
/// The routing table is the authorization. An event about one doctor's queue is
/// addressed to that doctor's group and to the assistants; no other doctor is a
/// recipient, so there is nothing for a filter to get wrong later.
/// </para>
/// <para>
/// Every method may throw — a hub context over a dropped connection or a
/// disposed host does. That is deliberate and not an oversight: the guarantee
/// that a failed push cannot fail a committed write belongs to
/// <c>VisitAnnouncer</c>, where it holds for every implementation of this
/// interface rather than only for well-behaved ones.
/// </para>
/// </remarks>
public sealed class SignalRRealtimeNotifier(IHubContext<QueueHub> hub) : IRealtimeNotifier
{
    /// <summary>The client-side method names. Wire contract: renaming one is a breaking change.</summary>
    internal const string VisitRegistered = nameof(VisitRegistered);
    internal const string VisitQueued = nameof(VisitQueued);
    internal const string VisitCalledIn = nameof(VisitCalledIn);
    internal const string VisitReleased = nameof(VisitReleased);
    internal const string VisitDeleted = nameof(VisitDeleted);

    /// <inheritdoc />
    /// <remarks>
    /// Assistants only. An unrouted visit is in nobody's queue, so there is no
    /// doctor it could concern.
    /// </remarks>
    public Task VisitRegisteredAsync(VisitSummaryDto visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        return hub.Clients
            .Group(QueueHub.AssistantGroup)
            .SendAsync(VisitRegistered, visit, cancellationToken);
    }

    /// <inheritdoc />
    public Task VisitQueuedAsync(VisitSummaryDto visit, CancellationToken cancellationToken) =>
        SendAboutAsync(VisitQueued, visit, cancellationToken);

    /// <inheritdoc />
    public Task VisitCalledInAsync(VisitSummaryDto visit, CancellationToken cancellationToken) =>
        SendAboutAsync(VisitCalledIn, visit, cancellationToken);

    /// <inheritdoc />
    public Task VisitReleasedAsync(VisitSummaryDto visit, CancellationToken cancellationToken) =>
        SendAboutAsync(VisitReleased, visit, cancellationToken);

    /// <inheritdoc />
    public Task VisitDeletedAsync(Guid visitId, Guid? doctorId, CancellationToken cancellationToken) =>
        Audience(doctorId).SendAsync(
            VisitDeleted,
            new VisitDeletedPayload(visitId, doctorId),
            cancellationToken);

    /// <summary>Sends a visit event to the assistants and to the doctor whose queue it concerns.</summary>
    private Task SendAboutAsync(string eventName, VisitSummaryDto visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        return Audience(visit.DoctorId).SendAsync(eventName, visit, cancellationToken);
    }

    /// <summary>
    /// Who an event about one doctor's queue reaches.
    /// </summary>
    /// <remarks>
    /// Written once, so there is a single place to read to answer "can a doctor
    /// see another doctor's queue?". The answer is the group list on this line.
    /// </remarks>
    private IClientProxy Audience(Guid? doctorId) =>
        doctorId is { } id
            ? hub.Clients.Groups(QueueHub.AssistantGroup, QueueHub.DoctorGroup(id))
            : hub.Clients.Group(QueueHub.AssistantGroup);
}
