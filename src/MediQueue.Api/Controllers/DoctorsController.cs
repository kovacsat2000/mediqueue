using MediQueue.Application.Abstractions;
using MediQueue.Contracts.Directory;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

/// <summary>The doctors a visit can be routed to.</summary>
[ApiController]
[Route("api/doctors")]
public sealed class DoctorsController(IUserDirectory users) : ControllerBase
{
    /// <summary>Lists active doctors, ordered by name, with their specialty.</summary>
    /// <param name="specialtyId">Restricts the list to one specialty.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Active doctors only.</returns>
    /// <response code="200">The doctors.</response>
    /// <response code="401">No valid token was presented.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DoctorDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<DoctorDto>>> GetAsync(
        [FromQuery] Guid? specialtyId,
        CancellationToken cancellationToken) =>
        Ok(await users.ListActiveDoctorsAsync(specialtyId, cancellationToken).ConfigureAwait(false));
}
