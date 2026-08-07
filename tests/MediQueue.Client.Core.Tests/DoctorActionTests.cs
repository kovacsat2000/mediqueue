using System.Net;
using System.Text.Json;
using MediQueue.Client.Core.Api;
using MediQueue.Client.Core.ViewModels;
using MediQueue.Contracts;
using MediQueue.Contracts.Authentication;
using MediQueue.Contracts.Visits;
using Microsoft.Extensions.Time.Testing;

namespace MediQueue.Client.Core.Tests;

/// <summary>
/// The doctor acting on a patient: which visit the action names, what the
/// buttons allow, and what a refusal does to the screen.
/// </summary>
public class DoctorActionTests
{
    private static readonly DateTimeOffset EightUtc = new(2026, 8, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid TheDoctor = Guid.CreateVersion7(EightUtc);

    private readonly StubHttpMessageHandler _handler = new();
    private readonly FakeQueueConnection _realtime = new();
    private readonly IAuthSession _session = new AuthSession();

    public DoctorActionTests() =>
        _session.SignIn(new LoginResponse(
            "token",
            EightUtc.AddHours(8),
            new UserDto(TheDoctor, "kovacs.istvan", "Dr. Kovács István", UserRole.Doctor, Guid.CreateVersion7(EightUtc))));

    private MediQueueApiClient Api => new(_handler.CreateClient(), _session);

    private QueueViewModel AQueue()
    {
        var clock = new FakeTimeProvider(EightUtc);
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);

        return new QueueViewModel(Api, _session, _realtime, clock);
    }

    private static VisitSummaryDto AVisit(
        string name,
        DateTimeOffset queuedAt,
        VisitStatus status = VisitStatus.Waiting) =>
        new(
            Guid.CreateVersion7(queuedAt),
            Guid.CreateVersion7(EightUtc),
            name,
            "123-456-788",
            "Fejfájás",
            Guid.CreateVersion7(EightUtc),
            "Belgyógyászat",
            TheDoctor,
            "Dr. Kovács István",
            status,
            EightUtc,
            queuedAt,
            null,
            null);

    private static VisitDetailDto ADetail(Guid visitId, string? diagnosis = null) =>
        new(
            visitId,
            Guid.CreateVersion7(EightUtc),
            "Kovács Anna",
            "123-456-788",
            "1052 Budapest, Váci utca 12.",
            "Fejfájás",
            Guid.CreateVersion7(EightUtc),
            "Belgyógyászat",
            TheDoctor,
            "Dr. Kovács István",
            VisitStatus.InTreatment,
            diagnosis,
            EightUtc,
            EightUtc,
            EightUtc.AddMinutes(10),
            null);

    private static string Json<T>(T value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Loads a queue of three, and returns them in display order.</summary>
    private async Task<QueueViewModel> AQueueOfThreeAsync()
    {
        var first = AVisit("Első Beteg", EightUtc);
        var second = AVisit("Második Beteg", EightUtc.AddMinutes(10));
        var third = AVisit("Harmadik Beteg", EightUtc.AddMinutes(20));

        _handler.Respond(HttpStatusCode.OK, Json(new[] { first, second, third }));

        var queue = AQueue();
        await queue.RefreshAsync(default);

        queue.Rows.Count.ShouldBe(3);

        return queue;
    }

    private static async Task SettleAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 200 && !until(); attempt++)
        {
            await Task.Delay(5);
        }
    }

    // ---------------------------------------------------- the selected visit

    [Fact]
    public async Task Calling_in_names_the_selected_visit_and_not_the_first_one()
    {
        // The mistake a "take the head of the queue" convenience would make, and
        // the one a doctor would only notice after calling in the wrong patient.
        var queue = await AQueueOfThreeAsync();
        var chosen = queue.Rows[2];

        queue.SelectedRow = chosen;
        _handler.Respond(HttpStatusCode.OK, Json(ADetail(chosen.VisitId)));

        await queue.CallInAsync(default);

        _handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe($"/api/visits/{chosen.VisitId}/call-in");
        _handler.LastRequest.RequestUri!.AbsolutePath.ShouldNotContain(queue.Rows[0].VisitId.ToString());
    }

    [Fact]
    public async Task Recording_a_diagnosis_names_the_selected_visit_and_sends_what_was_typed()
    {
        var queue = await AQueueOfThreeAsync();
        var chosen = queue.Rows[1];

        // In treatment, so the diagnosis box is available. The detail fetch that
        // the selection triggers is answered first.
        _handler.Respond(HttpStatusCode.OK, Json(ADetail(chosen.VisitId)));
        queue.SelectedRow = chosen with { Status = VisitStatus.InTreatment };
        await SettleAsync(() => queue.SelectedDetail is not null);

        queue.DiagnosisText = "Migrén, feszültséges eredetű";
        _handler.Respond(HttpStatusCode.OK, Json(ADetail(chosen.VisitId, "Migrén, feszültséges eredetű")));

        await queue.RecordDiagnosisAsync(default);

        _handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe($"/api/visits/{chosen.VisitId}/diagnosis");
        // Parsed rather than string-matched: the serialiser escapes non-ASCII,
        // so the bytes read "Migr\u00E9n" and the value reads "Migrén".
        using var sent = JsonDocument.Parse(_handler.LastBody);
        sent.RootElement.GetProperty("diagnosis").GetString().ShouldBe("Migrén, feszültséges eredetű");
    }

    [Fact]
    public async Task Releasing_names_the_selected_visit()
    {
        var queue = await AQueueOfThreeAsync();
        var chosen = queue.Rows[2];

        _handler.Respond(HttpStatusCode.OK, Json(ADetail(chosen.VisitId)));
        queue.SelectedRow = chosen with { Status = VisitStatus.InTreatment };
        await SettleAsync(() => queue.SelectedDetail is not null);

        _handler.Respond(HttpStatusCode.OK, Json(ADetail(chosen.VisitId)));

        await queue.ReleaseAsync(default);

        _handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe($"/api/visits/{chosen.VisitId}/release");
    }

    [Fact]
    public async Task With_nothing_selected_an_action_sends_nothing()
    {
        var queue = await AQueueOfThreeAsync();
        var before = _handler.Requests.Count;

        await queue.CallInAsync(default);

        _handler.Requests.Count.ShouldBe(before);
    }

    // ------------------------------------------------------- what is enabled

    [Fact]
    public async Task The_buttons_follow_the_status_the_server_last_reported()
    {
        var queue = await AQueueOfThreeAsync();

        queue.SelectedRow = queue.Rows[0];
        queue.CanCallIn.ShouldBeTrue("a waiting patient can be called in");
        queue.CanRelease.ShouldBeFalse("a waiting patient is not in treatment");
        queue.CanRecordDiagnosis.ShouldBeFalse();

        _handler.Respond(HttpStatusCode.OK, Json(ADetail(queue.Rows[0].VisitId)));
        queue.SelectedRow = queue.Rows[0] with { Status = VisitStatus.InTreatment };

        queue.CanCallIn.ShouldBeFalse("a patient already in treatment cannot be called in again");
        queue.CanRelease.ShouldBeTrue();

        queue.DiagnosisText = "Migrén";
        queue.CanRecordDiagnosis.ShouldBeTrue();
    }

    [Fact]
    public async Task A_diagnosis_cannot_be_recorded_while_the_box_is_empty()
    {
        var queue = await AQueueOfThreeAsync();

        _handler.Respond(HttpStatusCode.OK, Json(ADetail(queue.Rows[0].VisitId)));
        queue.SelectedRow = queue.Rows[0] with { Status = VisitStatus.InTreatment };

        queue.DiagnosisText = string.Empty;
        queue.CanRecordDiagnosis.ShouldBeFalse();

        queue.DiagnosisText = "   ";
        queue.CanRecordDiagnosis.ShouldBeFalse("whitespace is not a finding");
    }

    // -------------------------------------------------------- what a refusal does

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "This visit is not in your queue.")]
    [InlineData(HttpStatusCode.Conflict, "A diagnosis can only be recorded while the visit is 'InTreatment'.")]
    public async Task A_refusal_shows_the_servers_sentence_and_leaves_the_list_alone(
        HttpStatusCode status,
        string detail)
    {
        var queue = await AQueueOfThreeAsync();
        var before = queue.Rows.Select(row => row.VisitId).ToList();

        queue.SelectedRow = queue.Rows[0];

        _handler.Respond(
            status,
            $$"""{"title":"Refused","status":{{(int)status}},"detail":"{{detail}}"}""",
            "application/problem+json");

        await queue.CallInAsync(default);

        queue.ActionError.ShouldNotBeNull().ShouldContain(detail);

        // Nothing optimistic ever happened, so there is nothing to have rolled
        // back — the list is unchanged by construction.
        queue.Rows.Select(row => row.VisitId).ShouldBe(before);
        queue.Rows[0].Status.ShouldBe(VisitStatus.Waiting);
    }

    [Fact]
    public async Task A_refusal_carries_the_trace_id_when_the_server_sent_one()
    {
        var queue = await AQueueOfThreeAsync();
        queue.SelectedRow = queue.Rows[0];

        _handler.Respond(
            HttpStatusCode.Forbidden,
            """{"title":"Refused","status":403,"detail":"This visit is not in your queue.","traceId":"00-abc-def-00"}""",
            "application/problem+json");

        await queue.CallInAsync(default);

        queue.ActionError.ShouldNotBeNull().ShouldContain("00-abc-def-00");
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_rather_than_thrown()
    {
        var queue = await AQueueOfThreeAsync();
        queue.SelectedRow = queue.Rows[0];

        _handler.FailTransportWith(new HttpRequestException("Connection refused"));

        await Should.NotThrowAsync(() => queue.CallInAsync(default));

        queue.ActionError.ShouldNotBeNull().ShouldContain("not reachable");
    }

    // ----------------------------------------------------------- the detail

    [Fact]
    public async Task Selecting_a_patient_in_treatment_loads_their_detail()
    {
        // The only place this client asks for VisitDetailDto.
        var queue = await AQueueOfThreeAsync();
        var chosen = queue.Rows[0] with { Status = VisitStatus.InTreatment };

        _handler.Respond(HttpStatusCode.OK, Json(ADetail(chosen.VisitId, "Migrén")));

        queue.SelectedRow = chosen;
        await SettleAsync(() => queue.SelectedDetail is not null);

        queue.SelectedDetail.ShouldNotBeNull().PatientAddress.ShouldBe("1052 Budapest, Váci utca 12.");
        queue.DiagnosisText.ShouldBe("Migrén", "an existing finding is shown rather than an empty box");
        _handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe($"/api/visits/{chosen.VisitId}");
    }

    [Fact]
    public async Task Selecting_a_waiting_patient_fetches_no_detail_at_all()
    {
        var queue = await AQueueOfThreeAsync();
        var before = _handler.Requests.Count;

        queue.SelectedRow = queue.Rows[0];
        await Task.Delay(50);

        queue.SelectedDetail.ShouldBeNull();
        _handler.Requests.Count.ShouldBe(before, "a waiting patient's record is not opened");
    }

    [Fact]
    public async Task A_push_that_changes_the_selected_row_moves_the_selection_with_it()
    {
        // Otherwise the doctor calls a patient in, the push says InTreatment,
        // and the buttons stay enabled for a state the visit has left.
        var queue = await AQueueOfThreeAsync();
        var chosen = queue.Rows[0];

        queue.SelectedRow = chosen;
        queue.CanCallIn.ShouldBeTrue();

        _handler.Respond(HttpStatusCode.OK, Json(ADetail(chosen.VisitId)));

        var summary = AVisit("Első Beteg", EightUtc, VisitStatus.InTreatment);
        _realtime.PushCalledIn(summary with { Id = chosen.VisitId });

        await SettleAsync(() => queue.SelectedRow?.Status == VisitStatus.InTreatment);

        queue.SelectedRow.ShouldNotBeNull().Status.ShouldBe(VisitStatus.InTreatment);
        queue.CanCallIn.ShouldBeFalse();
        queue.CanRelease.ShouldBeTrue();
    }

    [Fact]
    public async Task Releasing_the_selected_patient_clears_the_selection()
    {
        var queue = await AQueueOfThreeAsync();
        var chosen = queue.Rows[0];

        queue.SelectedRow = chosen;

        var summary = AVisit("Első Beteg", EightUtc, VisitStatus.Done);
        _realtime.PushReleased(summary with { Id = chosen.VisitId });

        await SettleAsync(() => queue.SelectedRow is null);

        queue.SelectedRow.ShouldBeNull();
        queue.CanCallIn.ShouldBeFalse();
    }
}
