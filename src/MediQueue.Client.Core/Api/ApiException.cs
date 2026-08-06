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
    private ApiException(int status, string? title, string detail, string? traceId)
        : base(detail)
    {
        Status = status;
        Title = title;
        Detail = detail;
        TraceId = traceId;
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
                        return new ApiException(status, Text(root, "title"), detail, Text(root, "traceId"));
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
