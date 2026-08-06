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
/// The two rules the specification cares about most: an assistant must never
/// receive a diagnosis, and a doctor may only touch their own queue.
/// </summary>
/// <remarks>
/// The diagnosis assertions are made against the raw JSON rather than a
/// deserialised object on purpose. Deserialising into
/// <see cref="VisitSummaryDto"/> would discard a <c>diagnosis</c> key silently,
/// so a test written that way would pass against an application that leaked it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class VisitSecurityTests(PostgresFixture postgres) : IAsyncLifetime
{
    private MediQueueApiFactory _factory = null!;
    private HttpClient _assistant = null!;
    private HttpClient _kovacs = null!;
    private HttpClient _nagy = null!;
    private Guid _kovacsId;
    private IReadOnlyList<SpecialtyDto> _specialties = null!;

    /// <summary>A visit that has reached Done with a diagnosis recorded on it.</summary>
    private VisitSummaryDto _diagnosedVisit = null!;

    public async Task InitializeAsync()
    {
        _factory = new MediQueueApiFactory(postgres);
        await _factory.CreateReadyClientAsync();

        (_assistant, _) = await SignInAsync("horvath.anna");
        (_kovacs, _kovacsId) = await SignInAsync("kovacs.istvan");
        (_nagy, _) = await SignInAsync("nagy.peter");

        _specialties = (await _assistant.GetFromJsonAsync<List<SpecialtyDto>>("/api/specialties"))!;
        _diagnosedVisit = await ADiagnosedVisitAsync();
    }

    public async Task DisposeAsync()
    {
        _assistant.Dispose();
        _kovacs.Dispose();
        _nagy.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> SignInAsync(string username)
    {
        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(username, DatabaseSeeder.DemoPassword));
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);

        return (client, body.User.Id);
    }

    private static int _tajCounter = 500_000_000;

    private static string AUniqueTaj()
    {
        var digits = Interlocked.Increment(ref _tajCounter).ToString();

        return $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";
    }

    private static string AUniqueName()
    {
        var letters = Guid.NewGuid().ToString("N")
            .Select(character => (char)('a' + ((character + 11) % 23)))
            .Take(8)
            .ToArray();

        return "Beteg " + char.ToUpperInvariant(letters[0]) + new string(letters[1..]);
    }

    private async Task<VisitSummaryDto> ADiagnosedVisitAsync()
    {
        var specialty = _specialties.Single(candidate => candidate.Name == "Bőrgyógyászat").Id;

        var created = await _assistant.PostAsJsonAsync(
            "/api/visits",
            new RegisterVisitRequest(AUniqueName(), "Budapest", AUniqueTaj(), "Viszkető kiütés", specialty));
        var visit = (await created.Content.ReadFromJsonAsync<VisitSummaryDto>())!;

        var (doctor, _) = await SignInAsync("szabo.maria");
        using (doctor)
        {
            await doctor.PostAsync($"/api/visits/{visit.Id}/call-in", null);
            await doctor.PutAsJsonAsync(
                $"/api/visits/{visit.Id}/diagnosis", new RecordDiagnosisRequest("Kontakt dermatitisz"));
        }

        return visit;
    }

    private static void ShouldCarryNoDiagnosis(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    property.Name.ToLowerInvariant().ShouldNotBe("diagnosis");
                    ShouldCarryNoDiagnosis(property.Value);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ShouldCarryNoDiagnosis(item);
                }

                break;
        }
    }

    [Fact]
    public async Task An_assistant_reading_a_diagnosed_visit_receives_no_diagnosis_key_at_all()
    {
        var body = await _assistant.GetStringAsync($"/api/visits/{_diagnosedVisit.Id}");

        // Absent, not null. Asserted on the raw document, because deserialising
        // into the summary type would drop the key and pass regardless.
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("diagnosis", out _).ShouldBeFalse();
        ShouldCarryNoDiagnosis(document.RootElement);

        // The response is a real visit rather than something empty, so the
        // assertion above is about an absent key and not an absent body.
        document.RootElement.GetProperty("id").GetString().ShouldBe(_diagnosedVisit.Id.ToString());
        document.RootElement.GetProperty("complaint").GetString().ShouldBe("Viszkető kiütés");
    }

    [Fact]
    public async Task The_treating_doctor_can_see_the_diagnosis_the_assistant_cannot()
    {
        var (doctor, _) = await SignInAsync("szabo.maria");
        using var _ = doctor;

        var body = await doctor.GetStringAsync($"/api/visits/{_diagnosedVisit.Id}");

        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("diagnosis").GetString().ShouldBe("Kontakt dermatitisz");
    }

    [Fact]
    public async Task No_queue_endpoint_ever_carries_a_diagnosis()
    {
        foreach (var url in new[] { "/api/queues", $"/api/queues/{_diagnosedVisit.DoctorId}" })
        {
            using var document = JsonDocument.Parse(await _assistant.GetStringAsync(url));

            ShouldCarryNoDiagnosis(document.RootElement);
        }
    }

    [Fact]
    public async Task A_doctors_own_queue_carries_no_diagnosis_either()
    {
        // The summary type is what /api/queues/mine returns, so this holds for a
        // doctor too — the detail projection is only ever one visit at a time.
        var (doctor, _) = await SignInAsync("szabo.maria");
        using var _ = doctor;

        using var document = JsonDocument.Parse(await doctor.GetStringAsync("/api/queues/mine"));

        ShouldCarryNoDiagnosis(document.RootElement);
    }

    [Theory]
    [InlineData("call-in")]
    [InlineData("diagnosis")]
    [InlineData("release")]
    public async Task A_doctor_cannot_touch_another_doctors_visit_and_it_stays_unchanged(string action)
    {
        var specialty = _specialties.Single(candidate => candidate.Name == "Belgyógyászat").Id;
        var created = await _assistant.PostAsJsonAsync(
            "/api/visits",
            new RegisterVisitRequest(AUniqueName(), "Budapest", AUniqueTaj(), "Fejfájás", specialty));
        var visit = (await created.Content.ReadFromJsonAsync<VisitSummaryDto>())!;

        var intruder = visit.DoctorId == _kovacsId ? _nagy : _kovacs;

        var response = action switch
        {
            "diagnosis" => await intruder.PutAsJsonAsync(
                $"/api/visits/{visit.Id}/diagnosis", new RecordDiagnosisRequest("Nem az enyém")),
            _ => await intruder.PostAsync($"/api/visits/{visit.Id}/{action}", null),
        };

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A follow-up read proves the refusal happened before anything was written.
        var after = await _assistant.GetFromJsonAsync<VisitSummaryDto>($"/api/visits/{visit.Id}");
        after.ShouldNotBeNull();
        after.Status.ShouldBe(VisitStatus.Waiting);
        after.CalledInAt.ShouldBeNull();
        after.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_doctor_may_read_only_their_own_queue()
    {
        var (other, otherId) = await SignInAsync("toth.gabor");
        using var _ = other;

        (await _kovacs.GetAsync($"/api/queues/{_kovacsId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _kovacs.GetAsync($"/api/queues/{otherId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // An assistant may read anybody's.
        (await _assistant.GetAsync($"/api/queues/{otherId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_doctor_cannot_register_a_patient()
    {
        var response = await _kovacs.PostAsJsonAsync(
            "/api/visits", new RegisterVisitRequest("Kis Elemér", "Budapest", AUniqueTaj(), "Fejfájás", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_assistant_cannot_call_in_diagnose_or_release()
    {
        foreach (var response in new[]
                 {
                     await _assistant.PostAsync($"/api/visits/{_diagnosedVisit.Id}/call-in", null),
                     await _assistant.PostAsync($"/api/visits/{_diagnosedVisit.Id}/release", null),
                     await _assistant.PutAsJsonAsync(
                         $"/api/visits/{_diagnosedVisit.Id}/diagnosis", new RecordDiagnosisRequest("Nem az enyém")),
                 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task An_assistant_cannot_read_a_doctors_own_queue_endpoint()
    {
        (await _assistant.GetAsync("/api/queues/mine")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_doctor_cannot_read_the_assistant_wide_queue_list()
    {
        (await _kovacs.GetAsync("/api/queues")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_a_token_every_visit_endpoint_is_401_rather_than_403()
    {
        // The distinction matters: 403 would tell an anonymous caller that their
        // identity was considered and rejected, which it never was.
        var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync($"/api/visits/{_diagnosedVisit.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/queues")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/queues/mine")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync(
            "/api/visits", new RegisterVisitRequest("Kis Elemér", "Budapest", AUniqueTaj(), "Fejfájás", null)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"/api/visits/{_diagnosedVisit.Id}/call-in", null)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }
}
