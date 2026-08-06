using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Contracts.Authentication;
using MediQueue.Contracts.Directory;
using MediQueue.Infrastructure.Persistence;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>The read-only endpoints the clients need before they can do anything.</summary>
[Collection(PostgresCollection.Name)]
public class DirectoryEndpointTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task The_seeded_specialties_come_back_in_name_order()
    {
        var specialties = await _client.GetFromJsonAsync<List<SpecialtyDto>>("/api/specialties");

        specialties.ShouldNotBeNull();
        specialties.Select(specialty => specialty.Name)
            .ShouldBe(["Belgyógyászat", "Bőrgyógyászat", "Reumatológia", "Szemészet"]);
    }

    [Fact]
    public async Task Every_active_doctor_comes_back_with_their_specialty_name()
    {
        var doctors = await _client.GetFromJsonAsync<List<DoctorDto>>("/api/doctors");

        doctors.ShouldNotBeNull();

        // Five doctors are seeded and one is deactivated. Before the inactive
        // one existed this count was 4 for no reason — every seeded doctor was
        // active, so deleting the IsActive filter failed nothing.
        doctors.Count.ShouldBe(4);
        doctors.ShouldNotContain(doctor => doctor.FullName == "Dr. Farkas Judit");

        // The specialty name travels with the doctor, so a client rendering the
        // list needs no second call and no join of its own.
        doctors.ShouldAllBe(doctor => !string.IsNullOrWhiteSpace(doctor.SpecialtyName));
        doctors.Select(doctor => doctor.FullName).ShouldBeInOrder();
    }

    [Fact]
    public async Task Filtering_by_specialty_returns_only_that_specialty()
    {
        var specialties = await _client.GetFromJsonAsync<List<SpecialtyDto>>("/api/specialties");
        var internalMedicine = specialties!.Single(specialty => specialty.Name == "Belgyógyászat");

        var doctors = await _client.GetFromJsonAsync<List<DoctorDto>>($"/api/doctors?specialtyId={internalMedicine.Id}");

        doctors.ShouldNotBeNull();
        // Two doctors share internal medicine, which is what gives the assignment
        // strategy something to choose between.
        doctors.Count.ShouldBe(2);
        doctors.ShouldAllBe(doctor => doctor.SpecialtyId == internalMedicine.Id);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user()
    {
        var me = await _client.GetFromJsonAsync<UserDto>("/api/me");

        me.ShouldNotBeNull();
        me.Username.ShouldBe("horvath.anna");
        me.Role.ShouldBe(MediQueue.Contracts.UserRole.Assistant);
        me.SpecialtyId.ShouldBeNull();
    }

    [Fact]
    public async Task The_openapi_document_declares_the_bearer_scheme()
    {
        // Without this the reference UI has no Authorize control, and every
        // protected endpoint is untestable from the browser.
        var document = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var bearer = document
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");

        bearer.GetProperty("type").GetString().ShouldBe("http");
        bearer.GetProperty("scheme").GetString().ShouldBe("bearer");
        bearer.GetProperty("bearerFormat").GetString().ShouldBe("JWT");
    }
}
