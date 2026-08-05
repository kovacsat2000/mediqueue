using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Contracts.Authentication;
using MediQueue.Infrastructure.Persistence;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// Every way the system says no, checked on the wire.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ErrorModelTests(PostgresFixture postgres) : IAsyncLifetime
{
    private MediQueueApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new MediQueueApiFactory(postgres);
        await _factory.CreateReadyClientAsync();

        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("horvath.anna", DatabaseSeeder.DemoPassword));
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task An_invalid_transition_tells_the_client_what_it_could_have_done_instead()
    {
        // The specification's "meaningful error message" requirement. A client
        // must be able to render "this patient has already been released" and
        // grey out the impossible buttons without parsing English.
        var response = await _client.GetAsync("/test-only/invalid-transition");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("type").GetString().ShouldBe("https://mediqueue.example/problems/invalid-visit-transition");
        problem.GetProperty("status").GetInt32().ShouldBe(409);
        problem.GetProperty("currentStatus").GetString().ShouldBe("Registered");
        problem.GetProperty("attemptedStatus").GetString().ShouldBe("Done");
        problem.GetProperty("allowedTransitions").EnumerateArray()
            .Select(value => value.GetString())
            .ShouldBe(["Waiting"]);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_validation_failure_is_reported_against_the_field_that_failed()
    {
        var response = await _client.GetAsync("/test-only/validation-failure");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("type").GetString().ShouldBe("https://mediqueue.example/problems/validation-failed");
        problem.GetProperty("errors").GetProperty("Taj").EnumerateArray().ShouldNotBeEmpty();
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_unexpected_failure_describes_nothing_but_its_trace_id()
    {
        var response = await _client.GetAsync("/test-only/boom");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();

        // Nothing about our internals may cross the boundary.
        body.ShouldNotContain("InvalidOperationException");
        body.ShouldNotContain("Sensitive internal detail");
        body.ShouldNotContain("at MediQueue");
        body.ShouldNotContain("StackTrace", Case.Insensitive);
        body.ShouldNotContain("TestOnlyController");

        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("status").GetInt32().ShouldBe(500);
    }

    [Fact]
    public async Task Every_refusal_carries_a_trace_id_and_the_problem_media_type()
    {
        var anonymous = _factory.CreateClient();

        foreach (var response in new[]
                 {
                     await _client.GetAsync("/test-only/invalid-transition"),
                     await _client.GetAsync("/test-only/validation-failure"),
                     await _client.GetAsync("/test-only/boom"),
                     await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody", "nothing")),
                 })
        {
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
            problem.GetProperty("type").GetString().ShouldStartWith("https://mediqueue.example/problems/");
        }
    }
}
