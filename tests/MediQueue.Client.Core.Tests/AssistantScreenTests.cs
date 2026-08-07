using System.Net;
using System.Text.Json;
using MediQueue.Client.Core.Api;
using MediQueue.Client.Core.ViewModels;
using MediQueue.Contracts.Directory;
using MediQueue.Contracts.Visits;
using Microsoft.Extensions.Time.Testing;

namespace MediQueue.Client.Core.Tests;

/// <summary>
/// The assistant's screen: the registration form, the unrouted list, and the
/// doctors' queues.
/// </summary>
public class AssistantScreenTests
{
    private static readonly DateTimeOffset EightUtc = new(2026, 8, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Kovacs = Guid.CreateVersion7(EightUtc);
    private static readonly Guid Nagy = Guid.CreateVersion7(EightUtc.AddSeconds(1));
    private static readonly Guid InternalMedicine = Guid.CreateVersion7(EightUtc.AddSeconds(2));

    private readonly StubHttpMessageHandler _handler = new();
    private readonly FakeQueueConnection _realtime = new();
    private readonly AuthSession _session = new();

    private MediQueueApiClient Api => new(_handler.CreateClient(), _session);

    private static FakeTimeProvider InZone(string timeZoneId)
    {
        var clock = new FakeTimeProvider(EightUtc);
        clock.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

        return clock;
    }

    private PracticeViewModel APractice(string zone = "UTC") => new(Api, _realtime, InZone(zone));

    private static VisitSummaryDto AVisit(
        Guid? id = null,
        string name = "Kovács Anna",
        Guid? doctorId = null,
        DateTimeOffset? queuedAt = null,
        VisitStatus status = VisitStatus.Registered) =>
        new(
            id ?? Guid.CreateVersion7(queuedAt ?? EightUtc),
            Guid.CreateVersion7(EightUtc),
            name,
            "123-456-788",
            "Fejfájás",
            doctorId is null ? null : InternalMedicine,
            doctorId is null ? null : "Belgyógyászat",
            doctorId,
            doctorId is null ? null : "Dr. Kovács István",
            status,
            EightUtc,
            doctorId is null ? null : queuedAt ?? EightUtc,
            null,
            null);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>An empty unrouted list and two doctors with empty queues.</summary>
    private void RespondWithTwoEmptyQueues(params VisitSummaryDto[] unrouted)
    {
        _handler.Respond(HttpStatusCode.OK, Json(unrouted));
        _handler.Respond(HttpStatusCode.OK, Json(new[]
        {
            new QueueDto(Kovacs, "Dr. Kovács István", InternalMedicine, "Belgyógyászat", []),
            new QueueDto(Nagy, "Dr. Nagy Péter", InternalMedicine, "Belgyógyászat", []),
        }));
    }

    private static async Task SettleAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 200 && !until(); attempt++)
        {
            await Task.Delay(5);
        }
    }

    // ------------------------------------------------------------- the lists

    [Fact]
    public async Task A_registered_visit_arrives_in_the_unrouted_list()
    {
        RespondWithTwoEmptyQueues();
        var practice = APractice();
        await practice.RefreshAsync(default);

        _realtime.PushRegistered(AVisit(name: "Nagy Piroska"));
        await SettleAsync(() => practice.Unrouted.Count == 1);

        practice.Unrouted.ShouldHaveSingleItem().PatientFullName.ShouldBe("Nagy Piroska");
        practice.NobodyIsUnrouted.ShouldBeFalse();
    }

    [Fact]
    public async Task Routing_moves_a_visit_out_of_the_unrouted_list_and_into_a_doctors_queue()
    {
        // The assertion this screen exists for: a visit is in exactly one list.
        var visit = AVisit(name: "Varga László");

        RespondWithTwoEmptyQueues(visit);
        var practice = APractice();
        await practice.RefreshAsync(default);

        practice.Unrouted.Count.ShouldBe(1);

        _realtime.PushQueued(visit with
        {
            DoctorId = Kovacs,
            SpecialtyId = InternalMedicine,
            Status = VisitStatus.Waiting,
            QueuedAt = EightUtc.AddMinutes(5),
        });

        await SettleAsync(() => practice.Unrouted.Count == 0);

        practice.Unrouted.ShouldBeEmpty();
        practice.Queues.Single(queue => queue.DoctorId == Kovacs)
            .Rows.ShouldHaveSingleItem().PatientFullName.ShouldBe("Varga László");

        // And the other doctor's panel is untouched.
        practice.Queues.Single(queue => queue.DoctorId == Nagy).Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_visit_is_never_in_two_lists_at_once()
    {
        // Checked after every step rather than only at the end: the failure this
        // guards against is a visible instant in which the patient is in both.
        var visit = AVisit(name: "Varga László");

        RespondWithTwoEmptyQueues(visit);
        var practice = APractice();
        await practice.RefreshAsync(default);

        int Occurrences() =>
            practice.Unrouted.Count(row => row.VisitId == visit.Id)
            + practice.Queues.Sum(queue => queue.Rows.Count(row => row.VisitId == visit.Id));

        Occurrences().ShouldBe(1);

        _realtime.PushQueued(visit with { DoctorId = Kovacs, Status = VisitStatus.Waiting, QueuedAt = EightUtc });
        await SettleAsync(() => practice.Unrouted.Count == 0);
        Occurrences().ShouldBe(1);

        _realtime.PushCalledIn(visit with { DoctorId = Kovacs, Status = VisitStatus.InTreatment, QueuedAt = EightUtc });
        await SettleAsync(() => practice.Queues.Single(queue => queue.DoctorId == Kovacs).Rows.Count == 1);
        Occurrences().ShouldBe(1);

        _realtime.PushReleased(visit with { DoctorId = Kovacs, Status = VisitStatus.Done });
        await SettleAsync(() => Occurrences() == 0);
        Occurrences().ShouldBe(0);
    }

    [Fact]
    public async Task A_withdrawal_removes_the_row_from_whichever_list_holds_it()
    {
        var unrouted = AVisit(name: "Még Sehol");
        var queued = AVisit(name: "Már Sorban", doctorId: Kovacs, status: VisitStatus.Waiting);

        _handler.Respond(HttpStatusCode.OK, Json(new[] { unrouted }));
        _handler.Respond(HttpStatusCode.OK, Json(new[]
        {
            new QueueDto(Kovacs, "Dr. Kovács István", InternalMedicine, "Belgyógyászat", [queued]),
            new QueueDto(Nagy, "Dr. Nagy Péter", InternalMedicine, "Belgyógyászat", []),
        }));

        var practice = APractice();
        await practice.RefreshAsync(default);

        _realtime.PushDeleted(unrouted.Id, null);
        await SettleAsync(() => practice.Unrouted.Count == 0);
        practice.Unrouted.ShouldBeEmpty();

        _realtime.PushDeleted(queued.Id, Kovacs);
        await SettleAsync(() => practice.Queues.Single(queue => queue.DoctorId == Kovacs).Rows.Count == 0);
        practice.Queues.Single(queue => queue.DoctorId == Kovacs).Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_queue_says_so_rather_than_showing_nothing()
    {
        RespondWithTwoEmptyQueues();
        var practice = APractice();

        await practice.RefreshAsync(default);

        practice.Queues.Count.ShouldBe(2);
        practice.Queues.ShouldAllBe(queue => queue.IsEmpty);
        practice.NobodyIsUnrouted.ShouldBeTrue();
    }

    [Fact]
    public async Task The_empty_state_stops_being_shown_once_somebody_is_waiting()
    {
        // A computed property over an ObservableCollection is not automatic,
        // so this is a real risk rather than a hypothetical one.
        RespondWithTwoEmptyQueues();
        var practice = APractice();
        await practice.RefreshAsync(default);

        var panel = practice.Queues.Single(queue => queue.DoctorId == Kovacs);
        var raised = false;
        panel.PropertyChanged += (_, args) => raised |= args.PropertyName == nameof(panel.IsEmpty);

        _realtime.PushQueued(AVisit(doctorId: Kovacs, status: VisitStatus.Waiting));
        await SettleAsync(() => panel.Rows.Count == 1);

        panel.IsEmpty.ShouldBeFalse();
        raised.ShouldBeTrue("the panel must report that it is no longer empty");
    }

    [Theory]
    [InlineData("Europe/Budapest", "10:00")]
    [InlineData("UTC", "08:00")]
    [InlineData("America/New_York", "04:00")]
    public async Task Queued_times_are_rendered_in_the_configured_zone(string zone, string expected)
    {
        // Same rule and same single place as the doctor client. The literals
        // are asserted rather than recomputed with the production formula.
        _handler.Respond(HttpStatusCode.OK, "[]");
        _handler.Respond(HttpStatusCode.OK, Json(new[]
        {
            new QueueDto(Kovacs, "Dr. Kovács István", InternalMedicine, "Belgyógyászat",
                [AVisit(doctorId: Kovacs, queuedAt: EightUtc, status: VisitStatus.Waiting)]),
        }));

        var practice = APractice(zone);
        await practice.RefreshAsync(default);

        practice.Queues[0].Rows.ShouldHaveSingleItem().QueuedAtDisplay.ShouldBe(expected);
    }

    [Fact]
    public async Task A_push_arriving_during_a_refresh_is_applied_rather_than_dropped()
    {
        // D-65: the response is held open, so the push genuinely lands while the
        // refresh is in flight. A completed task would prove nothing.
        var arriving = AVisit(name: "Közben Érkezett");
        var gate = _handler.HoldResponses();

        RespondWithTwoEmptyQueues();

        var practice = APractice();
        var refreshing = practice.RefreshAsync(default);

        await WaitUntilAsync(() => _handler.Requests.Count >= 1);
        _realtime.PushRegistered(arriving);

        gate.SetResult();
        await refreshing;
        await SettleAsync(() => practice.Unrouted.Count == 1);

        practice.Unrouted.ShouldHaveSingleItem().VisitId.ShouldBe(arriving.Id);
    }

    [Fact]
    public async Task A_refused_routing_shows_the_servers_sentence_and_changes_nothing()
    {
        var visit = AVisit(name: "Reumatológiába Küldött");

        RespondWithTwoEmptyQueues(visit);
        var practice = APractice();
        await practice.RefreshAsync(default);

        _handler.Respond(
            HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"No doctor is currently available in Reumatológia."}""",
            "application/problem+json");

        await practice.AssignAsync(visit.Id, InternalMedicine, default);

        practice.ActionError.ShouldBe("No doctor is currently available in Reumatológia.");

        // Nothing optimistic happened, so there is nothing to have rolled back.
        practice.Unrouted.ShouldHaveSingleItem().VisitId.ShouldBe(visit.Id);
    }

    [Fact]
    public async Task A_refused_withdrawal_shows_the_servers_sentence_and_changes_nothing()
    {
        var visit = AVisit();

        RespondWithTwoEmptyQueues(visit);
        var practice = APractice();
        await practice.RefreshAsync(default);

        _handler.Respond(
            HttpStatusCode.NotFound,
            """{"title":"Not found","status":404,"detail":"Visit was not found."}""",
            "application/problem+json");

        await practice.DeleteAsync(visit.Id, default);

        practice.ActionError.ShouldBe("Visit was not found.");
        practice.Unrouted.Count.ShouldBe(1);
    }

    // ------------------------------------------------------ the registration

    [Fact]
    public async Task A_rejected_taj_puts_the_servers_message_against_the_taj_input()
    {
        _handler.Respond(
            HttpStatusCode.BadRequest,
            """
            {"title":"Validation failed","status":400,"detail":"TajNumber must be nine digits.",
             "errors":{"TajNumber":["TajNumber must be nine digits."]}}
            """,
            "application/problem+json");

        var form = new RegistrationViewModel(Api)
        {
            FullName = "Kovács Anna",
            Address = "1052 Budapest, Váci utca 12.",
            Taj = "12-3",
            Complaint = "Fejfájás",
        };

        await form.SubmitAsync(default);

        form.TajError.ShouldBe("TajNumber must be nine digits.");
        form.FullNameError.ShouldBeNull();

        // And the form keeps what was typed: retyping an address to fix one
        // digit of a TAJ is how a reception desk comes to hate a system.
        form.FullName.ShouldBe("Kovács Anna");
        form.Address.ShouldBe("1052 Budapest, Váci utca 12.");
        form.Taj.ShouldBe("12-3");
        form.Complaint.ShouldBe("Fejfájás");
    }

    [Fact]
    public async Task A_rejected_name_puts_the_message_against_the_name_input()
    {
        _handler.Respond(
            HttpStatusCode.BadRequest,
            """
            {"title":"Validation failed","status":400,"detail":"PatientName must not contain digits.",
             "errors":{"PatientName":["PatientName must not contain digits."]}}
            """,
            "application/problem+json");

        var form = new RegistrationViewModel(Api)
        {
            FullName = "Kovács Anna 2",
            Address = "Budapest",
            Taj = "123-456-788",
            Complaint = "Fejfájás",
        };

        await form.SubmitAsync(default);

        form.FullNameError.ShouldBe("PatientName must not contain digits.");
        form.TajError.ShouldBeNull();
        form.ErrorMessage.ShouldBeNull("a message that reached an input must not be repeated as a general error");
    }

    [Fact]
    public async Task A_conflict_that_names_no_field_is_shown_as_the_general_error()
    {
        _handler.Respond(
            HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"detail":"Patient 'Kovács Anna' already has a visit in progress."}""",
            "application/problem+json");

        var form = new RegistrationViewModel(Api)
        {
            FullName = "Kovács Anna",
            Address = "Budapest",
            Taj = "123-456-788",
            Complaint = "Fejfájás",
        };

        await form.SubmitAsync(default);

        form.ErrorMessage.ShouldBe("Patient 'Kovács Anna' already has a visit in progress.");
        form.TajError.ShouldBeNull();
    }

    [Fact]
    public async Task A_successful_registration_clears_the_form_and_announces_the_visit()
    {
        _handler.Respond(HttpStatusCode.Created, Json(AVisit(name: "Kovács Anna")));

        var form = new RegistrationViewModel(Api)
        {
            FullName = "Kovács Anna",
            Address = "Budapest",
            Taj = "123-456-788",
            Complaint = "Fejfájás",
        };

        VisitSummaryDto? announced = null;
        form.Registered += (_, visit) => announced = visit;

        await form.SubmitAsync(default);

        form.FullName.ShouldBeEmpty();
        form.Address.ShouldBeEmpty();
        form.Taj.ShouldBeEmpty();
        form.Complaint.ShouldBeEmpty();
        form.ErrorMessage.ShouldBeNull();

        announced.ShouldNotBeNull().PatientFullName.ShouldBe("Kovács Anna");
    }

    [Fact]
    public async Task The_chosen_specialty_is_sent_with_the_registration()
    {
        _handler.Respond(HttpStatusCode.OK, Json(new[] { new SpecialtyDto(InternalMedicine, "Belgyógyászat") }));

        var form = new RegistrationViewModel(Api);
        await form.LoadSpecialtiesAsync(default);

        // First entry is "decide later"; the real specialty is the second.
        form.Specialties.Count.ShouldBe(2);
        form.SelectedSpecialty = form.Specialties[1];

        _handler.Respond(HttpStatusCode.Created, Json(AVisit(doctorId: Kovacs, status: VisitStatus.Waiting)));

        form.FullName = "Kovács Anna";
        form.Address = "Budapest";
        form.Taj = "123-456-788";
        form.Complaint = "Fejfájás";

        await form.SubmitAsync(default);

        _handler.LastBody.ShouldContain(InternalMedicine.ToString());
    }

    [Fact]
    public async Task Registering_without_a_specialty_sends_none()
    {
        _handler.Respond(HttpStatusCode.OK, Json(new[] { new SpecialtyDto(InternalMedicine, "Belgyógyászat") }));

        var form = new RegistrationViewModel(Api);
        await form.LoadSpecialtiesAsync(default);

        // The default: a patient can arrive before anybody knows where they go.
        form.SelectedSpecialty.ShouldBe(SpecialtyChoice.DecideLater);

        _handler.Respond(HttpStatusCode.Created, Json(AVisit()));

        form.FullName = "Kovács Anna";
        form.Address = "Budapest";
        form.Taj = "123-456-788";
        form.Complaint = "Fejfájás";

        await form.SubmitAsync(default);

        _handler.LastBody.ShouldContain("\"specialtyId\":null");
    }

    [Fact]
    public void The_form_will_not_submit_until_the_required_boxes_have_something_in_them()
    {
        // Presence only. Whether a TAJ is well-formed is the domain's question
        // and this deliberately does not try to answer it.
        var form = new RegistrationViewModel(Api);

        form.CanSubmit.ShouldBeFalse();

        form.FullName = "Kovács Anna";
        form.Address = "Budapest";
        form.Taj = "nonsense";
        form.Complaint = "Fejfájás";

        form.CanSubmit.ShouldBeTrue("presence is the only thing the client checks");
    }

    private static async Task WaitUntilAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 200 && !until(); attempt++)
        {
            await Task.Delay(5);
        }

        until().ShouldBeTrue("the awaited condition never became true");
    }
}
