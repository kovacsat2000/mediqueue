using CommunityToolkit.Mvvm.ComponentModel;

namespace MediQueue.Client.Core.ViewModels;

/// <summary>Which screen the window is showing.</summary>
/// <remarks>
/// Switching screens is a view-model decision so that the window has nothing to
/// decide. The view binds to <see cref="Current"/> and renders whatever it is.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly QueueViewModel _queue;

    /// <summary>Creates the shell, showing the sign-in screen.</summary>
    /// <param name="login">The sign-in screen.</param>
    /// <param name="queue">The queue screen.</param>
    public ShellViewModel(LoginViewModel login, QueueViewModel queue)
    {
        ArgumentNullException.ThrowIfNull(login);

        _queue = queue;
        Current = login;

        login.SignedIn += (_, _) =>
        {
            Current = _queue;

            // Through the command rather than the method: an async lambda on an
            // event is fire-and-forget, and its exceptions would go nowhere.
            // The command owns the running task and the concurrency guard.
            _queue.RefreshCommand.Execute(null);
        };
    }

    /// <summary>The screen on show: the sign-in view model, then the queue.</summary>
    [ObservableProperty]
    public partial ObservableObject Current { get; set; }
}
