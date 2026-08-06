using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Contracts.Authentication;
using MediQueue.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>Assertions the phase-3 review asked to be carried forward.</summary>
[Collection(PostgresCollection.Name)]
public class CarriedFromPhase3Tests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_expiry_in_a_real_login_response_comes_from_the_injected_clock()
    {
        // Through POST /api/auth/login rather than against the issuer directly,
        // which is what the review asked for. Substituting the clock in the
        // factory turned out to have no side effects worth reporting: the seeder
        // uses the same TimeProvider, so it simply seeds that day's morning.
        var frozen = new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero);

        await using var factory = new MediQueueApiFactory(postgres)
            .WithClock(new FakeTimeProvider(frozen));

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("horvath.anna", DatabaseSeeder.DemoPassword));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        // Exactly the frozen instant plus the configured eight-hour lifetime.
        login.ExpiresAt.ShouldBe(frozen.AddHours(8));
    }

    [Fact]
    public async Task The_openapi_document_is_not_served_outside_development()
    {
        // The document and its UI are mapped only in Development, so outside it
        // they are absent rather than merely closed.
        await using var factory = new ProductionApiFactory(postgres);
        var client = factory.CreateClient();

        var document = await client.GetAsync("/openapi/v1.json");
        var ui = await client.GetAsync("/scalar/");

        // Not the document is the assertion; the exact status is 401 rather than
        // 404, and deliberately so. The routes are not mapped outside
        // Development, and ASP.NET Core applies the fallback policy to requests
        // that match no endpoint at all — so an anonymous caller cannot tell an
        // absent route from a protected one, which is the better answer of the
        // two.
        document.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ui.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        document.Content.Headers.ContentType?.MediaType.ShouldNotBe("application/json");
        (await document.Content.ReadAsStringAsync()).ShouldNotContain("openapi");

        // The application is genuinely up — this is not a host that failed to boot.
        (await client.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_concurrency_conflict_becomes_a_409()
    {
        await using var factory = new MediQueueApiFactory(postgres);
        await factory.CreateReadyClientAsync();

        var login = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("horvath.anna", DatabaseSeeder.DemoPassword));
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/test-only/concurrency-conflict");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("type").GetString()
            .ShouldBe("https://mediqueue.example/problems/concurrent-modification");
        problem.GetProperty("detail").GetString()!.ShouldContain("Reload");
    }
}

/// <summary>Boots the application as it runs outside Development.</summary>
/// <remarks>
/// Production does not load appsettings.Development.json, so the connection
/// string and the signing key are supplied here — otherwise the host refuses to
/// start, which is the fail-fast behaviour P3 added and not a fault to work
/// around.
/// </remarks>
internal sealed class ProductionApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:Default", postgres.ApiConnectionString);
        builder.UseSetting("Jwt:Issuer", "mediqueue-test");
        builder.UseSetting("Jwt:Audience", "mediqueue-test");
        builder.UseSetting("Jwt:SigningKey", new string('k', 64));
    }
}
