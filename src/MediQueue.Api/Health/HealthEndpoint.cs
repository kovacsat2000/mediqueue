using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediQueue.Api.Health;

/// <summary>
/// Maps <c>GET /health</c> and writes the MediQueue health payload.
/// </summary>
public static class HealthEndpoint
{
    /// <summary>
    /// Read from this assembly rather than <see cref="Assembly.GetEntryAssembly"/>:
    /// under <c>WebApplicationFactory</c> the entry assembly is the test host, so
    /// the endpoint would report the test runner's version instead of the API's.
    /// </summary>
    private static readonly string ApiVersion =
        typeof(HealthEndpoint).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    // Built once: System.Text.Json rebuilds its metadata cache for every new
    // options instance, and the response writer runs on every request.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Maps the health endpoint. No database check yet — that arrives with the
    /// DbContext in P2.
    /// </summary>
    public static IEndpointRouteBuilder MapMediQueueHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteResponseAsync,
        })
        // One of only two anonymous endpoints. A health check that needed a
        // token could not be read by the thing most likely to ask: a load
        // balancer, a container orchestrator, or a monitor.
        .AllowAnonymous();

        return endpoints;
    }

    private static async Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        // HealthStatus has exactly three members — Unhealthy, Degraded, Healthy —
        // so lowercasing the name yields the documented wire values. The status
        // code itself is chosen by the middleware before this writer runs.
        var payload = new HealthResponse(
            report.Status.ToString().ToLowerInvariant(),
            ApiVersion,
            DateTimeOffset.UtcNow);

        await context.Response.WriteAsJsonAsync(payload, SerializerOptions, context.RequestAborted);
    }

    /// <summary>
    /// The property names are stated explicitly rather than left to a naming
    /// policy: this is a published contract, and it should not change because
    /// someone reconfigures the API's serializer.
    /// </summary>
    private sealed record HealthResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("utc")] DateTimeOffset Utc);
}
