using System.Net;
using System.Text.Json;

namespace MediQueue.Client.Core.Api;

/// <summary>
/// The server refused a request, described by whatever it told us.
/// </summary>
/// <remarks>
/// The server answers every failure as RFC 9457 <c>application/problem+json</c>,
/// so the useful case is parsing that. The awkward case is everything else — a
/// proxy error page, an empty 502, a connection cut mid-body — where the client
/// must still produce something a user can read rather than throwing while it
/// handles a throw.
/// </remarks>
public sealed class ApiException : Exception
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private ApiException(
        int status,
        string? title,
        string detail,
        string? traceId,
        IReadOnlyDictionary<string, string[]>? errors = null)
        : base(detail)
    {
        Status = status;
        Title = title;
        Detail = detail;
        TraceId = traceId;
        Errors = errors ?? NoErrors;
    }

    /// <summary>The HTTP status code.</summary>
    public int Status { get; }

    /// <summary>The problem's title, when the server sent one.</summary>
    public string? Title { get; }

    /// <summary>What to show the user. Never empty.</summary>
    public string Detail { get; }

    /// <summary>
    /// The server's trace id, when it sent one.
    /// </summary>
    /// <remarks>
    /// This is what makes a user's complaint traceable to a log line: the body
    /// of a 500 says nothing else on purpose, and the id is the thread back to
    /// the entry that says everything.
    /// </remarks>
    public string? TraceId { get; }

    /// <summary>
    /// The server's per-field messages from a validation failure, keyed by the
    /// field name the domain used. Empty for every other kind of failure.
    /// </summary>
    /// <remarks>
    /// This is what lets a form put the message next to the input that caused
    /// it. It is deliberately the server's own text: the clients do not
    /// re-implement the TAJ or name rules, so the only place that knows why a
    /// value was refused is the domain that refused it.
    /// </remarks>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>The server's message for one field, if it named that field.</summary>
    /// <param name="fieldName">The field, matched case-insensitively.</param>
    /// <returns>The first message, or <c>null</c>.</returns>
    public string? ErrorFor(string fieldName) =>
        Errors.TryGetValue(fieldName, out var messages) && messages.Length > 0 ? messages[0] : null;

    /// <summary>Builds the exception from a failed response, however it is shaped.</summary>
    /// <param name="response">The failed response.</param>
    /// <param name="cancellationToken">Cancels reading the body.</param>
    /// <returns>An exception carrying the best message available.</returns>
    public static async Task<ApiException> FromAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var status = (int)response.StatusCode;
        string? body = null;

        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    var detail = Text(root, "detail") ?? Text(root, "title");

                    if (detail is not null)
                    {
                        return new ApiException(
                            status,
                            Text(root, "title"),
                            detail,
                            Text(root, "traceId"),
                            FieldErrors(root));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON at all — a proxy page, or HTML from something in the way.
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            // The body could not be read. The status is still worth reporting.
        }

        return new ApiException(status, null, Fallback(response.StatusCode), null);
    }

    /// <summary>
    /// Reads the <c>errors</c> extension: field name to messages.
    /// </summary>
    /// <remarks>
    /// Shaped like ASP.NET Core's own validation problem, which is what the
    /// server deliberately emits, so a client has one thing to render rather
    /// than two. Anything that is not that shape is ignored rather than
    /// throwing — this method runs while the client is already handling a
    /// failure, and failing here would replace the server's message with a
    /// parser's.
    /// </remarks>
    private static IReadOnlyDictionary<string, string[]> FieldErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Object)
        {
            return NoErrors;
        }

        var byField = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in errors.EnumerateObject())
        {
            if (field.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            byField[field.Name] =
            [
                .. field.Value.EnumerateArray()
                    .Where(message => message.ValueKind == JsonValueKind.String)
                    .Select(message => message.GetString()!),
            ];
        }

        return byField;
    }

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// A sentence for the cases where the server said nothing usable.
    /// </summary>
    /// <remarks>
    /// Phrased for somebody at a reception desk rather than for a developer:
    /// "502 Bad Gateway" is not something they can act on, and "the server is
    /// not reachable" is.
    /// </remarks>
    private static string Fallback(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Your session is not valid. Sign in again.",
        HttpStatusCode.Forbidden => "You are not allowed to do that.",
        HttpStatusCode.NotFound => "That record no longer exists.",
        >= HttpStatusCode.InternalServerError => $"The server could not complete the request ({(int)status}).",
        _ => $"The request was refused ({(int)status}).",
    };
}
