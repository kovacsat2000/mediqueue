using MediQueue.Application.Abstractions;
using MediQueue.Application.Mapping;
using MediQueue.Contracts.Authentication;
using MediQueue.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace MediQueue.Application.Authentication;

/// <summary>
/// Signing in: prove who you are, and receive a token that says so.
/// </summary>
/// <remarks>
/// There is no JWT type anywhere in this class. It knows that authentication
/// produces a token which expires, and nothing about how. That is what
/// <see cref="ITokenIssuer"/> is for, and it is why the signing algorithm could
/// change without this file being opened.
/// </remarks>
public sealed class AuthenticationService(
    IUserDirectory users,
    IPasswordHasher<User> passwordHasher,
    ITokenIssuer tokenIssuer)
{
    /// <summary>Authenticates a user and issues them a token.</summary>
    /// <param name="request">The presented credentials.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The token, its expiry, and who it belongs to.</returns>
    /// <exception cref="AuthenticationFailedException">
    /// The username is unknown, the password is wrong, or the account is
    /// inactive — the caller is not told which.
    /// </exception>
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.FindByUsernameAsync(request.Username, cancellationToken).ConfigureAwait(false);

        // Every failure below leaves by the same door, carrying the same message.
        if (user is null || !user.IsActive || !PasswordMatches(user, request.Password))
        {
            throw new AuthenticationFailedException();
        }

        var (token, expiresAt) = tokenIssuer.Issue(user);

        return new LoginResponse(token, expiresAt, user.ToDto());
    }

    private bool PasswordMatches(User user, string password)
    {
        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

        // SuccessRehashNeeded means the stored hash used older parameters than the
        // hasher would choose today. The password is correct, so sign-in succeeds;
        // silently upgrading the stored hash is a password-lifecycle concern this
        // system does not have yet.
        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
