using MediQueue.Client.Core.Api;
using MediQueue.Contracts.Visits;
using Microsoft.AspNetCore.SignalR.Client;

namespace MediQueue.Client.Core.Realtime;

/// <summary>
/// The push channel, over SignalR.
/// </summary>
/// <remarks>
/// <para>
/// No UI framework is involved and none is needed: this is a client library
/// raising events. Which thread a handler runs on is the shell's problem, and
/// the shell is the only part of the system that knows a window exists.
/// </para>
/// <para>
/// <see cref="HubConnectionBuilder.WithAutomaticReconnect()"/> handles the
/// ordinary case of a dropped socket. It is not a resynchronisation strategy —
/// messages sent while the client was away are simply gone — which is why
/// <see cref="StatusChanged"/> is surfaced and why the client refreshes once
/// after a reconnect rather than assuming it missed nothing.
/// </para>
/// </remarks>
public sealed class QueueConnection : IQueueConnection
{
    private readonly HubConnection _connection;
    private RealtimeStatus _status = RealtimeStatus.Disconnected;

    /// <summary>Builds the connection. Nothing is opened until <see cref="StartAsync"/>.</summary>
    /// <param name="hubUri">Where the hub lives.</param>
    /// <param name="session">Supplies the token, through its one named accessor.</param>
    public QueueConnection(Uri hubUri, IAuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
                // The one place the token leaves the session as a value, and it
                // says why in its name (D-55). SignalR puts it on the query
                // string for WebSockets, which is what the server's bearer
                // handler reads back — for this path and no other.
                options.AccessTokenProvider = session.GetTokenForRealtimeAsync)
            .WithAutomaticReconnect()
            .Build();

        _connection.Reconnecting += _ => Report(RealtimeStatus.Reconnecting);
        _connection.Reconnected += _ => Report(RealtimeStatus.Live);
        _connection.Closed += _ => Report(RealtimeStatus.Disconnected);

        Forward<VisitSummaryDto>(SignalRMethods.VisitRegistered, visit => VisitRegistered?.Invoke(this, visit));
        Forward<VisitSummaryDto>(SignalRMethods.VisitQueued, visit => VisitQueued?.Invoke(this, visit));
        Forward<VisitSummaryDto>(SignalRMethods.VisitCalledIn, visit => VisitCalledIn?.Invoke(this, visit));
        Forward<VisitSummaryDto>(SignalRMethods.VisitReleased, visit => VisitReleased?.Invoke(this, visit));
        Forward<VisitDeletedPayload>(SignalRMethods.VisitDeleted, payload => VisitDeleted?.Invoke(this, payload));
    }

    /// <inheritdoc />
    public RealtimeStatus Status => _status;

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
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_connection.State != HubConnectionState.Disconnected)
        {
            return;
        }

        await Report(RealtimeStatus.Connecting).ConfigureAwait(false);

        try
        {
            await _connection.StartAsync(cancellationToken).ConfigureAwait(false);
            await Report(RealtimeStatus.Live).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The queue still loads over HTTP without a push channel, so a hub
            // that will not open degrades the client rather than breaking it.
            // The status is what tells the doctor their list is no longer
            // updating by itself.
            await Report(RealtimeStatus.Disconnected).ConfigureAwait(false);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _connection.StopAsync(cancellationToken).ConfigureAwait(false);
        await Report(RealtimeStatus.Disconnected).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _connection.DisposeAsync().ConfigureAwait(false);

    private void Forward<T>(string method, Action<T> raise) => _connection.On(method, raise);

    private Task Report(RealtimeStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);

        return Task.CompletedTask;
    }
}

/// <summary>
/// The client-side method names the hub sends to.
/// </summary>
/// <remarks>
/// A wire contract: these strings are matched by name at run time, so a typo is
/// silence rather than a compile error — a client that quietly receives nothing
/// looks exactly like a quiet morning. Naming them once, here, is the closest
/// this gets to a compile-time check.
/// </remarks>
internal static class SignalRMethods
{
    internal const string VisitRegistered = nameof(VisitRegistered);
    internal const string VisitQueued = nameof(VisitQueued);
    internal const string VisitCalledIn = nameof(VisitCalledIn);
    internal const string VisitReleased = nameof(VisitReleased);
    internal const string VisitDeleted = nameof(VisitDeleted);
}
