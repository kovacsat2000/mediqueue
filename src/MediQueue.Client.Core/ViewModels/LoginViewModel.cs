using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediQueue.Client.Core.Api;
using MediQueue.Contracts;

namespace MediQueue.Client.Core.ViewModels;

/// <summary>Signing in.</summary>
public sealed partial class LoginViewModel(MediQueueApiClient api, IAuthSession session) : ObservableObject
{
    /// <summary>Raised once a doctor has signed in successfully.</summary>
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
            // than showing an empty queue that would look like a bug.
            if (session.CurrentUser?.Role != UserRole.Doctor)
            {
                session.SignOut();
                ErrorMessage = "This application is for doctors. Use the assistant application instead.";

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
