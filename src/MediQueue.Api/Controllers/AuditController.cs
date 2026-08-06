using MediQueue.Application.Abstractions;
using MediQueue.Application.Auditing;
using MediQueue.Contracts.Auditing;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

/// <summary>The audit trail.</summary>
/// <remarks>
/// Readable by any authenticated role — the specification requires the log to
/// be queryable, and an assistant is entitled to see that a record changed and
/// who changed it. What the role decides is whether the clinical values in it
/// are legible, and that decision lives in <c>AuditMapper</c>.
/// </remarks>
[ApiController]
[Route("api/audit")]
public sealed class AuditController(AuditQueryService audit) : ControllerBase
{
    /// <summary>Reads the audit trail, newest first.</summary>
    /// <remarks>
    /// A page size above the maximum is clamped rather than refused, and so is
    /// one below 1 — a caller cannot act differently on being told, which is the
    /// test D-50 sets. The clamped size travels back on the response so the
    /// client knows what it actually got.
    /// </remarks>
    /// <param name="patientId">Only entries concerning this patient.</param>
    /// <param name="userId">Only entries made by this user.</param>
    /// <param name="from">Only entries at or after this instant.</param>
    /// <param name="to">Only entries at or before this instant.</param>
    /// <param name="page">Which page, one-based. Defaults to 1.</param>
    /// <param name="pageSize">How many entries per page. Defaults to 50, capped at 200.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One page of the trail.</returns>
    /// <response code="200">The page.</response>
    /// <response code="401">The request carries no valid token.</response>
    [HttpGet]
    [ProducesResponseType<AuditPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuditPageDto>> QueryAsync(
        [FromQuery] Guid? patientId,
        [FromQuery] Guid? userId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var query = AuditQuery.Create(patientId, userId, from, to, page, pageSize);

        return Ok(await audit.QueryAsync(query, cancellationToken).ConfigureAwait(false));
    }
}
