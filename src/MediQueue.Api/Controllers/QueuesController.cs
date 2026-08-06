using MediQueue.Application.Abstractions;
using MediQueue.Application.Exceptions;
using MediQueue.Application.Visits;
using MediQueue.Contracts.Visits;
using MediQueue.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

/// <summary>The waiting lists.</summary>
[ApiController]
[Route("api/queues")]
public sealed class QueuesController(QueueQueryService queues, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Every active doctor's queue.</summary>
    /// <remarks>
    /// Includes doctors with nothing waiting: an empty queue is how an assistant
    /// sees that somebody is free.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One entry per active doctor.</returns>
    /// <response code="200">The queues.</response>
    [Authorize(Policy = AuthorizationPolicies.AssistantOnly)]
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<QueueDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<QueueDto>>> GetAllAsync(CancellationToken cancellationToken) =>
        Ok(await queues.GetAllQueuesAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>The calling doctor's own queue, in arrival order.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Their waiting and in-treatment visits.</returns>
    /// <response code="200">The queue.</response>
    [Authorize(Policy = AuthorizationPolicies.DoctorOnly)]
    [HttpGet("mine")]
    [ProducesResponseType<IReadOnlyList<VisitSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<VisitSummaryDto>>> GetMineAsync(CancellationToken cancellationToken)
    {
        var doctorId = currentUser.UserId
            ?? throw new ForbiddenException("The request carries no user identity.");

        return Ok(await queues.GetQueueForDoctorAsync(doctorId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>One doctor's queue.</summary>
    /// <remarks>An assistant may read any doctor's queue; a doctor may read only their own.</remarks>
    /// <param name="doctorId">Whose queue to read.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>That doctor's waiting and in-treatment visits.</returns>
    /// <response code="200">The queue.</response>
    /// <response code="403">A doctor asked for somebody else's queue.</response>
    [HttpGet("{doctorId:guid}")]
    [ProducesResponseType<IReadOnlyList<VisitSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<VisitSummaryDto>>> GetForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken) =>
        Ok(await queues.GetQueueForDoctorAsync(doctorId, cancellationToken).ConfigureAwait(false));
}
