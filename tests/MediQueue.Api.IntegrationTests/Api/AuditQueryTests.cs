using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Contracts.Auditing;
using MediQueue.Contracts.Authentication;
using MediQueue.Contracts.Directory;
using MediQueue.Contracts.Visits;
using MediQueue.Infrastructure.Persistence;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// The audit trail through real HTTP: what it records, and what it refuses to
/// show an assistant.
/// </summary>
/// <remarks>
/// Every test drives the API to produce its own history rather than reading the
/// seed, so nothing here depends on seed state — and the seeder's own writes
/// are asserted to be absent, which is a rule of its own.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class AuditQueryTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>The diagnosis every leak test hunts for in the raw bytes.</summary>
    private const string TheDiagnosis = "Migrén, feszültséges eredetű";

    private MediQueueApiFactory _factory = null!;
    private HttpClient _assistant = null!;
    private Guid _assistantId;

    /// <summary>
    /// Both doctors who share Belgyógyászat, by id.
    /// </summary>
    /// <remarks>
    /// The server chooses the doctor, not the test. Assuming which one gets the
    /// next arrival makes the test depend on the assignment strategy and on how
    /// many patients the seed already queued — so the visit is treated by
    /// whoever it was actually given to.
    /// </remarks>
    private readonly Dictionary<Guid, HttpClient> _doctors = [];

    /// <summary>Either doctor, for the reads that do not care which.</summary>
    private HttpClient _doctor = null!;

    private IReadOnlyList<SpecialtyDto> _specialties = null!;

    public async Task InitializeAsync()
    {
        _factory = new MediQueueApiFactory(postgres);
        await _factory.CreateReadyClientAsync();

        (_assistant, _assistantId) = await SignInAsync("horvath.anna");

        foreach (var username in new[] { "kovacs.istvan", "nagy.peter" })
        {
            var (client, id) = await SignInAsync(username);
            _doctors[id] = client;
        }

        _doctor = _doctors.First().Value;

        _specialties = (await _assistant.GetFromJsonAsync<List<SpecialtyDto>>("/api/specialties"))!;
    }

    public async Task DisposeAsync()
    {
        _assistant.Dispose();

        foreach (var client in _doctors.Values)
        {
            client.Dispose();
        }

        await _factory.DisposeAsync();
    }

    /// <summary>The client for whichever doctor the server routed this visit to.</summary>
    private HttpClient TreatingDoctorOf(VisitSummaryDto visit) => _doctors[visit.DoctorId!.Value];

    private async Task<(HttpClient Client, Guid UserId)> SignInAsync(string username)
    {
        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(username, DatabaseSeeder.DemoPassword));
        var body = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);

        return (client, body.User.Id);
    }

    private Guid InternalMedicine => _specialties.Single(specialty => specialty.Name == "Belgyógyászat").Id;

    // Its own range, like every other HTTP test class: the shared database
    // accumulates rows and TAJ is unique across the practice. 100/300/500
    // million are taken by the lifecycle, unrouted and security suites — a
    // collision reads as "this patient already has a visit in progress", which
    // looks like a rule firing rather than the fixture clash it is.
    private static int _tajCounter = 700_000_000;

    private static string AUniqueTaj()
    {
        var digits = Interlocked.Increment(ref _tajCounter).ToString();

        return $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";
    }

    /// <summary>Registers a patient and routes them straight into a queue.</summary>
    private async Task<VisitSummaryDto> RegisterAsync(string name = "Kovács Anna")
    {
        var response = await _assistant.PostAsJsonAsync("/api/visits", new RegisterVisitRequest(
            name, "1052 Budapest, Váci utca 12.", AUniqueTaj(), "Fejfájás és szédülés", InternalMedicine));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<VisitSummaryDto>())!;
    }

    /// <summary>Takes a visit all the way to a recorded diagnosis.</summary>
    private async Task<VisitSummaryDto> DiagnoseAsync()
    {
        var visit = await RegisterAsync();
        var doctor = TreatingDoctorOf(visit);

        // Called in by id rather than by taking the head of the queue: the
        // seeded queue may already hold patients.
        (await doctor.PostAsync($"/api/visits/{visit.Id}/call-in", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await doctor.PutAsJsonAsync($"/api/visits/{visit.Id}/diagnosis", new RecordDiagnosisRequest(TheDiagnosis)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        return visit;
    }

    private async Task<AuditPageDto> AuditAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/audit{query}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<AuditPageDto>())!;
    }

    /// <summary>The response exactly as it went over the wire.</summary>
    /// <remarks>
    /// Every leak assertion in this file reads this rather than a deserialised
    /// object. Deserialising into <c>AuditFieldChangeDto</c> would discard or
    /// default the very field that leaked and pass happily against a broken
    /// server — the P4 lesson, applied to the one rule that most needs it.
    /// </remarks>
    private static async Task<string> RawAsync(HttpClient client, string query) =>
        await client.GetStringAsync($"/api/audit{query}");

    [Fact]
    public async Task Registering_a_patient_records_the_patient_and_the_visit_against_the_assistant()
    {
        var visit = await RegisterAsync();

        var page = await AuditAsync(_assistant, $"?patientId={visit.PatientId}");

        var types = page.Items.Select(entry => entry.EntityType).ToList();
        types.ShouldContain("Patient");
        types.ShouldContain("Visit");

        // The actor is the acting assistant, on both — which is only true
        // because D-37 keeps sub readable.
        page.Items.ShouldAllBe(entry => entry.UserId == _assistantId);
        page.Items.ShouldAllBe(entry => entry.PatientId == visit.PatientId);
    }

    [Fact]
    public async Task Recording_a_diagnosis_produces_one_entry_with_a_sensitive_change()
    {
        var visit = await DiagnoseAsync();

        var page = await AuditAsync(_doctor, $"?patientId={visit.PatientId}");

        var change = page.Items
            .SelectMany(entry => entry.Changes)
            .Single(candidate => candidate.FieldName == "Diagnosis");

        change.NewValue.ShouldBe(TheDiagnosis);
        change.Redacted.ShouldBeFalse();
    }

    [Fact]
    public async Task An_assistants_raw_json_never_contains_the_diagnosis()
    {
        // The most important assertion in the phase, and the reason it reads the
        // bytes: this is the one guarantee in the system enforced by a branch
        // rather than by a type.
        var visit = await DiagnoseAsync();

        var raw = await RawAsync(_assistant, $"?patientId={visit.PatientId}");

        raw.ShouldNotContain(TheDiagnosis);
        raw.ShouldContain("Diagnosis", Case.Sensitive);
        raw.ShouldContain("***");
        raw.ShouldContain("\"redacted\":true");
    }

    [Fact]
    public async Task A_doctors_raw_json_does_contain_the_diagnosis()
    {
        // The other half. Without this, redacting everything for everyone would
        // pass the test above.
        var visit = await DiagnoseAsync();

        var raw = await RawAsync(_doctor, $"?patientId={visit.PatientId}");

        raw.ShouldContain(TheDiagnosis);
        raw.ShouldContain("\"redacted\":false");
    }

    [Fact]
    public async Task No_page_of_a_multi_page_response_leaks_the_diagnosis_to_an_assistant()
    {
        // A leak on page three is still a leak. Paging is exactly the sort of
        // path where a projection gets applied in one branch and not the other.
        var visit = await DiagnoseAsync();

        var first = await AuditAsync(_assistant, "?pageSize=1");
        first.TotalCount.ShouldBeGreaterThan(3);

        var pages = (int)Math.Ceiling(first.TotalCount / 1.0);

        for (var page = 1; page <= pages; page++)
        {
            var raw = await RawAsync(_assistant, $"?page={page}&pageSize=1");

            raw.ShouldNotContain(TheDiagnosis, Case.Sensitive, $"page {page} of {pages} leaked the diagnosis");
        }

        // And the sweep really did visit the page holding it, rather than
        // passing because the diagnosis was on no page at all.
        var everything = await RawAsync(_doctor, $"?patientId={visit.PatientId}&pageSize=200");
        everything.ShouldContain(TheDiagnosis);
    }

    [Fact]
    public async Task Filtering_by_patient_returns_that_patients_entries_and_no_others()
    {
        var mine = await RegisterAsync("Nagy Piroska");
        var theirs = await RegisterAsync("Kovács Anna");

        mine.PatientId.ShouldNotBe(theirs.PatientId);

        var page = await AuditAsync(_assistant, $"?patientId={mine.PatientId}");

        page.Items.ShouldNotBeEmpty();
        page.Items.ShouldAllBe(entry => entry.PatientId == mine.PatientId);
    }

    [Fact]
    public async Task Filtering_by_user_returns_that_users_entries_and_no_others()
    {
        var visit = await DiagnoseAsync();

        // Whoever the server routed the visit to, not whoever the test guessed:
        // the other doctor may have touched nothing at all in this run.
        var treatingDoctorId = visit.DoctorId!.Value;

        var page = await AuditAsync(_doctor, $"?userId={treatingDoctorId}");

        page.Items.ShouldNotBeEmpty();
        page.Items.ShouldAllBe(entry => entry.UserId == treatingDoctorId);

        // The assistant registered this patient moments earlier, so the filter
        // is excluding something rather than matching everything.
        var byAssistant = await AuditAsync(_doctor, $"?userId={_assistantId}");

        byAssistant.Items.ShouldNotBeEmpty();
        byAssistant.Items.ShouldAllBe(entry => entry.UserId == _assistantId);
        byAssistant.Items.ShouldContain(entry => entry.PatientId == visit.PatientId);
    }

    [Fact]
    public async Task A_soft_delete_is_recorded_as_a_deletion_and_its_history_survives()
    {
        // The history most worth having. Without IgnoreQueryFilters the global
        // soft-delete filter would hide precisely the record whose removal was
        // worth recording.
        var visit = await RegisterAsync();

        (await _assistant.DeleteAsync($"/api/visits/{visit.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Gone from the API, as D-30 requires.
        (await _assistant.GetAsync($"/api/visits/{visit.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var page = await AuditAsync(_assistant, $"?patientId={visit.PatientId}");

        page.Items.ShouldContain(entry =>
            entry.EntityId == visit.Id && entry.Action == AuditAction.Delete);

        // And the whole life of the visit is still legible, not only its end.
        page.Items.ShouldContain(entry =>
            entry.EntityId == visit.Id && entry.Action == AuditAction.Create);
    }

    [Fact]
    public async Task An_update_that_changed_two_properties_is_one_entry_with_several_changes()
    {
        var visit = await RegisterAsync();

        (await _doctor.PostAsync($"/api/visits/{visit.Id}/call-in", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var page = await AuditAsync(_doctor, $"?patientId={visit.PatientId}");

        var callIn = page.Items.Single(entry =>
            entry.EntityId == visit.Id && entry.Action == AuditAction.Update);

        // Status and CalledInAt moved together, in one action, so one entry.
        callIn.Changes.Count.ShouldBe(2);
        callIn.Changes.Select(change => change.FieldName)
            .ShouldBe(["Status", "CalledInAt"], ignoreOrder: true);
    }

    [Fact]
    public async Task Value_objects_appear_in_their_canonical_form_rather_than_as_a_type_name()
    {
        var visit = await RegisterAsync("Szabó Erzsébet");

        var page = await AuditAsync(_assistant, $"?patientId={visit.PatientId}");

        var patient = page.Items.First(entry => entry.EntityType == "Patient");

        // The column stores nine bare digits; the log must still read the way
        // the domain spells it, because the values come from the PropertyEntry
        // and so are model values rather than column values.
        var taj = patient.Changes.Single(change => change.FieldName == "Taj").NewValue;

        taj.ShouldBe(visit.Taj);
        taj.ShouldNotBeNull().ShouldMatch(@"\A[0-9]{3}-[0-9]{3}-[0-9]{3}\z");

        patient.Changes.Single(change => change.FieldName == "FullName").NewValue.ShouldBe("Szabó Erzsébet");
    }

    [Fact]
    public async Task Entries_come_back_newest_first()
    {
        await RegisterAsync();

        var page = await AuditAsync(_assistant, "?pageSize=200");

        page.Items.Select(entry => entry.OccurredAt).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public async Task A_page_size_above_the_maximum_is_clamped_rather_than_refused()
    {
        var page = await AuditAsync(_assistant, "?pageSize=100000");

        page.PageSize.ShouldBe(200);
        page.Items.Count.ShouldBeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task Both_roles_may_read_the_log_and_an_anonymous_caller_may_not()
    {
        (await _assistant.GetAsync("/api/audit")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _doctor.GetAsync("/api/audit")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync("/api/audit")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_seeder_produced_no_audit_entries_at_all()
    {
        // Seed rows are fixture, not history. An audit trail whose first two
        // dozen entries have no actor teaches a reader that "no actor" is
        // normal, and it must never be normal.
        //
        // Asserted on a fresh application whose only writes are the seeder's,
        // so nothing this test class did can mask the answer.
        await using var factory = new MediQueueApiFactory(postgres)
            .WithOwnDatabase(await postgres.CreateEmptyDatabaseAsync());

        using var client = await factory.CreateReadyClientAsync();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("horvath.anna", DatabaseSeeder.DemoPassword));
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var page = await client.GetFromJsonAsync<AuditPageDto>("/api/audit");

        page.ShouldNotBeNull();
        page.TotalCount.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_entry_the_log_returns_can_be_read_as_the_declared_contract()
    {
        // D-42: a query that only compiles fails in front of the user as an
        // opaque 500. This one executes the real query, against the real
        // database, and reads every field the contract declares.
        await DiagnoseAsync();

        var raw = await RawAsync(_assistant, "?pageSize=200");

        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;

        root.GetProperty("totalCount").GetInt32().ShouldBeGreaterThan(0);
        root.GetProperty("page").GetInt32().ShouldBe(1);
        root.GetProperty("pageSize").GetInt32().ShouldBe(200);

        foreach (var entry in root.GetProperty("items").EnumerateArray())
        {
            entry.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
            entry.GetProperty("entityId").GetGuid().ShouldNotBe(Guid.Empty);
            entry.GetProperty("entityType").GetString().ShouldNotBeNullOrWhiteSpace();
            entry.GetProperty("occurredAt").GetDateTimeOffset();

            // Never empty: an entry with no changes is not written at all.
            entry.GetProperty("changes").GetArrayLength().ShouldBeGreaterThan(0);
        }
    }
}
