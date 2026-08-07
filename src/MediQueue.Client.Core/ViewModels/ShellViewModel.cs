using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MediQueue.Client.Core.ViewModels;

/// <summary>Which screen the window is showing.</summary>
/// <remarks>
/// <para>
/// Switching screens is a view-model decision so that the window has nothing to
/// decide. The view binds to <see cref="Current"/> and renders whatever it is.
/// </para>
/// <para>
/// Shared by both applications. The only difference between them at this level
/// is which view model comes after sign-in, so that is a constructor argument
/// rather than a second copy of this class — D-14 chose two applications for
/// the demo, not two of everything.
/// </para>
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ObservableObject _main;
    private readonly IAsyncRelayCommand _start;

    /// <summary>Creates the shell, showing the sign-in screen.</summary>
    /// <param name="login">The sign-in screen.</param>
    /// <param name="main">The screen to show once somebody has signed in.</param>
    /// <param name="start">
    /// What to run when they do — opening the push channel and loading the
    /// lists. A command rather than a method: an async lambda on an event is
    /// fire-and-forget and its exceptions go nowhere, whereas the command owns
    /// the running task and the concurrency guard (D-54).
    /// </param>
    public ShellViewModel(LoginViewModel login, ObservableObject main, IAsyncRelayCommand start)
    {
        ArgumentNullException.ThrowIfNull(login);

        _main = main;
        _start = start;
        Current = login;

        login.SignedIn += (_, _) =>
        {
            Current = _main;
            _start.Execute(null);
        };
    }

    /// <summary>The screen on show: the sign-in view model, then the application's own.</summary>
    [ObservableProperty]
    public partial ObservableObject Current { get; set; }
}
