using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Contracts.Authentication;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.Api;

/// <summary>The server, as the desktop clients see it.</summary>
/// <remarks>
/// The base address is configured on the injected <see cref="HttpClient"/> by
/// the composition root, which reads it from configuration. There is no address
/// literal in this project.
/// </remarks>
public sealed class MediQueueApiClient(HttpClient http, IAuthSession session)
{
    /// <summary>The API serialises camelCase; this reads it back the same way.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Signs in and remembers the token for every later call.</summary>
    /// <remarks>
    /// The session is updated here rather than by the caller, so no call site
    /// can sign in and then forget to record it.
    /// </remarks>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The token, its expiry, and the signed-in user.</returns>
    /// <exception cref="ApiException">The server refused.</exception>
    public async Task<LoginResponse> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password), options: SerializerOptions),
        };

        var login = await SendAsync<LoginResponse>(request, cancellationToken).ConfigureAwait(false);
        session.SignIn(login);

        return login;
    }

    /// <summary>The signed-in doctor's own queue, in arrival order.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Their waiting and in-treatment visits.</returns>
    /// <exception cref="ApiException">The server refused.</exception>
    public async Task<IReadOnlyList<VisitSummaryDto>> GetMyQueueAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/queues/mine");

        return await SendAsync<List<VisitSummaryDto>>(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Every request goes through here, so the token is attached in exactly
        // one place and no endpoint method can omit it.
        session.Authorize(request);

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await ApiException.FromAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return await response.Content
                   .ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   $"The server returned an empty body where a {typeof(T).Name} was expected.");
    }
}
