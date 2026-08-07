using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Contracts.Authentication;
using MediQueue.Contracts.Directory;
using MediQueue.Contracts.Visits;
using MediQueue.Infrastructure.Persistence;
using MediQueue.Infrastructure.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// The push channel over a real hub: who may connect, who receives what, and
/// what the payload contains.
/// </summary>
[Collection(PostgresCollection.Name)]
public class QueueHubTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>How long a test waits for a message that should arrive.</summary>
    private static readonly TimeSpan Arrives = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a test waits to be satisfied that a message will <em>not</em>
    /// arrive.
    /// </summary>
    /// <remarks>
    /// A negative assertion on a push channel can only ever be "it did not come
    /// within this long". Kept short enough that the suite stays quick and long
    /// enough to be past the delivery of the positive message the same action
    /// produced — every such test below waits for that positive message first,
    /// so by the time this window opens the server has already sent everything
    /// it intended to.
    /// </remarks>
    private static readonly TimeSpan DoesNotArrive = TimeSpan.FromSeconds(2);

    private MediQueueApiFactory _factory = null!;
    private HttpClient _assistant = null!;
    private IReadOnlyList<SpecialtyDto> _specialties = null!;
    private readonly List<HubConnection> _connections = [];

    private static int _tajCounter = 900_000_000;

    private static string AUniqueTaj()
    {
        var digits = Interlocked.Increment(ref _tajCounter).ToString();

        return $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";
    }

    public async Task InitializeAsync()
    {
        _factory = new MediQueueApiFactory(postgres);
        await _factory.CreateReadyClientAsync();

        (_assistant, _) = await SignInAsync("horvath.anna");
        _specialties = (await _assistant.GetFromJsonAsync<List<SpecialtyDto>>("/api/specialties"))!;
    }

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        _assistant.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> SignInAsync(string username)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(username));

        var me = await client.GetFromJsonAsync<UserDto>("/api/me");

        return (client, me!.Id);
    }

    private async Task<string> TokenAsync(string username)
    {
        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(username, DatabaseSeeder.DemoPassword));

        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    /// <summary>Connects to the hub through the in-memory server as the given user.</summary>
    private async Task<HubConnection> ConnectAsync(string username, string path = QueueHub.Path)
    {
        var token = await TokenAsync(username);

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, path), options =>
            {
                // WebSockets explicitly, not whatever the in-memory server
                // negotiates down to. It is the transport the desktop client
                // uses, and it is the one whose DI scope differs: a long-polling
                // invocation is an HTTP request and would populate
                // IHttpContextAccessor for free, so a scope test that quietly
                // fell back to it would prove nothing about the real thing.
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();

                // For the queue hub the token goes on the query string, by
                // hand, because that is exactly what a real WebSocket client
                // does — it cannot set an Authorization header, which is the
                // entire reason the bearer handler has an OnMessageReceived
                // hook. SignalR's own AccessTokenProvider sets a header and
                // leaves a custom WebSocketFactory to carry it, so using it
                // would test a path the browser never takes.
                //
                // The probe hub has to use a header instead, and the reason is
                // itself a result: the hook is restricted to the queue hub's
                // path, so a query-string token on any other route — including
                // this one — is ignored and the connection is refused. That is
                // the restriction working, discovered by tripping over it.
                var overQueryString = path == QueueHub.Path;

                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var client = _factory.Server.CreateWebSocketClient();

                    if (!overQueryString)
                    {
                        client.ConfigureRequest =
                            request => request.Headers.Authorization = $"Bearer {token}";
                    }

                    var uri = overQueryString
                        ? new UriBuilder(context.Uri) { Query = $"access_token={Uri.EscapeDataString(token)}" }.Uri
                        : context.Uri;

                    return await client.ConnectAsync(uri, cancellationToken);
                };
            })
            .Build();

        _connections.Add(connection);
        await connection.StartAsync();

        return connection;
    }

    /// <summary>Captures the next payload of one event as raw JSON.</summary>
    /// <remarks>
    /// <c>JsonElement</c> rather than a deserialised DTO, so a leak test reads
    /// what actually travelled instead of what the contract admits — the same
    /// reason the audit tests assert on bytes.
    /// </remarks>
    private static TaskCompletionSource<JsonElement> Captures(HubConnection connection, string eventName)
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<JsonElement>(eventName, payload => received.TrySetResult(payload));

        return received;
    }

    private static async Task<JsonElement> WaitForAsync(TaskCompletionSource<JsonElement> capture)
    {
        var completed = await Task.WhenAny(capture.Task, Task.Delay(Arrives));

        completed.ShouldBe(capture.Task, "the expected push never arrived");

        return await capture.Task;
    }

    private static async Task ShouldStaySilentAsync(TaskCompletionSource<JsonElement> capture, string because)
    {
        var completed = await Task.WhenAny(capture.Task, Task.Delay(DoesNotArrive));

        completed.ShouldNotBe(capture.Task, because);
    }

    private Guid InternalMedicine => _specialties.Single(specialty => specialty.Name == "Belgyógyászat").Id;

    private async Task<VisitSummaryDto> RegisterAsync(Guid? specialtyId)
    {
        var response = await _assistant.PostAsJsonAsync("/api/visits", new RegisterVisitRequest(
            "Kovács Anna", "1052 Budapest, Váci utca 12.", AUniqueTaj(), "Fejfájás", specialtyId));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<VisitSummaryDto>())!;
    }

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task An_unauthenticated_connection_is_refused()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, QueueHub.Path), options =>
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();

        _connections.Add(connection);

        await Should.ThrowAsync<Exception>(() => connection.StartAsync());
        connection.State.ShouldBe(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task A_token_in_the_query_string_authenticates_a_hub_request()
    {
        // The whole reason the OnMessageReceived hook exists: a WebSocket cannot
        // set an Authorization header.
        using var anonymous = _factory.CreateClient();
        var token = await TokenAsync("horvath.anna");

        var negotiate = await anonymous.PostAsync($"{QueueHub.Path}/negotiate?access_token={token}", null);

        negotiate.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_the_query_string_token_the_same_hub_request_is_refused()
    {
        // Shows the previous test asserts the mechanism rather than an endpoint
        // that was open anyway.
        using var anonymous = _factory.CreateClient();

        var negotiate = await anonymous.PostAsync($"{QueueHub.Path}/negotiate", null);

        negotiate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_in_the_query_string_does_not_authenticate_anything_outside_the_hub()
    {
        // The security half of the same mechanism, and the reason the hook is
        // restricted by path. A token in a query string ends up in access logs,
        // proxy records and pasted URLs; if every route honoured it, any of
        // those would be a live credential for the whole API.
        using var anonymous = _factory.CreateClient();
        var token = await TokenAsync("horvath.anna");

        foreach (var path in new[] { "/api/me", "/api/queues", "/api/audit", "/api/specialties" })
        {
            var response = await anonymous.GetAsync($"{path}?access_token={token}");

            response.StatusCode.ShouldBe(
                HttpStatusCode.Unauthorized,
                $"'{path}' must not accept a token from the query string");
        }
    }

    [Fact]
    public async Task The_same_token_still_works_as_a_header_on_those_paths()
    {
        // So the previous test is not passing merely because the token was bad.
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await TokenAsync("horvath.anna"));

        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --------------------------------------------------------------- routing

    [Fact]
    public async Task An_assistant_receives_a_registration_and_a_doctor_does_not()
    {
        var assistant = await ConnectAsync("horvath.anna");
        var doctor = await ConnectAsync("kovacs.istvan");

        var toAssistant = Captures(assistant, "VisitRegistered");
        var toDoctor = Captures(doctor, "VisitRegistered");

        var visit = await RegisterAsync(specialtyId: null);

        var payload = await WaitForAsync(toAssistant);
        payload.GetProperty("id").GetGuid().ShouldBe(visit.Id);

        // An unrouted visit is in nobody's queue, so it concerns no doctor.
        await ShouldStaySilentAsync(toDoctor, "an unrouted visit concerns no doctor");
    }

    [Fact]
    public async Task The_assigned_doctor_receives_the_queue_event_and_another_doctor_does_not()
    {
        // The central authorization assertion of this phase. Group membership is
        // the mechanism: the other doctor was never a recipient, so there is no
        // filtering step that could be forgotten.
        var kovacs = await ConnectAsync("kovacs.istvan");
        var nagy = await ConnectAsync("nagy.peter");

        var toKovacs = Captures(kovacs, "VisitQueued");
        var toNagy = Captures(nagy, "VisitQueued");

        var visit = await RegisterAsync(InternalMedicine);
        visit.DoctorId.ShouldNotBeNull();

        // The server picks the doctor, so the test follows it rather than
        // assuming. Both share Belgyógyászat, which is what makes this provable.
        var (expected, excluded) = visit.DoctorFullName == "Dr. Kovács István"
            ? (toKovacs, toNagy)
            : (toNagy, toKovacs);

        var payload = await WaitForAsync(expected);
        payload.GetProperty("id").GetGuid().ShouldBe(visit.Id);

        await ShouldStaySilentAsync(excluded, "a doctor must never receive another doctor's queue event");
    }

    [Fact]
    public async Task A_call_in_reaches_the_treating_doctor_and_the_assistants()
    {
        var assistant = await ConnectAsync("horvath.anna");

        var visit = await RegisterAsync(InternalMedicine);
        var doctorUsername = visit.DoctorFullName == "Dr. Kovács István" ? "kovacs.istvan" : "nagy.peter";

        var doctor = await ConnectAsync(doctorUsername);
        var toDoctor = Captures(doctor, "VisitCalledIn");
        var toAssistant = Captures(assistant, "VisitCalledIn");

        using var doctorHttp = _factory.CreateClient();
        doctorHttp.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await TokenAsync(doctorUsername));

        (await doctorHttp.PostAsync($"/api/visits/{visit.Id}/call-in", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await WaitForAsync(toDoctor)).GetProperty("id").GetGuid().ShouldBe(visit.Id);
        (await WaitForAsync(toAssistant)).GetProperty("id").GetGuid().ShouldBe(visit.Id);
    }

    [Fact]
    public async Task Withdrawing_a_visit_pushes_the_identifiers_to_both()
    {
        var assistant = await ConnectAsync("horvath.anna");
        var visit = await RegisterAsync(InternalMedicine);

        var toAssistant = Captures(assistant, "VisitDeleted");

        (await _assistant.DeleteAsync($"/api/visits/{visit.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var payload = await WaitForAsync(toAssistant);

        payload.GetProperty("visitId").GetGuid().ShouldBe(visit.Id);
        payload.GetProperty("doctorId").GetGuid().ShouldBe(visit.DoctorId!.Value);
    }

    [Fact]
    public async Task Recording_a_diagnosis_pushes_nothing_to_anybody()
    {
        // The one action with no event, asserted from the outside. The payload
        // type could not carry a diagnosis in any case; not publishing at all
        // means the question never arises.
        var assistant = await ConnectAsync("horvath.anna");

        var visit = await RegisterAsync(InternalMedicine);
        var doctorUsername = visit.DoctorFullName == "Dr. Kovács István" ? "kovacs.istvan" : "nagy.peter";

        using var doctorHttp = _factory.CreateClient();
        doctorHttp.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await TokenAsync(doctorUsername));

        var calledIn = Captures(assistant, "VisitCalledIn");
        await doctorHttp.PostAsync($"/api/visits/{visit.Id}/call-in", null);
        await WaitForAsync(calledIn);

        // Only now, with the connection proven live, is silence meaningful.
        var anyEvent = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        foreach (var name in new[] { "VisitRegistered", "VisitQueued", "VisitCalledIn", "VisitReleased", "VisitDeleted" })
        {
            assistant.On<JsonElement>(name, payload => anyEvent.TrySetResult(payload));
        }

        (await doctorHttp.PutAsJsonAsync(
            $"/api/visits/{visit.Id}/diagnosis", new RecordDiagnosisRequest("Migrén, feszültséges eredetű")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await ShouldStaySilentAsync(anyEvent, "recording a diagnosis publishes no event at all");
    }

    // --------------------------------------------------------------- payload

    [Fact]
    public async Task The_pushed_payload_carries_no_diagnosis_key()
    {
        // D-10 through the push channel. The payload type declares no diagnosis
        // member, so this asserts the guarantee held rather than that somebody
        // remembered to strip a field.
        var assistant = await ConnectAsync("horvath.anna");
        var visit = await RegisterAsync(InternalMedicine);
        var doctorUsername = visit.DoctorFullName == "Dr. Kovács István" ? "kovacs.istvan" : "nagy.peter";

        using var doctorHttp = _factory.CreateClient();
        doctorHttp.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await TokenAsync(doctorUsername));

        await doctorHttp.PostAsync($"/api/visits/{visit.Id}/call-in", null);
        await doctorHttp.PutAsJsonAsync(
            $"/api/visits/{visit.Id}/diagnosis", new RecordDiagnosisRequest("Migrén, feszültséges eredetű"));

        var released = Captures(assistant, "VisitReleased");
        (await doctorHttp.PostAsync($"/api/visits/{visit.Id}/release", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await WaitForAsync(released);

        // The visit has a diagnosis by now, so there was something to leak.
        payload.TryGetProperty("diagnosis", out _).ShouldBeFalse("the push payload must have no diagnosis key");
        payload.GetRawText().ShouldNotContain("Migrén");
        payload.GetProperty("status").GetInt32().ShouldBe((int)VisitStatus.Done);
    }

    // ----------------------------------------------------------------- scope

    [Fact]
    public async Task A_hub_invocation_resolves_the_identity_the_same_way_a_request_does()
    {
        // The trap the brief named. Nothing writes through a hub today, so this
        // is not currently load-bearing — it exists so that the phase which adds
        // a writing hub method does not rediscover D-37 one transport along,
        // where the symptom would be an audit trail with no actor and everything
        // else still working.
        var probe = await ConnectAsync("kovacs.istvan", ScopeProbeHub.Path);

        var seen = await probe.InvokeAsync<ScopeProbeHub.IdentitySnapshot>("WhoAmI");

        var (_, expectedId) = await SignInAsync("kovacs.istvan");

        seen.IsAuthenticated.ShouldBeTrue();
        seen.Role.ShouldBe("Doctor");
        seen.UserId.ShouldBe(expectedId, "a hub invocation must see the same user id an HTTP request does");
    }
}
