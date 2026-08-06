using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Contracts.Authentication;
using MediQueue.Contracts.Directory;
using MediQueue.Contracts.Visits;
using MediQueue.Infrastructure.Persistence;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// The listing that makes <c>Registered</c> observable.
/// </summary>
/// <remarks>
/// Every other listing groups by doctor, and a registered visit has none — so
/// before this endpoint a patient registered without a specialty was in no list
/// at all, reachable only by an identifier nobody had seen.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class UnassignedVisitsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private MediQueueApiFactory _factory = null!;
    private HttpClient _assistant = null!;
    private HttpClient _doctor = null!;
    private IReadOnlyList<SpecialtyDto> _specialties = null!;

    public async Task InitializeAsync()
    {
        _factory = new MediQueueApiFactory(postgres);
        await _factory.CreateReadyClientAsync();

        _assistant = await SignInAsync("horvath.anna");
        _doctor = await SignInAsync("kovacs.istvan");
        _specialties = (await _assistant.GetFromJsonAsync<List<SpecialtyDto>>("/api/specialties"))!;
    }

    public async Task DisposeAsync()
    {
        _assistant.Dispose();
        _doctor.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<HttpClient> SignInAsync(string username)
    {
        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(username, DatabaseSeeder.DemoPassword));
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);

        return client;
    }

    private static int _tajCounter = 300_000_000;

    private static string AUniqueTaj()
    {
        var digits = Interlocked.Increment(ref _tajCounter).ToString();

        return $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";
    }

    private static string AUniqueName()
    {
        var letters = Guid.NewGuid().ToString("N")
            .Select(character => (char)('a' + ((character + 3) % 23)))
            .Take(8)
            .ToArray();

        return "Varakozo " + char.ToUpperInvariant(letters[0]) + new string(letters[1..]);
    }

    private async Task<VisitSummaryDto> RegisterAsync(Guid? specialtyId)
    {
        var response = await _assistant.PostAsJsonAsync(
            "/api/visits",
            new RegisterVisitRequest(AUniqueName(), "Budapest", AUniqueTaj(), "Fejfájás", specialtyId));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<VisitSummaryDto>())!;
    }

    private async Task<List<VisitSummaryDto>> UnassignedAsync() =>
        (await _assistant.GetFromJsonAsync<List<VisitSummaryDto>>("/api/visits/unassigned"))!;

    [Fact]
    public async Task The_literal_segment_reaches_its_own_action()
    {
        var response = await _assistant.GetAsync("/api/visits/unassigned");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<VisitSummaryDto>>()).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_identifier_that_is_not_a_guid_is_not_found_rather_than_malformed()
    {
        // This is what the {id:guid} constraint actually does, and it is worth
        // stating because it is not what one might assume. The literal route
        // above wins on precedence with or without the constraint — measured.
        // The constraint decides how an unparseable id is answered: 404 with it,
        // 400 without. A visit id that is not a GUID names no resource, so "not
        // found" is the truthful reply.
        var response = await _assistant.GetAsync("/api/visits/not-a-guid");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_visit_registered_without_a_specialty_appears()
    {
        var visit = await RegisterAsync(specialtyId: null);

        var unassigned = await UnassignedAsync();

        unassigned.ShouldContain(candidate => candidate.Id == visit.Id);
        unassigned.ShouldAllBe(candidate => candidate.Status == VisitStatus.Registered);
        unassigned.ShouldAllBe(candidate => candidate.DoctorId == null);
    }

    [Fact]
    public async Task A_visit_that_has_been_routed_does_not_appear()
    {
        var visit = await RegisterAsync(_specialties.Single(s => s.Name == "Szemészet").Id);

        visit.Status.ShouldBe(VisitStatus.Waiting);
        (await UnassignedAsync()).ShouldNotContain(candidate => candidate.Id == visit.Id);
    }

    [Fact]
    public async Task Routing_a_visit_removes_it_from_the_list()
    {
        var visit = await RegisterAsync(specialtyId: null);
        (await UnassignedAsync()).ShouldContain(candidate => candidate.Id == visit.Id);

        await _assistant.PostAsJsonAsync(
            $"/api/visits/{visit.Id}/assign",
            new AssignSpecialtyRequest(_specialties.Single(s => s.Name == "Szemészet").Id));

        (await UnassignedAsync()).ShouldNotContain(candidate => candidate.Id == visit.Id);
    }

    [Fact]
    public async Task A_soft_deleted_visit_does_not_appear()
    {
        var visit = await RegisterAsync(specialtyId: null);

        await _assistant.DeleteAsync($"/api/visits/{visit.Id}");

        (await UnassignedAsync()).ShouldNotContain(candidate => candidate.Id == visit.Id);
    }

    [Fact]
    public async Task The_list_is_in_arrival_order()
    {
        var first = await RegisterAsync(specialtyId: null);
        var second = await RegisterAsync(specialtyId: null);

        var mine = (await UnassignedAsync())
            .Where(candidate => candidate.Id == first.Id || candidate.Id == second.Id)
            .ToList();

        mine.Select(candidate => candidate.Id).ShouldBe([first.Id, second.Id]);
        mine.Select(candidate => candidate.RegisteredAt).ShouldBeInOrder();
    }

    [Fact]
    public async Task A_doctor_may_not_read_it()
    {
        (await _doctor.GetAsync("/api/visits/unassigned")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        (await _factory.CreateClient().GetAsync("/api/visits/unassigned")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_names_are_present_on_every_row()
    {
        // The batched lookup must actually resolve. A dictionary that silently
        // yields nothing would render blank names rather than fail, which is the
        // worse of the two outcomes.
        await RegisterAsync(specialtyId: null);

        var unassigned = await UnassignedAsync();

        unassigned.ShouldNotBeEmpty();
        unassigned.ShouldAllBe(visit => !string.IsNullOrWhiteSpace(visit.PatientFullName));
        unassigned.ShouldAllBe(visit => !string.IsNullOrWhiteSpace(visit.Taj));
    }
}
