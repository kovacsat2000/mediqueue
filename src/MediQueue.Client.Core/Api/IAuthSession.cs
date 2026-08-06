using System.Net.Http.Headers;
using MediQueue.Contracts.Authentication;

namespace MediQueue.Client.Core.Api;

/// <summary>Who is signed in on this client, for as long as it is running.</summary>
/// <remarks>
/// <para>
/// <strong>In memory only. Nothing is written to disk.</strong> A token on disk
/// in a proof of concept is a token in a backup, in a screen recording, or in a
/// repository — and this system has no secure store to put one in. Closing the
/// application signs you out, which is the honest behaviour when the
/// alternative is a file nobody is protecting.
/// </para>
/// <para>
/// The token is never exposed as a string. Callers hand this a request and it
/// authorises it, so there is no property for a log statement or an error
/// message to reach.
/// </para>
/// </remarks>
public interface IAuthSession
{
    /// <summary>The signed-in user, or <c>null</c>.</summary>
    UserDto? CurrentUser { get; }

    /// <summary>Whether anybody is signed in.</summary>
    bool IsSignedIn { get; }

    /// <summary>Records a successful sign-in.</summary>
    /// <param name="response">What the server returned.</param>
    void SignIn(LoginResponse response);

    /// <summary>Forgets the token and the user.</summary>
    void SignOut();

    /// <summary>Attaches the bearer token to a request, if there is one.</summary>
    /// <param name="request">The outgoing request.</param>
    void Authorize(HttpRequestMessage request);
}

/// <summary>The in-memory session.</summary>
public sealed class AuthSession : IAuthSession
{
    private string? _accessToken;

    /// <inheritdoc />
    public UserDto? CurrentUser { get; private set; }

    /// <inheritdoc />
    public bool IsSignedIn => _accessToken is not null;

    /// <inheritdoc />
    public void SignIn(LoginResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        _accessToken = response.AccessToken;
        CurrentUser = response.User;
    }

    /// <inheritdoc />
    public void SignOut()
    {
        _accessToken = null;
        CurrentUser = null;
    }

    /// <inheritdoc />
    public void Authorize(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }
}
