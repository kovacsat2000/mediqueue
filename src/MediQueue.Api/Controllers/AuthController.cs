using MediQueue.Application.Authentication;
using MediQueue.Contracts.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

/// <summary>Signing in.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthenticationService authentication) : ControllerBase
{
    /// <summary>Exchanges a username and password for an access token.</summary>
    /// <param name="request">The credentials.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The token, its expiry, and the signed-in user.</returns>
    /// <response code="200">Signed in.</response>
    /// <response code="401">The credentials were refused. The reason is deliberately not given.</response>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken) =>
        await authentication.LoginAsync(request, cancellationToken).ConfigureAwait(false);
}
