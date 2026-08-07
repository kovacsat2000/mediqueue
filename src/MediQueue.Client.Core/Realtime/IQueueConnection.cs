using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.Realtime;

/// <summary>How the push channel is doing, in terms a person can be shown.</summary>
public enum RealtimeStatus
{
    /// <summary>Not started, or deliberately stopped.</summary>
    Disconnected = 0,

    /// <summary>Opening, or reopening after a drop.</summary>
    Connecting = 1,

    /// <summary>Connected and receiving.</summary>
    Live = 2,

    /// <summary>The connection dropped and is being retried.</summary>
    Reconnecting = 3,
}

/// <summary>
/// The client half of the push channel.
/// </summary>
/// <remarks>
/// An interface so that a view model's reaction to a message can be tested by
/// raising one, without a hub, a server or a socket. The implementation is
/// tested where a real hub exists — in the integration suite — and this seam is
/// what keeps the two kinds of test from being the same test.
/// </remarks>
public interface IQueueConnection : IAsyncDisposable
{
    /// <summary>How the connection is doing.</summary>
    RealtimeStatus Status { get; }

    /// <summary>Raised whenever <see cref="Status"/> changes.</summary>
    event EventHandler<RealtimeStatus>? StatusChanged;

    /// <summary>A patient arrived but has not been routed. Assistants only.</summary>
    event EventHandler<VisitSummaryDto>? VisitRegistered;

    /// <summary>A visit entered a queue.</summary>
    event EventHandler<VisitSummaryDto>? VisitQueued;

    /// <summary>A patient was called in.</summary>
    event EventHandler<VisitSummaryDto>? VisitCalledIn;

    /// <summary>A patient was released and the visit finished.</summary>
    event EventHandler<VisitSummaryDto>? VisitReleased;

    /// <summary>A visit was withdrawn.</summary>
    event EventHandler<VisitDeletedPayload>? VisitDeleted;

    /// <summary>Opens the connection. Safe to call when already open.</summary>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection.</summary>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    Task StopAsync(CancellationToken cancellationToken);
}
