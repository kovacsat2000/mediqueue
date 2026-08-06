using System.Net;
using System.Text;

namespace MediQueue.Client.Core.Tests;

/// <summary>
/// Returns canned responses and records what was asked for.
/// </summary>
/// <remarks>
/// No server and no container: these tests are about what the client sends and
/// how it reads what comes back, and neither question needs a real endpoint.
/// </remarks>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    /// <summary>Every request the client sent, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The most recent request.</summary>
    public HttpRequestMessage LastRequest => Requests[^1];

    /// <summary>Queues a JSON response.</summary>
    /// <param name="status">The status code.</param>
    /// <param name="json">The body.</param>
    /// <param name="contentType">The media type; problem responses use <c>application/problem+json</c>.</param>
    /// <returns>This handler, for chaining.</returns>
    public StubHttpMessageHandler Respond(
        HttpStatusCode status,
        string json,
        string contentType = "application/json")
    {
        _responses.Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, contentType),
        });

        return this;
    }

    /// <summary>Queues a response with no body at all.</summary>
    /// <param name="status">The status code.</param>
    /// <returns>This handler, for chaining.</returns>
    public StubHttpMessageHandler RespondEmpty(HttpStatusCode status)
    {
        _responses.Enqueue(new HttpResponseMessage(status) { Content = new StringContent(string.Empty) });

        return this;
    }

    /// <summary>Builds a client pointed at a base address, as the composition root would.</summary>
    /// <returns>The client.</returns>
    public HttpClient CreateClient() =>
        new(this, disposeHandler: false) { BaseAddress = new Uri("http://localhost:5123/") };

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return Task.FromResult(_responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.NotImplemented)
            {
                Content = new StringContent("the test queued no response for this request"),
            });
    }
}
