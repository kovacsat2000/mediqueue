using System.Diagnostics;
using MediQueue.Application.Authentication;
using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Visits;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Api.Errors;

/// <summary>
/// Turns every exception the system can raise into an RFC 9457 problem document.
/// </summary>
/// <remarks>
/// <para>
/// This is the single place the specification's requirement that an invalid
/// transition produce a meaningful error message is implemented. "Meaningful"
/// is taken to mean a client can act on it without reading English prose, which
/// is why the transition case puts the states into extension members rather than
/// only into the message.
/// </para>
/// <para>
/// The order of the checks matters: the most specific exception type is matched
/// first, and <see cref="DomainException"/> is the catch-all for rules that have
/// no dedicated mapping. Anything that is not a broken rule is a defect in this
/// system, and a defect tells the caller nothing but a trace id.
/// </para>
/// </remarks>
public sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    private const string TypeBase = "https://mediqueue.example/problems/";

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        var problem = Describe(exception, traceId);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            // Logged in full, answered with nothing. The trace id is the thread
            // between the two, so a user can quote it and an engineer can find it.
            logger.LogError(exception, "Unhandled exception. TraceId {TraceId}", traceId);
        }
        else
        {
            logger.LogInformation(
                "Request refused: {Problem} ({Status}). TraceId {TraceId}",
                problem.Title,
                problem.Status,
                traceId);
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        }).ConfigureAwait(false);
    }

    private static ProblemDetails Describe(Exception exception, string traceId) => exception switch
    {
        // Most specific first. ValidationException is a DomainException, and
        // InvalidVisitTransitionException is too.
        ValidationException validation => Problem(
            status: StatusCodes.Status400BadRequest,
            type: "validation-failed",
            title: "Validation failed",
            detail: validation.Message,
            traceId: traceId,
            extensions: new Dictionary<string, object?>
            {
                // Shaped like the framework's own validation problem so a client
                // has one thing to render rather than two.
                ["errors"] = new Dictionary<string, string[]>
                {
                    [validation.FieldName] = [validation.Message],
                },
            }),

        InvalidVisitTransitionException transition => Problem(
            status: StatusCodes.Status409Conflict,
            type: "invalid-visit-transition",
            title: "The visit cannot move to that state",
            detail: transition.Message,
            traceId: traceId,
            extensions: new Dictionary<string, object?>
            {
                // The whole point. A client can say "this patient has already
                // been released" and can grey out the buttons that would fail,
                // without parsing the sentence in `detail`.
                ["currentStatus"] = transition.From.ToString(),
                ["attemptedStatus"] = transition.To.ToString(),
                ["allowedTransitions"] = transition.AllowedAlternatives
                    .Order()
                    .Select(status => status.ToString())
                    .ToArray(),
            }),

        DomainException domain => Problem(
            status: StatusCodes.Status400BadRequest,
            type: "domain-rule-violated",
            title: "The request breaks a business rule",
            detail: domain.Message,
            traceId: traceId),

        AuthenticationFailedException => Problem(
            status: StatusCodes.Status401Unauthorized,
            type: "authentication-failed",
            title: "Authentication failed",
            // The exception's own message, which is deliberately the same
            // whether the username was unknown, the password wrong, or the
            // account disabled.
            detail: AuthenticationFailedException.GenericMessage,
            traceId: traceId),

        DbUpdateConcurrencyException => Problem(
            status: StatusCodes.Status409Conflict,
            type: "concurrent-modification",
            title: "The record changed while you were working on it",
            detail: "Someone else modified this record. Reload it and try again.",
            traceId: traceId),

        _ => Problem(
            status: StatusCodes.Status500InternalServerError,
            type: "unexpected-error",
            title: "An unexpected error occurred",
            // Deliberately says nothing: no message, no exception type, no stack
            // trace. All of those describe our internals to whoever asked.
            detail: $"An unexpected error occurred. Quote trace id '{traceId}' when reporting it.",
            traceId: traceId),
    };

    private static ProblemDetails Problem(
        int status,
        string type,
        string title,
        string detail,
        string traceId,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Type = TypeBase + type,
            Title = title,
            Detail = detail,
        };

        problem.Extensions["traceId"] = traceId;

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return problem;
    }
}
