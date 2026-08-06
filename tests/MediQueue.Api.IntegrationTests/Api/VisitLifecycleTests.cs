using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Contracts.Authentication;
using MediQueue.Contracts.Directory;
using MediQueue.Contracts.Visits;
using MediQueue.Infrastructure.Persistence;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// The visit lifecycle, driven through real HTTP against a real database.
/// </summary>
/// <remarks>
/// Every test creates its own patients through the API rather than mutating
/// seeded rows, so nothing here depends on seed state or on the order the tests
/// happen to run in.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class VisitLifecycleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private MediQueueApiFactory _factory = null!;

    /// <summary>Signed in as an assistant.</summary>
    private HttpClient _assistant = null!;

    /// <summary>The two doctors who share Belgyógyászat.</summary>
    private HttpClient _kovacs = null!;
    private HttpClient _nagy = null!;
    private Guid _kovacsId;
    private Guid _nagyId;

    private IReadOnlyList<SpecialtyDto> _specialties = null!;

    public async Task InitializeAsync()
    {
        _factory = new MediQueueApiFactory(postgres);
        await _factory.CreateReadyClientAsync();

        _assistant = await SignInAsync("horvath.anna");
        (_kovacs, _kovacsId) = await SignInWithIdAsync("kovacs.istvan");
        (_nagy, _nagyId) = await SignInWithIdAsync("nagy.peter");

        _specialties = (await _assistant.GetFromJsonAsync<List<SpecialtyDto>>("/api/specialties"))!;
    }

    public async Task DisposeAsync()
    {
        _assistant.Dispose();
        _kovacs.Dispose();
        _nagy.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> SignInWithIdAsync(string username)
    {
        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(username, DatabaseSeeder.DemoPassword));
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);

        return (client, body.User.Id);
    }

    private async Task<HttpClient> SignInAsync(string username) => (await SignInWithIdAsync(username)).Client;

    private Guid SpecialtyNamed(string name) => _specialties.Single(specialty => specialty.Name == name).Id;

    // PatientName rejects digits, so a GUID is not usable as an isolation token.
    // Letters drawn from the guid give a unique, valid Hungarian-looking name.
    private static string AUniqueName()
    {
        var letters = Guid.NewGuid().ToString("N")
            .Select(character => (char)('a' + ((character + 7) % 23)))
            .Take(8)
            .ToArray();

        return "Teszt " + char.ToUpperInvariant(letters[0]) + new string(letters[1..]);
    }

    private static int _tajCounter = 100_000_000;

    private static string AUniqueTaj()
    {
        var digits = Interlocked.Increment(ref _tajCounter).ToString();

        return $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";
    }

    private async Task<VisitSummaryDto> RegisterAsync(Guid? specialtyId = null, HttpClient? client = null)
    {
        var response = await (client ?? _assistant).PostAsJsonAsync(
            "/api/visits",
            new RegisterVisitRequest(AUniqueName(), "1052 Budapest, Váci utca 12.", AUniqueTaj(), "Fejfájás", specialtyId));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<VisitSummaryDto>())!;
    }

    private HttpClient DoctorOwning(VisitSummaryDto visit) => visit.DoctorId == _kovacsId ? _kovacs : _nagy;

    [Fact]
    public async Task Registering_returns_201_with_a_location_that_resolves()
    {
        var response = await _assistant.PostAsJsonAsync(
            "/api/visits",
            new RegisterVisitRequest(AUniqueName(), "Budapest", AUniqueTaj(), "Fejfájás", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        (await _assistant.GetAsync(response.Headers.Location)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_whole_lifecycle_runs_end_to_end()
    {
        var registered = await RegisterAsync();
        registered.Status.ShouldBe(VisitStatus.Registered);
        registered.QueuedAt.ShouldBeNull();

        var assigned = await AssignAsync(registered.Id, SpecialtyNamed("Belgyógyászat"));
        assigned.Status.ShouldBe(VisitStatus.Waiting);
        assigned.QueuedAt.ShouldNotBeNull();
        assigned.DoctorId.ShouldNotBeNull();

        var doctor = DoctorOwning(assigned);

        var calledIn = await PostAsync<VisitDetailDto>(doctor, $"/api/visits/{assigned.Id}/call-in");
        calledIn.Status.ShouldBe(VisitStatus.InTreatment);
        calledIn.CalledInAt.ShouldNotBeNull();

        var diagnosed = await PutAsync<VisitDetailDto>(
            doctor, $"/api/visits/{assigned.Id}/diagnosis", new RecordDiagnosisRequest("Migrén"));
        diagnosed.Diagnosis.ShouldBe("Migrén");

        var released = await PostAsync<VisitDetailDto>(doctor, $"/api/visits/{assigned.Id}/release");
        released.Status.ShouldBe(VisitStatus.Done);
        released.CompletedAt.ShouldNotBeNull();

        // Each timestamp set once, and in order.
        new[] { released.RegisteredAt, released.QueuedAt!.Value, released.CalledInAt!.Value, released.CompletedAt!.Value }
            .ShouldBeInOrder();
    }

    [Fact]
    public async Task Registering_with_a_specialty_reaches_the_queue_in_one_call()
    {
        var visit = await RegisterAsync(SpecialtyNamed("Belgyógyászat"));

        visit.Status.ShouldBe(VisitStatus.Waiting);
        visit.DoctorId.ShouldNotBeNull();
        visit.SpecialtyName.ShouldBe("Belgyógyászat");
        visit.QueuedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_specialty_with_no_active_doctor_is_refused_and_the_visit_stays_registered()
    {
        var response = await _assistant.PostAsJsonAsync(
            "/api/visits",
            new RegisterVisitRequest(AUniqueName(), "Budapest", AUniqueTaj(), "Ízületi fájdalom",
                SpecialtyNamed("Reumatológia")));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString()!.ShouldContain("Reumatológia");

        // Registering the same person again, unrouted, must succeed — which
        // proves the refused attempt committed nothing at all.
        var retry = await _assistant.PostAsJsonAsync(
            "/api/visits",
            new RegisterVisitRequest(AUniqueName(), "Budapest", AUniqueTaj(), "Ízületi fájdalom", null));

        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await retry.Content.ReadFromJsonAsync<VisitSummaryDto>())!.Status.ShouldBe(VisitStatus.Registered);
    }

    [Fact]
    public async Task An_invalid_transition_comes_back_as_409_naming_the_alternatives()
    {
        // Production coverage for the mapping the P3 test-only endpoint proved
        // in isolation: this one goes through a real endpoint and a real rule.
        var visit = await RegisterAsync(SpecialtyNamed("Belgyógyászat"));
        var doctor = DoctorOwning(visit);

        var response = await doctor.PostAsync($"/api/visits/{visit.Id}/release", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("currentStatus").GetString().ShouldBe("Waiting");
        problem.GetProperty("attemptedStatus").GetString().ShouldBe("Done");
        problem.GetProperty("allowedTransitions").EnumerateArray()
            .Select(value => value.GetString()).ShouldBe(["InTreatment"]);
    }

    [Theory]
    [InlineData("12-123-123", "Kis Elemér", "TajNumber")]
    [InlineData("123-456-788", "Kis Elemér2", "PatientName")]
    public async Task Malformed_input_is_400_naming_the_field(string taj, string name, string field)
    {
        var response = await _assistant.PostAsJsonAsync(
            "/api/visits", new RegisterVisitRequest(name, "Budapest", taj, "Fejfájás", null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_patient_cannot_be_in_two_queues_at_once()
    {
        var taj = AUniqueTaj();
        var name = AUniqueName();

        var first = await _assistant.PostAsJsonAsync(
            "/api/visits", new RegisterVisitRequest(name, "Budapest", taj, "Fejfájás", null));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await _assistant.PostAsJsonAsync(
            "/api/visits", new RegisterVisitRequest(name, "Budapest", taj, "Szédülés", null));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deleting_makes_the_visit_invisible_and_a_second_delete_is_404()
    {
        var visit = await RegisterAsync();

        (await _assistant.DeleteAsync($"/api/visits/{visit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The query filter, not a special case anybody wrote.
        (await _assistant.GetAsync($"/api/visits/{visit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _assistant.DeleteAsync($"/api/visits/{visit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_queue_is_returned_in_the_order_of_the_timestamp_it_displays()
    {
        var specialty = SpecialtyNamed("Szemészet");

        // One doctor in this specialty, so all three land in the same queue.
        var first = await RegisterAsync(specialty);
        var second = await RegisterAsync(specialty);
        var third = await RegisterAsync(specialty);

        var queue = await _assistant.GetFromJsonAsync<List<VisitSummaryDto>>($"/api/queues/{first.DoctorId}");

        queue.ShouldNotBeNull();
        var mine = queue.Where(visit => new[] { first.Id, second.Id, third.Id }.Contains(visit.Id)).ToList();

        mine.Select(visit => visit.Id).ShouldBe([first.Id, second.Id, third.Id]);
        mine.Select(visit => visit.QueuedAt).ShouldBeInOrder();
    }

    [Fact]
    public async Task Consecutive_registrations_alternate_between_the_two_shared_doctors()
    {
        // Kovács and Nagy both practise Belgyógyászat, so the shortest-queue
        // strategy must spread the load rather than piling onto whoever sorts
        // first.
        var specialty = SpecialtyNamed("Belgyógyászat");

        var assigned = new List<Guid?>();
        for (var index = 0; index < 4; index++)
        {
            assigned.Add((await RegisterAsync(specialty)).DoctorId);
        }

        assigned.Distinct().Count().ShouldBe(2, "both doctors should receive patients");
        assigned.Count(doctorId => doctorId == _kovacsId).ShouldBe(2);
        assigned.Count(doctorId => doctorId == _nagyId).ShouldBe(2);
    }

    private async Task<VisitSummaryDto> AssignAsync(Guid visitId, Guid specialtyId)
    {
        var response = await _assistant.PostAsJsonAsync(
            $"/api/visits/{visitId}/assign", new AssignSpecialtyRequest(specialtyId));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<VisitSummaryDto>())!;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url)
    {
        var response = await client.PostAsync(url, null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync() ?? string.Empty);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<T> PutAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PutAsJsonAsync(url, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync() ?? string.Empty);

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
