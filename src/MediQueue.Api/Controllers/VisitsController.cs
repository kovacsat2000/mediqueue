using MediQueue.Application.Visits;
using MediQueue.Contracts.Visits;
using MediQueue.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.Controllers;

/// <summary>The visit lifecycle.</summary>
[ApiController]
[Route("api/visits")]
public sealed class VisitsController(
    VisitRegistrationService registration,
    VisitAssignmentService assignment,
    VisitLifecycleService lifecycle,
    VisitQueryService queries,
    QueueQueryService queues) : ControllerBase
{
    /// <summary>
    /// Names the read action for the Location header of a newly created visit.
    /// </summary>
    /// <remarks>
    /// An explicit route name rather than <c>nameof</c>: MVC strips the "Async"
    /// suffix from action names by convention, so <c>nameof(GetAsync)</c> names a
    /// route that does not exist and the failure arrives at runtime.
    /// </remarks>
    private const string GetVisitRoute = "GetVisit";

    /// <summary>Registers a patient's arrival and opens a visit.</summary>
    /// <remarks>
    /// A returning patient is matched on their TAJ number and their record is
    /// reused unchanged. Supply a specialty to route the visit straight into a
    /// queue, or omit it to leave the visit registered and choose later.
    /// </remarks>
    /// <param name="request">The patient's details and their complaint.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new visit.</returns>
    /// <response code="201">Registered.</response>
    /// <response code="400">A field failed validation.</response>
    /// <response code="409">The patient already has a visit open, or the specialty has no active doctor.</response>
    [Authorize(Policy = AuthorizationPolicies.AssistantOnly)]
    [HttpPost]
    [ProducesResponseType<VisitSummaryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitSummaryDto>> RegisterAsync(
        RegisterVisitRequest request,
        CancellationToken cancellationToken)
    {
        var visit = await registration.RegisterAsync(request, cancellationToken).ConfigureAwait(false);

        return CreatedAtRoute(GetVisitRoute, new { id = visit.Id }, visit);
    }

    /// <summary>Routes a registered visit to a specialty. The server chooses the doctor.</summary>
    /// <param name="id">The visit.</param>
    /// <param name="request">The specialty to route to.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit, now waiting.</returns>
    /// <response code="200">Routed.</response>
    /// <response code="404">No such visit.</response>
    /// <response code="409">The specialty has no active doctor, or the visit cannot be routed from its current state.</response>
    [Authorize(Policy = AuthorizationPolicies.AssistantOnly)]
    [HttpPost("{id:guid}/assign")]
    [ProducesResponseType<VisitSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitSummaryDto>> AssignAsync(
        Guid id,
        AssignSpecialtyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await assignment.AssignAsync(id, request.SpecialtyId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Calls the patient in. The visit must be in the calling doctor's own queue.</summary>
    /// <param name="id">The visit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit.</returns>
    /// <response code="200">Called in.</response>
    /// <response code="403">The visit is in another doctor's queue.</response>
    /// <response code="404">No such visit.</response>
    /// <response code="409">The visit is not waiting.</response>
    [Authorize(Policy = AuthorizationPolicies.DoctorOnly)]
    [HttpPost("{id:guid}/call-in")]
    [ProducesResponseType<VisitDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitDetailDto>> CallInAsync(Guid id, CancellationToken cancellationToken) =>
        await lifecycle.CallInAsync(id, cancellationToken).ConfigureAwait(false);

    /// <summary>Records what the doctor found.</summary>
    /// <param name="id">The visit.</param>
    /// <param name="request">The finding.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit.</returns>
    /// <response code="200">Recorded.</response>
    /// <response code="403">The visit is in another doctor's queue.</response>
    /// <response code="404">No such visit.</response>
    /// <response code="409">The visit is not in treatment.</response>
    [Authorize(Policy = AuthorizationPolicies.DoctorOnly)]
    [HttpPut("{id:guid}/diagnosis")]
    [ProducesResponseType<VisitDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitDetailDto>> RecordDiagnosisAsync(
        Guid id,
        RecordDiagnosisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await lifecycle.RecordDiagnosisAsync(id, request.Diagnosis, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases the patient and completes the visit.</summary>
    /// <param name="id">The visit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit.</returns>
    /// <response code="200">Released.</response>
    /// <response code="403">The visit is in another doctor's queue.</response>
    /// <response code="404">No such visit.</response>
    /// <response code="409">The visit is not in treatment.</response>
    [Authorize(Policy = AuthorizationPolicies.DoctorOnly)]
    [HttpPost("{id:guid}/release")]
    [ProducesResponseType<VisitDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitDetailDto>> ReleaseAsync(Guid id, CancellationToken cancellationToken) =>
        await lifecycle.ReleaseAsync(id, cancellationToken).ConfigureAwait(false);

    /// <summary>Withdraws a visit.</summary>
    /// <remarks>
    /// A logical delete: medical records under an audit requirement are not
    /// physically removed. The visit becomes invisible to every query, so a
    /// second delete answers 404.
    /// </remarks>
    /// <param name="id">The visit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Nothing.</returns>
    /// <response code="204">Withdrawn.</response>
    /// <response code="404">No such visit, or it was already withdrawn.</response>
    [Authorize(Policy = AuthorizationPolicies.AssistantOnly)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await lifecycle.SoftDeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>Lists visits that have arrived but have not been routed to anybody.</summary>
    /// <remarks>
    /// Every other listing groups by doctor, and these visits have none. Without
    /// this endpoint a patient registered without a specialty is in no list at
    /// all, reachable only by an identifier the assistant never saw.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Registered visits, oldest arrival first.</returns>
    /// <response code="200">The unrouted visits.</response>
    [Authorize(Policy = AuthorizationPolicies.AssistantOnly)]
    [HttpGet("unassigned")]
    [ProducesResponseType<IReadOnlyList<VisitSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<VisitSummaryDto>>> GetUnassignedAsync(
        CancellationToken cancellationToken) =>
        Ok(await queues.GetUnassignedAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Reads one visit, projected for the caller's role.</summary>
    /// <remarks>
    /// The <c>{id:guid}</c> constraint is load-bearing: without it this route
    /// would swallow the literal <c>unassigned</c> segment above and answer 400
    /// on a failed GUID parse.
    /// <para>
    /// <strong>The response shape depends on who is asking.</strong> An assistant
    /// receives the summary projection, which has no diagnosis member at all. The
    /// doctor treating the visit receives the detail projection, which does.
    /// A doctor asking about a colleague's visit is refused rather than
    /// downgraded. The declared type below is the superset.
    /// </para>
    /// </remarks>
    /// <param name="id">The visit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The visit, projected for the caller.</returns>
    /// <response code="200">The visit. An assistant receives the summary projection, without a diagnosis.</response>
    /// <response code="403">A doctor asked for a visit that is not in their queue.</response>
    /// <response code="404">No such visit.</response>
    [HttpGet("{id:guid}", Name = GetVisitRoute)]
    [ProducesResponseType<VisitDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var visit = await queries.GetAsync(id, cancellationToken).ConfigureAwait(false);

        // Exactly one of the two is set, and which one is the whole point.
        return Ok((object?)visit.Detail ?? visit.Summary!);
    }
}
