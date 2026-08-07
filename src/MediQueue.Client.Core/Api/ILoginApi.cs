using MediQueue.Contracts.Authentication;

namespace MediQueue.Client.Core.Api;

/// <summary>The one call both roles make before they are a role at all.</summary>
/// <remarks>
/// Extracted so that the sign-in screen can live in <c>Client.Core</c> and be
/// written once. Both role interfaces extend it, so each shell still registers
/// exactly one API interface and gets sign-in with it.
/// </remarks>
public interface ILoginApi
{
    /// <summary>Signs in and remembers the token for every later call.</summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The token, its expiry, and the signed-in user.</returns>
    /// <exception cref="ApiException">The server refused.</exception>
    Task<LoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken);
}
