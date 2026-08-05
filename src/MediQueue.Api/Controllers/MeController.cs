using MediQueue.Application.Abstractions;
using MediQueue.Application.Mapping;
using MediQueue.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

/// <summary>Who the caller is, according to the token they presented.</summary>
[ApiController]
[Route("api/me")]
public sealed class MeController(ICurrentUser currentUser, IUserDirectory users) : ControllerBase
{
    /// <summary>Returns the signed-in user.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The user the token belongs to.</returns>
    /// <response code="200">The signed-in user.</response>
    /// <response code="401">No valid token was presented.</response>
    [HttpGet]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> GetAsync(CancellationToken cancellationToken)
    {
        // The token is validated, so a missing id here is impossible rather than
        // merely unlikely; the fallback policy has already refused anonymity.
        if (currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        // Re-read rather than rebuilt from claims: a token lives eight hours, and
        // a name or a specialty may have changed inside one.
        var user = await users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return user is null ? Unauthorized() : user.ToDto();
    }
}
