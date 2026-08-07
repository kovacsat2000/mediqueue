using MediQueue.Client.Core.Realtime;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.Tests;

/// <summary>
/// A push channel a test can drive: no hub, no socket, no server.
/// </summary>
/// <remarks>
/// Hand-written rather than substituted because these tests raise events, and
/// raising an event is the one thing a mocking library makes harder to read
/// than a five-line class does. The real implementation is exercised where a
/// real hub exists, in the integration suite.
/// </remarks>
public sealed class FakeQueueConnection : IQueueConnection
{
    /// <summary>How many times a test asked for the connection to open.</summary>
    public int StartCount { get; private set; }

    /// <summary>Set to make <see cref="StartAsync"/> fail, as an unreachable hub would.</summary>
    public Exception? StartFailure { get; set; }

    /// <inheritdoc />
    public RealtimeStatus Status { get; private set; } = RealtimeStatus.Disconnected;

    /// <inheritdoc />
    public event EventHandler<RealtimeStatus>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler<VisitSummaryDto>? VisitRegistered;

    /// <inheritdoc />
    public event EventHandler<VisitSummaryDto>? VisitQueued;

    /// <inheritdoc />
    public event EventHandler<VisitSummaryDto>? VisitCalledIn;

    /// <inheritdoc />
    public event EventHandler<VisitSummaryDto>? VisitReleased;

    /// <inheritdoc />
    public event EventHandler<VisitDeletedPayload>? VisitDeleted;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartCount++;

        if (StartFailure is not null)
        {
            Report(RealtimeStatus.Disconnected);

            return Task.FromException(StartFailure);
        }

        Report(RealtimeStatus.Live);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Report(RealtimeStatus.Disconnected);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Moves the connection to a state and tells anybody listening.</summary>
    /// <param name="status">The new state.</param>
    public void Report(RealtimeStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    /// <summary>Delivers a message, as the server would.</summary>
    /// <param name="visit">The visit the event concerns.</param>
    public void PushRegistered(VisitSummaryDto visit) => VisitRegistered?.Invoke(this, visit);

    /// <inheritdoc cref="PushRegistered" />
    public void PushQueued(VisitSummaryDto visit) => VisitQueued?.Invoke(this, visit);

    /// <inheritdoc cref="PushRegistered" />
    public void PushCalledIn(VisitSummaryDto visit) => VisitCalledIn?.Invoke(this, visit);

    /// <inheritdoc cref="PushRegistered" />
    public void PushReleased(VisitSummaryDto visit) => VisitReleased?.Invoke(this, visit);

    /// <summary>Delivers a withdrawal, as the server would.</summary>
    /// <param name="visitId">The visit that was withdrawn.</param>
    /// <param name="doctorId">Whose queue it was in.</param>
    public void PushDeleted(Guid visitId, Guid? doctorId) =>
        VisitDeleted?.Invoke(this, new VisitDeletedPayload(visitId, doctorId));
}
