using MediQueue.Domain.Users;

namespace MediQueue.Application.Abstractions;

/// <summary>Turns a user into an access token.</summary>
/// <remarks>
/// This interface is the reason the application layer contains no JWT type at
/// all. The use case knows that signing in produces a token which expires; it
/// does not know that the token is a JWT, that it is signed with HS256, or that
/// a library called Microsoft.IdentityModel exists. Swapping the scheme is an
/// implementation change in one class.
/// </remarks>
public interface ITokenIssuer
{
    /// <summary>Issues a token for a user who has already been authenticated.</summary>
    /// <param name="user">The authenticated user.</param>
    /// <returns>The token and the moment it stops being accepted.</returns>
    (string Token, DateTimeOffset ExpiresAt) Issue(User user);
}
