using System.Net;
using MediQueue.Client.Core.Api;
using MediQueue.Client.Core.Realtime;
using MediQueue.Client.Core.ViewModels;
using MediQueue.Contracts;
using MediQueue.Contracts.Authentication;
using MediQueue.Contracts.Visits;
using Microsoft.Extensions.Time.Testing;

namespace MediQueue.Client.Core.Tests;

/// <summary>
/// What the queue does when the server pushes at it.
/// </summary>
/// <remarks>
/// No hub and no window: the push channel is an interface here, so these assert
/// the view model's reaction rather than SignalR's plumbing. The plumbing is
/// tested against a real hub in the integration suite.
/// </remarks>
public class RealtimeQueueTests
{
    private static readonly Guid TheDoctor = Guid.CreateVersion7(new DateTimeOffset(2026, 8, 6, 8, 0, 0, TimeSpan.Zero));
    private static readonly DateTimeOffset EightUtc = new(2026, 8, 6, 8, 0, 0, TimeSpan.Zero);

    private readonly StubHttpMessageHandler _handler = new();
    private readonly FakeQueueConnection _realtime = new();
    private readonly IAuthSession _session = new AuthSession();

    public RealtimeQueueTests() =>
        _session.SignIn(new LoginResponse(
            "token",
            EightUtc.AddHours(8),
            new UserDto(TheDoctor, "kovacs.istvan", "Dr. Kovács István", UserRole.Doctor, Guid.CreateVersion7(EightUtc))));

    private MediQueueApiClient Api => new(new HttpClient(_handler) { BaseAddress = new Uri("http://localhost/") }, _session);

    private QueueViewModel AQueue()
    {
        var clock = new FakeTimeProvider(EightUtc);
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);

        return new QueueViewModel(Api, _session, _realtime, clock);
    }

    private static VisitSummaryDto AVisit(
        Guid? id = null,
        string name = "Kovács Anna",
        DateTimeOffset? queuedAt = null,
        VisitStatus status = VisitStatus.Waiting,
        Guid? doctorId = null) =>
        new(
            id ?? Guid.CreateVersion7(queuedAt ?? EightUtc),
            Guid.CreateVersion7(EightUtc),
            name,
            "123-456-788",
            "Fejfájás",
            Guid.CreateVersion7(EightUtc),
            "Belgyógyászat",
            doctorId ?? TheDoctor,
            "Dr. Kovács István",
            status,
            EightUtc,
            queuedAt ?? EightUtc,
            null,
            null);

    /// <summary>Lets the fire-and-forget handler finish before the assertion reads the rows.</summary>
    /// <remarks>
    /// An event handler cannot be awaited, so the push path is started and not
    /// returned. Every test that pushes waits for the row count to settle rather
    /// than sleeping for a guessed interval.
    /// </remarks>
    private static async Task SettleAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 200 && !until(); attempt++)
        {
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task A_queued_visit_arrives_as_a_new_row()
    {
        var queue = AQueue();
        var visit = AVisit(name: "Nagy Piroska");

        _realtime.PushQueued(visit);
        await SettleAsync(() => queue.Rows.Count == 1);

        var row = queue.Rows.ShouldHaveSingleItem();
        row.VisitId.ShouldBe(visit.Id);
        row.PatientFullName.ShouldBe("Nagy Piroska");
    }

    [Fact]
    public async Task A_release_removes_the_row()
    {
        var queue = AQueue();
        var visit = AVisit();

        _realtime.PushQueued(visit);
        await SettleAsync(() => queue.Rows.Count == 1);

        _realtime.PushReleased(visit with { Status = VisitStatus.Done });
        await SettleAsync(() => queue.Rows.Count == 0);

        queue.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_withdrawal_removes_the_row()
    {
        var queue = AQueue();
        var visit = AVisit();

        _realtime.PushQueued(visit);
        await SettleAsync(() => queue.Rows.Count == 1);

        _realtime.PushDeleted(visit.Id, TheDoctor);
        await SettleAsync(() => queue.Rows.Count == 0);

        queue.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_call_in_updates_the_row_in_place_rather_than_adding_one()
    {
        var queue = AQueue();
        var first = AVisit(name: "Első Beteg", queuedAt: EightUtc);
        var second = AVisit(name: "Második Beteg", queuedAt: EightUtc.AddMinutes(10));

        _realtime.PushQueued(first);
        _realtime.PushQueued(second);
        await SettleAsync(() => queue.Rows.Count == 2);

        _realtime.PushCalledIn(first with { Status = VisitStatus.InTreatment });
        await SettleAsync(() => queue.Rows[0].Status == nameof(VisitStatus.InTreatment));

        queue.Rows.Count.ShouldBe(2);

        // Still first: a status change must not make a patient jump position.
        queue.Rows[0].PatientFullName.ShouldBe("Első Beteg");
        queue.Rows[0].Status.ShouldBe(nameof(VisitStatus.InTreatment));
    }

    [Fact]
    public async Task Rows_stay_in_arrival_order_whatever_order_they_arrive_in()
    {
        var queue = AQueue();

        var late = AVisit(name: "Kései Beteg", queuedAt: EightUtc.AddMinutes(30));
        var early = AVisit(name: "Korai Beteg", queuedAt: EightUtc);
        var middle = AVisit(name: "Középső Beteg", queuedAt: EightUtc.AddMinutes(15));

        // Deliberately out of order — a push describes when something happened,
        // not where it belongs on screen.
        _realtime.PushQueued(late);
        await SettleAsync(() => queue.Rows.Count == 1);
        _realtime.PushQueued(early);
        await SettleAsync(() => queue.Rows.Count == 2);
        _realtime.PushQueued(middle);
        await SettleAsync(() => queue.Rows.Count == 3);

        queue.Rows.Select(row => row.PatientFullName)
            .ShouldBe(["Korai Beteg", "Középső Beteg", "Kései Beteg"]);
    }

    [Fact]
    public async Task Another_doctors_visit_is_not_added_to_this_queue()
    {
        // The server never addresses it here — a doctor is only in their own
        // group — but this list is "my queue", and what belongs in it is the
        // view model's own question.
        var queue = AQueue();

        _realtime.PushQueued(AVisit(doctorId: Guid.CreateVersion7(EightUtc.AddDays(1))));
        await SettleAsync(() => queue.Rows.Count > 0);

        queue.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_push_arriving_during_a_refresh_does_not_duplicate_the_row()
    {
        // The P4b bug from the other side. The refresh holds its lock across the
        // HTTP call as well as the row swap, so a push cannot land between the
        // fetch and the swap; it applies strictly before or strictly after.
        var visit = AVisit(name: "Kovács Anna");

        _handler.Respond(HttpStatusCode.OK, $"[{Json(visit)}]");

        var queue = AQueue();

        var refreshing = queue.RefreshAsync(default);
        _realtime.PushQueued(visit);

        await refreshing;
        await SettleAsync(() => queue.Rows.Count != 1);

        // One row, not two: the push described a visit the refresh also
        // returned, and Upsert replaced rather than appended.
        queue.Rows.Count.ShouldBe(1);
        queue.Rows[0].VisitId.ShouldBe(visit.Id);
    }

    [Fact]
    public async Task A_push_that_arrives_during_a_refresh_is_applied_rather_than_dropped()
    {
        // A second refresh may be skipped, because the one running produces the
        // same answer. A push may not: it is the only notice this client will
        // get, and dropping it leaves the screen disagreeing with the database.
        var alreadyThere = AVisit(name: "Régi Beteg", queuedAt: EightUtc);
        var arriving = AVisit(name: "Új Beteg", queuedAt: EightUtc.AddMinutes(20));

        _handler.Respond(HttpStatusCode.OK, $"[{Json(alreadyThere)}]");

        var queue = AQueue();

        var refreshing = queue.RefreshAsync(default);
        _realtime.PushQueued(arriving);

        await refreshing;
        await SettleAsync(() => queue.Rows.Count == 2);

        queue.Rows.Select(row => row.PatientFullName).ShouldBe(["Régi Beteg", "Új Beteg"]);
    }

    [Fact]
    public void The_connection_state_is_surfaced_when_the_hub_drops()
    {
        var queue = AQueue();

        _realtime.Report(RealtimeStatus.Live);
        queue.IsLive.ShouldBeTrue();
        queue.ConnectionStatus.ShouldBe(RealtimeStatus.Live);

        _realtime.Report(RealtimeStatus.Reconnecting);
        queue.IsLive.ShouldBeFalse();
        queue.ConnectionStatus.ShouldBe(RealtimeStatus.Reconnecting);

        _realtime.Report(RealtimeStatus.Disconnected);
        queue.ConnectionStatus.ShouldBe(RealtimeStatus.Disconnected);
    }

    [Fact]
    public async Task Coming_back_from_a_reconnect_refetches_the_queue()
    {
        // Automatic reconnect restores the socket; it does not replay what was
        // sent while the client was away. Coming back Live means "you have
        // missed an unknown amount", so the list is fetched rather than trusted.
        _handler.Respond(HttpStatusCode.OK, $"[{Json(AVisit(name: "Visszatért Beteg"))}]");

        var queue = AQueue();

        _realtime.Report(RealtimeStatus.Reconnecting);
        _realtime.Report(RealtimeStatus.Live);

        await SettleAsync(() => queue.Rows.Count == 1);

        queue.Rows.ShouldHaveSingleItem().PatientFullName.ShouldBe("Visszatért Beteg");
    }

    [Fact]
    public async Task A_hub_that_will_not_open_still_leaves_a_working_queue()
    {
        // Degraded, not broken: the list loads over HTTP and the status line is
        // what tells the doctor it is no longer updating by itself.
        _realtime.StartFailure = new HttpRequestException("the hub is unreachable");
        _handler.Respond(HttpStatusCode.OK, $"[{Json(AVisit())}]");

        var queue = AQueue();

        await queue.StartAsync(default);

        queue.Rows.Count.ShouldBe(1);
        queue.ConnectionStatus.ShouldBe(RealtimeStatus.Disconnected);
        queue.IsLive.ShouldBeFalse();
    }

    private static string Json(VisitSummaryDto visit) => System.Text.Json.JsonSerializer.Serialize(visit);
}
