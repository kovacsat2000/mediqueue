using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Visits;
using MediQueue.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// Endpoints that exist only to make the cross-cutting rules observable.
/// </summary>
/// <remarks>
/// <para>
/// They live in the test assembly and are added as an MVC application part by
/// the test factory, so they cannot reach a deployed build — which is why this
/// is preferable to adding a throwaway endpoint to the API and deleting it
/// again. The assertions stay in the suite rather than being made once by hand.
/// </para>
/// <para>
/// Two of these cover rules with no production endpoint yet: the
/// <c>DoctorOnly</c> policy, whose first real use arrives in P4, and the invalid
/// transition mapping, which nothing can trigger until visits have endpoints.
/// </para>
/// </remarks>
[ApiController]
[Route("test-only")]
public sealed class TestOnlyController : ControllerBase
{
    /// <summary>Carries no authorization attribute, so only the fallback policy protects it.</summary>
    [HttpGet("unattributed")]
    public IActionResult Unattributed() => Ok("reached");

    /// <summary>Guarded by the doctor policy.</summary>
    [Authorize(Policy = AuthorizationPolicies.DoctorOnly)]
    [HttpGet("doctor-only")]
    public IActionResult DoctorOnly() => Ok("reached");

    /// <summary>Guarded by the assistant policy.</summary>
    [Authorize(Policy = AuthorizationPolicies.AssistantOnly)]
    [HttpGet("assistant-only")]
    public IActionResult AssistantOnly() => Ok("reached");

    /// <summary>Reports what the framework itself thinks of the caller's role.</summary>
    /// <remarks>
    /// Reading <c>User.IsInRole</c> through a real request is the only way to
    /// prove the claim mapping is right. Inspecting the token proves only that
    /// the token is right, which is the half that was never in doubt.
    /// </remarks>
    [HttpGet("role-check")]
    public IActionResult RoleCheck() => Ok(new
    {
        IsDoctor = User.IsInRole(nameof(Domain.Users.UserRole.Doctor)),
        IsAssistant = User.IsInRole(nameof(Domain.Users.UserRole.Assistant)),
        Name = User.Identity?.Name,
        ClaimTypes = User.Claims.Select(claim => claim.Type).ToArray(),
    });

    /// <summary>Throws something that is nobody's fault but ours.</summary>
    [HttpGet("boom")]
    public IActionResult Boom() =>
        throw new InvalidOperationException("Sensitive internal detail that must never reach a client.");

    /// <summary>Throws the transition error the state machine produces.</summary>
    [HttpGet("invalid-transition")]
    public IActionResult InvalidTransition()
    {
        VisitStateMachine.EnsureCanTransition(VisitStatus.Registered, VisitStatus.Done);
        return Ok();
    }

    /// <summary>Throws the error EF Core raises when a row changed underneath us.</summary>
    /// <remarks>
    /// The concurrency mapping has no production endpoint that can reach it
    /// deterministically: provoking a real xmin conflict through HTTP would need
    /// two interleaved requests. The persistence suite proves the token itself
    /// works against a real database; this proves the status it turns into.
    /// </remarks>
    [HttpGet("concurrency-conflict")]
    public IActionResult ConcurrencyConflict() =>
        throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Row was modified.");

    /// <summary>Throws a field-level validation error.</summary>
    [HttpGet("validation-failure")]
    public IActionResult ValidationFailure() =>
        throw new ValidationException("Taj", "TAJ number must be nine digits in the form 123-123-123.");
}
