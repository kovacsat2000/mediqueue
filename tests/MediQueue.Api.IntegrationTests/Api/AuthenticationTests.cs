using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Contracts;
using MediQueue.Contracts.Authentication;
using MediQueue.Infrastructure.Persistence;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// Authentication and authorization exercised through the real pipeline.
/// </summary>
/// <remarks>
/// Every assertion here goes over HTTP against the application as it ships.
/// Inspecting a token proves the token is right; only a real request proves the
/// framework agrees.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class AuthenticationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string AssistantUsername = "horvath.anna";
    private const string DoctorUsername = "kovacs.istvan";

    private MediQueueApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new MediQueueApiFactory(postgres);
        _client = await _factory.CreateReadyClientAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<LoginResponse> LoginAsync(string username, string password = DatabaseSeeder.DemoPassword)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<HttpClient> SignedInAsAsync(string username)
    {
        var login = await LoginAsync(username);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        return client;
    }

    private static JsonElement ClaimsOf(string accessToken)
    {
        var payload = accessToken.Split('.')[1];
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        return JsonSerializer.Deserialize<JsonElement>(
            Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/')));
    }

    [Fact]
    public async Task An_assistant_signs_in_and_gets_a_token_without_a_specialty()
    {
        var login = await LoginAsync(AssistantUsername);

        login.User.Username.ShouldBe(AssistantUsername);
        login.User.Role.ShouldBe(UserRole.Assistant);
        login.User.SpecialtyId.ShouldBeNull();
        login.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        var claims = ClaimsOf(login.AccessToken);
        claims.GetProperty("sub").GetString().ShouldBe(login.User.Id.ToString());
        claims.GetProperty("name").GetString().ShouldBe(login.User.FullName);
        claims.GetProperty("role").GetString().ShouldBe(nameof(UserRole.Assistant));
        claims.TryGetProperty("specialtyId", out _).ShouldBeFalse("an assistant has no specialty");
    }

    [Fact]
    public async Task A_doctor_signs_in_and_the_token_carries_their_specialty()
    {
        var login = await LoginAsync(DoctorUsername);

        login.User.Role.ShouldBe(UserRole.Doctor);
        login.User.SpecialtyId.ShouldNotBeNull();

        var claims = ClaimsOf(login.AccessToken);
        claims.GetProperty("role").GetString().ShouldBe(nameof(UserRole.Doctor));
        claims.GetProperty("specialtyId").GetString().ShouldBe(login.User.SpecialtyId.ToString());
    }

    [Theory]
    [InlineData(DoctorUsername, "wrong-password")]
    [InlineData("no.such.user", DatabaseSeeder.DemoPassword)]
    public async Task A_refused_sign_in_says_nothing_about_which_part_was_wrong(string username, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Invalid username or password.");

        // Nothing in the body may hint at which of the two it was.
        body.ShouldNotContain("password is", Case.Insensitive);
        body.ShouldNotContain("not found", Case.Insensitive);
        body.ShouldNotContain("unknown", Case.Insensitive);
        body.ShouldNotContain(username, Case.Insensitive);
    }

    [Fact]
    public async Task The_two_kinds_of_refusal_are_byte_for_byte_identical_apart_from_the_trace_id()
    {
        var wrongPassword = await _client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(DoctorUsername, "wrong-password"));
        var unknownUser = await _client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("no.such.user", DatabaseSeeder.DemoPassword));

        static string WithoutTraceId(string body) =>
            string.Join(',', body.Split(',').Where(part => !part.Contains("traceId", StringComparison.Ordinal)));

        WithoutTraceId(await wrongPassword.Content.ReadAsStringAsync())
            .ShouldBe(WithoutTraceId(await unknownUser.Content.ReadAsStringAsync()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-token")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ4In0.wrong-signature")]
    public async Task A_missing_or_malformed_token_is_refused(string? token)
    {
        var client = _factory.CreateClient();

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_framework_itself_agrees_about_the_role()
    {
        // The claim-mapping trap. Without MapInboundClaims = false and an
        // explicit RoleClaimType, the handler renames "role" to a WS-Federation
        // URI, IsInRole finds nothing, and every policy silently refuses.
        var doctor = await SignedInAsAsync(DoctorUsername);

        var body = await doctor.GetFromJsonAsync<JsonElement>("/test-only/role-check");

        body.GetProperty("isDoctor").GetBoolean().ShouldBeTrue();
        body.GetProperty("isAssistant").GetBoolean().ShouldBeFalse();
        body.GetProperty("name").GetString().ShouldBe("Dr. Kovács István");

        // The claim types must still be the short names we issued.
        var claimTypes = body.GetProperty("claimTypes").EnumerateArray().Select(x => x.GetString()).ToList();
        claimTypes.ShouldContain("role");
        claimTypes.ShouldContain("sub");
        claimTypes.ShouldNotContain(type => type!.StartsWith("http://schemas.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_assistant_reaching_a_doctor_endpoint_is_forbidden_rather_than_unauthenticated()
    {
        // 403, not 401. The difference matters: 401 tells a client to sign in
        // again, which would send an assistant round a loop they can never win.
        var assistant = await SignedInAsAsync(AssistantUsername);

        (await assistant.GetAsync("/test-only/doctor-only")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await assistant.GetAsync("/test-only/assistant-only")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_doctor_reaching_an_assistant_endpoint_is_forbidden()
    {
        var doctor = await SignedInAsAsync(DoctorUsername);

        (await doctor.GetAsync("/test-only/assistant-only")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await doctor.GetAsync("/test-only/doctor-only")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_endpoint_with_no_authorization_attribute_is_still_protected()
    {
        // The fallback policy. A new endpoint is closed unless somebody opens it
        // deliberately, so forgetting costs a 401 in testing rather than an open
        // door in production.
        (await _client.GetAsync("/test-only/unattributed")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var signedIn = await SignedInAsAsync(AssistantUsername);
        (await signedIn.GetAsync("/test-only/unattributed")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_health_and_login_are_reachable_without_a_token()
    {
        (await _client.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);

        foreach (var path in new[] { "/api/me", "/api/specialties", "/api/doctors" })
        {
            (await _client.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{path} must be closed");
        }
    }
}
