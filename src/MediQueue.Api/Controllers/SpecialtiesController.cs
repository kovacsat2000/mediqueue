using MediQueue.Application.Abstractions;
using MediQueue.Contracts.Directory;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

/// <summary>The fields of medicine a patient can be routed to.</summary>
[ApiController]
[Route("api/specialties")]
public sealed class SpecialtiesController(ISpecialtyDirectory specialties) : ControllerBase
{
    /// <summary>Lists every specialty, ordered by name.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>All specialties.</returns>
    /// <response code="200">The specialties.</response>
    /// <response code="401">No valid token was presented.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SpecialtyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<SpecialtyDto>>> GetAsync(CancellationToken cancellationToken) =>
        Ok(await specialties.ListAsync(cancellationToken).ConfigureAwait(false));
}
