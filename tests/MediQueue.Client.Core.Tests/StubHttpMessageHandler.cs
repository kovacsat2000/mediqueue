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

    /// <summary>
    /// The body of every request, captured while it was still readable.
    /// </summary>
    /// <remarks>
    /// The client disposes each request once it has been sent, which disposes
    /// its content with it — so reading <c>LastRequest.Content</c> afterwards
    /// throws <c>ObjectDisposedException</c>. Recording the text here is the
    /// honest fix: the assertion is about what was sent, and what was sent is a
    /// string, not a live object.
    /// </remarks>
    public List<string> Bodies { get; } = [];

    /// <summary>The body of the most recent request, or empty if it had none.</summary>
    public string LastBody => Bodies.Count > 0 ? Bodies[^1] : string.Empty;

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

    private Exception? _transportFailure;

    /// <summary>Makes every later send fail, as an unreachable server does.</summary>
    /// <param name="failure">What the transport throws.</param>
    /// <returns>This handler, for chaining.</returns>
    public StubHttpMessageHandler FailTransportWith(Exception failure)
    {
        _transportFailure = failure;

        return this;
    }

    private TaskCompletionSource? _gate;

    /// <summary>
    /// Holds every response open until the returned handle is released.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this the handler returns an already-completed task, so an
    /// <c>await</c> on it never yields and a refresh runs from its first line to
    /// its last without the scheduler getting a look in. That makes it
    /// impossible for anything to interleave with a refresh — fine for most
    /// tests here, and fatal for the two whose entire subject is the
    /// interleaving.
    /// </para>
    /// <para>
    /// Found by mutation testing: removing the very lock those two tests exist
    /// to exercise killed neither of them. They were green and asserting
    /// nothing, which is D-45 in a concurrency test.
    /// </para>
    /// </remarks>
    /// <returns>The gate. Complete it to let the held responses through.</returns>
    public TaskCompletionSource HoldResponses()
    {
        _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return _gate;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        Bodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (_gate is { } gate)
        {
            await gate.Task.ConfigureAwait(false);
        }

        if (_transportFailure is { } failure)
        {
            throw failure;
        }

        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.NotImplemented)
            {
                Content = new StringContent("the test queued no response for this request"),
            };
    }
}
