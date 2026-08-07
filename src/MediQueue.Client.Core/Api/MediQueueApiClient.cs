using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Contracts.Authentication;
using MediQueue.Contracts.Directory;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.Api;

/// <summary>The server, as the desktop clients see it.</summary>
/// <remarks>
/// <para>
/// One implementation behind two role-scoped interfaces. The split is not a
/// second authorization layer — the server already refuses each endpoint to the
/// wrong role — it is what makes each shell's reachable surface visible in one
/// file, and what turns "the assistant application asks for a diagnosis" from a
/// 403 during the demo into a compile error.
/// </para>
/// <para>
/// Registering the concrete type is deliberately not done anywhere: each
/// composition root registers only its own interface against this
/// implementation, so a shell cannot reach past its half by resolving the class.
/// </para>
/// <para>
/// The base address is configured on the injected <see cref="HttpClient"/> by
/// the composition root, which reads it from configuration. There is no address
/// literal in this project.
/// </para>
/// </remarks>
public sealed class MediQueueApiClient(HttpClient http, IAuthSession session) : IAssistantApi, IDoctorApi
{
    /// <summary>The API serialises camelCase; this reads it back the same way.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc cref="IAssistantApi.LoginAsync" />
    /// <remarks>
    /// The session is updated here rather than by the caller, so no call site
    /// can sign in and then forget to record it.
    /// </remarks>
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

    // ------------------------------------------------------------ assistant

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpecialtyDto>> GetSpecialtiesAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/specialties");

        return await SendAsync<List<SpecialtyDto>>(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueueDto>> GetAllQueuesAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/queues");

        return await SendAsync<List<QueueDto>>(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VisitSummaryDto>> GetUnassignedAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/visits/unassigned");

        return await SendAsync<List<VisitSummaryDto>>(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VisitSummaryDto> RegisterVisitAsync(
        RegisterVisitRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/visits")
        {
            Content = JsonContent.Create(request, options: SerializerOptions),
        };

        return await SendAsync<VisitSummaryDto>(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VisitSummaryDto> AssignSpecialtyAsync(
        Guid visitId,
        Guid specialtyId,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/visits/{visitId}/assign")
        {
            Content = JsonContent.Create(new AssignSpecialtyRequest(specialtyId), options: SerializerOptions),
        };

        return await SendAsync<VisitSummaryDto>(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteVisitAsync(Guid visitId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/visits/{visitId}");

        await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // --------------------------------------------------------------- doctor

    /// <inheritdoc />
    public async Task<IReadOnlyList<VisitSummaryDto>> GetMyQueueAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/queues/mine");

        return await SendAsync<List<VisitSummaryDto>>(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VisitDetailDto> GetVisitAsync(Guid visitId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/visits/{visitId}");

        return await SendAsync<VisitDetailDto>(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VisitDetailDto> CallInAsync(Guid visitId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/visits/{visitId}/call-in");

        return await SendAsync<VisitDetailDto>(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VisitDetailDto> RecordDiagnosisAsync(
        Guid visitId,
        string diagnosis,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/visits/{visitId}/diagnosis")
        {
            Content = JsonContent.Create(new RecordDiagnosisRequest(diagnosis), options: SerializerOptions),
        };

        return await SendAsync<VisitDetailDto>(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VisitDetailDto> ReleaseAsync(Guid visitId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/visits/{visitId}/release");

        return await SendAsync<VisitDetailDto>(request, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ transport

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   $"The server returned an empty body where a {typeof(T).Name} was expected.");
    }

    /// <summary>Sends a request whose success carries no body, such as a 204.</summary>
    private async Task SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Every request goes through here, so the token is attached in exactly
        // one place and no endpoint method can omit it.
        session.Authorize(request);

        var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                throw await ApiException.FromAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }

        return response;
    }
}
