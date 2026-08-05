using System.Security.Claims;
using MediQueue.Application.Abstractions;
using MediQueue.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MediQueue.Infrastructure.Authentication;

/// <summary>Reads the current identity off the request.</summary>
/// <remarks>
/// Everything is read from claims the token carried and the middleware
/// validated. Nothing here trusts a header or a query-string value, and nothing
/// re-queries the database — a token that has been validated already says who
/// its bearer is.
/// </remarks>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId => ReadGuid(JwtRegisteredClaimNames.Sub);

    /// <inheritdoc />
    public UserRole? Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(JwtTokenIssuer.RoleClaim), out var role)
            ? role
            : null;

    /// <inheritdoc />
    public Guid? SpecialtyId => ReadGuid(JwtTokenIssuer.SpecialtyIdClaim);

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(Principal?.FindFirstValue(claimType), out var value) ? value : null;
}
