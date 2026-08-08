namespace MediQueue.Client.Core.Realtime;

/// <summary>
/// Moves work onto the thread a user interface is allowed to be touched from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists.</strong> SignalR delivers every message on a
/// thread-pool thread. The view models react by mutating
/// <c>ObservableCollection</c>s that the windows are bound to, and every UI
/// framework — Avalonia included — requires that to happen on its own thread.
/// Measured rather than assumed: a push handler was observed running on thread
/// 11 while the connection had been created on thread 4.
/// </para>
/// <para>
/// It is an interface here and implemented in the shells, because
/// <c>Client.Core</c> has no Avalonia reference and that rule does not bend for
/// this. The shells supply one line — <c>Dispatcher.UIThread.Post</c> — and the
/// view models stay testable without a window.
/// </para>
/// </remarks>
public interface IUiDispatcher
{
    /// <summary>Queues work for the user-interface thread and returns immediately.</summary>
    /// <remarks>
    /// Queued rather than executed inline, so a message arriving while the UI
    /// thread is busy waits its turn instead of blocking the transport. The
    /// queue is ordered, so two pushes are applied in the order they arrived.
    /// </remarks>
    /// <param name="action">What to run.</param>
    void Post(Action action);
}

/// <summary>
/// Runs the work on whichever thread asked for it.
/// </summary>
/// <remarks>
/// For tests and for any host with no user-interface thread to speak of. It is
/// deliberately <em>not</em> the default anywhere a window exists: a default
/// that silently does nothing is how the absence of marshalling went unnoticed
/// in the first place.
/// </remarks>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        action();
    }
}
