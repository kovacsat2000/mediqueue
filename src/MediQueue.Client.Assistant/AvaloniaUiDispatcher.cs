using Avalonia.Threading;
using MediQueue.Client.Core.Realtime;

namespace MediQueue.Client.Assistant;

/// <summary>Runs work on Avalonia's user-interface thread.</summary>
/// <remarks>
/// The whole of this shell's answer to "which thread may touch the window": one
/// line, in the only project that knows Avalonia exists. Everything above it —
/// the connection, the view models, their tests — is written against the
/// interface and needs no window to run.
/// </remarks>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
