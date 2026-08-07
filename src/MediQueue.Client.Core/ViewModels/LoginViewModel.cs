using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediQueue.Client.Core.Api;
using MediQueue.Contracts;

namespace MediQueue.Client.Core.ViewModels;

/// <summary>Signing in.</summary>
/// <remarks>
/// Written once and used by both shells. The role each one accepts is a
/// constructor argument rather than a copy of this class: D-14 chose two
/// applications for the demo, not two sign-in screens, and the difference
/// between them is one enum.
/// </remarks>
/// <param name="api">Whichever role-scoped API this shell was given.</param>
/// <param name="session">Where the token and the user are kept.</param>
/// <param name="acceptedRole">The only role this application will admit.</param>
public sealed partial class LoginViewModel(ILoginApi api, IAuthSession session, UserRole acceptedRole)
    : ObservableObject
{
    /// <summary>Raised once somebody of the accepted role has signed in.</summary>
    public event EventHandler? SignedIn;

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Attempts to sign in.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await api.LoginAsync(Username, Password, cancellationToken).ConfigureAwait(true);

            // Each shell accepts only its own role, and says so plainly rather
            // than showing an empty screen that would look like a bug. The
            // session is signed back out, so a refused user leaves no token
            // behind for anything else in the process to pick up.
            if (session.CurrentUser?.Role != acceptedRole)
            {
                session.SignOut();

                ErrorMessage = acceptedRole == UserRole.Doctor
                    ? "This application is for doctors. Use the assistant application instead."
                    : "This application is for assistants. Use the doctor application instead.";

                return;
            }

            Password = string.Empty;
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException exception)
        {
            // The server's message, not ours. It says "Invalid username or
            // password" and deliberately not which of the two.
            ErrorMessage = exception.Detail;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "The server is not reachable. Check that it is running.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
