using System.Net;
using MediQueue.Client.Core.Api;
using MediQueue.Client.Core.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace MediQueue.Client.Core.Tests;

/// <summary>
/// The view models, exercised without a UI framework.
/// </summary>
/// <remarks>
/// Nothing here references Avalonia. That is the property the whole
/// Client.Core / Client.Doctor split exists to provide, and this file is where
/// it is cashed in.
/// </remarks>
public class ViewModelTests
{
    private readonly StubHttpMessageHandler _handler = new();
    private readonly AuthSession _session = new();

    private MediQueueApiClient Api => new(_handler.CreateClient(), _session);

    private const string DoctorLogin = """
        {
          "accessToken": "a-token",
          "expiresAt": "2026-08-06T16:00:00+00:00",
          "user": {
            "id": "019fd616-1800-77f2-ae0b-1b6bf243505d",
            "username": "kovacs.istvan",
            "fullName": "Dr. Kovács István",
            "role": 2,
            "specialtyId": "019fd616-1800-721f-88d1-65bd80be4c48"
          }
        }
        """;

    private const string AssistantLogin = """
        {
          "accessToken": "a-token",
          "expiresAt": "2026-08-06T16:00:00+00:00",
          "user": {
            "id": "019fd616-1800-7aaa-ae0b-1b6bf243505d",
            "username": "horvath.anna",
            "fullName": "Horváth Anna",
            "role": 1,
            "specialtyId": null
          }
        }
        """;

    /// <summary>A visit queued at 08:00 UTC — 10:00 in Budapest in August.</summary>
    private const string QueueAtEightUtc = """
        [
          {
            "id": "019fd702-388a-790f-b10a-b82cc6053f25",
            "patientId": "019fd702-388a-7b41-84fb-d500a9e450fd",
            "patientFullName": "Kis Elemér",
            "taj": "123-456-788",
            "complaint": "Fejfájás",
            "specialtyId": null, "specialtyName": null,
            "doctorId": null, "doctorFullName": null,
            "status": 2,
            "registeredAt": "2026-08-06T07:45:00+00:00",
            "queuedAt": "2026-08-06T08:00:00+00:00",
            "calledInAt": null, "completedAt": null
          }
        ]
        """;

    private readonly FakeQueueConnection _realtime = new();

    private static FakeTimeProvider InZone(string timeZoneId)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 6, 8, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

        return clock;
    }

    [Fact]
    public async Task A_queued_time_is_rendered_in_the_configured_zone()
    {
        // 08:00Z is 10:00 in Budapest in August. The literal is asserted rather
        // than recomputed with the production formula, because a test that runs
        // the same conversion it is checking asserts nothing.
        _handler.Respond(HttpStatusCode.OK, QueueAtEightUtc);
        var queue = new QueueViewModel(Api, _session, _realtime, InZone("Europe/Budapest"));

        await queue.RefreshAsync(default);

        queue.Rows.ShouldHaveSingleItem().QueuedAtDisplay.ShouldBe("10:00");
    }

    [Theory]
    [InlineData("Europe/Budapest", "10:00")]
    [InlineData("UTC", "08:00")]
    [InlineData("America/New_York", "04:00")]
    [InlineData("Asia/Tokyo", "17:00")]
    public async Task The_same_instant_renders_differently_in_different_zones(string zone, string expected)
    {
        // Four literals, four zones, one wire value. If the conversion were
        // dropped, three of these would fail.
        _handler.Respond(HttpStatusCode.OK, QueueAtEightUtc);
        var queue = new QueueViewModel(Api, _session, _realtime, InZone(zone));

        await queue.RefreshAsync(default);

        queue.Rows[0].QueuedAtDisplay.ShouldBe(expected);
    }

    [Fact]
    public async Task The_row_carries_the_patient_and_the_complaint()
    {
        _handler.Respond(HttpStatusCode.OK, QueueAtEightUtc);
        var queue = new QueueViewModel(Api, _session, _realtime, InZone("Europe/Budapest"));

        await queue.RefreshAsync(default);

        var row = queue.Rows[0];
        row.PatientFullName.ShouldBe("Kis Elemér");
        row.Taj.ShouldBe("123-456-788");
        row.Complaint.ShouldBe("Fejfájás");
    }

    [Fact]
    public async Task An_empty_queue_is_reported_as_empty_rather_than_left_blank()
    {
        _handler.Respond(HttpStatusCode.OK, "[]");
        var queue = new QueueViewModel(Api, _session, _realtime, InZone("Europe/Budapest"));

        await queue.RefreshAsync(default);

        queue.Rows.ShouldBeEmpty();
        queue.IsEmpty.ShouldBeTrue();
        queue.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task A_refused_refresh_surfaces_the_servers_message_and_its_trace_id()
    {
        _handler.Respond(
            HttpStatusCode.Forbidden,
            """{"title":"You may not do that","status":403,"detail":"This is not your queue.","traceId":"00-abc-def-00"}""",
            "application/problem+json");
        var queue = new QueueViewModel(Api, _session, _realtime, InZone("Europe/Budapest"));

        await queue.RefreshAsync(default);

        queue.ErrorMessage!.ShouldContain("This is not your queue.");
        queue.ErrorMessage!.ShouldContain("00-abc-def-00");
        queue.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task A_failed_refresh_does_not_leave_the_spinner_running()
    {
        _handler.RespondEmpty(HttpStatusCode.InternalServerError);
        var queue = new QueueViewModel(Api, _session, _realtime, InZone("UTC"));

        await queue.RefreshAsync(default);

        // In a finally block, so a failure cannot strand the UI behind a spinner.
        queue.IsBusy.ShouldBeFalse();
        queue.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Signing_in_with_the_wrong_password_shows_the_servers_message()
    {
        _handler.Respond(
            HttpStatusCode.Unauthorized,
            """{"title":"Authentication failed","status":401,"detail":"Invalid username or password.","traceId":"00-x-y-00"}""",
            "application/problem+json");
        var login = new LoginViewModel(Api, _session) { Username = "kovacs.istvan", Password = "wrong" };

        await login.SignInAsync(default);

        // The server's own sentence, which deliberately does not say which half
        // was wrong.
        login.ErrorMessage.ShouldBe("Invalid username or password.");
        login.IsBusy.ShouldBeFalse();
        _session.IsSignedIn.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unreachable_server_is_reported_as_such_rather_than_as_a_crash()
    {
        var login = new LoginViewModel(
            new MediQueueApiClient(new HttpClient(new UnreachableHandler())
            {
                BaseAddress = new Uri("http://localhost:5123/"),
            }, _session),
            _session)
        { Username = "kovacs.istvan", Password = "MediQueue123!" };

        await login.SignInAsync(default);

        login.ErrorMessage!.ShouldContain("not reachable");
        login.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task A_successful_doctor_sign_in_raises_the_event()
    {
        _handler.Respond(HttpStatusCode.OK, DoctorLogin);
        var login = new LoginViewModel(Api, _session) { Username = "kovacs.istvan", Password = "MediQueue123!" };
        var raised = false;
        login.SignedIn += (_, _) => raised = true;

        await login.SignInAsync(default);

        raised.ShouldBeTrue();
        login.ErrorMessage.ShouldBeNull();
        // Not kept in memory a moment longer than the request needs it.
        login.Password.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_assistant_signing_into_the_doctor_application_is_told_plainly()
    {
        // Each shell accepts only its own role. Showing an assistant an empty
        // doctor queue would look like a bug rather than like a refusal.
        _handler.Respond(HttpStatusCode.OK, AssistantLogin);
        var login = new LoginViewModel(Api, _session) { Username = "horvath.anna", Password = "MediQueue123!" };
        var raised = false;
        login.SignedIn += (_, _) => raised = true;

        await login.SignInAsync(default);

        raised.ShouldBeFalse();
        login.ErrorMessage!.ShouldContain("for doctors");
        _session.IsSignedIn.ShouldBeFalse();
    }

    [Fact]
    public async Task The_shell_moves_to_the_queue_once_a_doctor_signs_in()
    {
        _handler.Respond(HttpStatusCode.OK, DoctorLogin).Respond(HttpStatusCode.OK, "[]");
        var login = new LoginViewModel(Api, _session);
        var queue = new QueueViewModel(Api, _session, _realtime, InZone("Europe/Budapest"));
        var shell = new ShellViewModel(login, queue);

        shell.Current.ShouldBeSameAs(login);

        login.Username = "kovacs.istvan";
        login.Password = "MediQueue123!";
        await login.SignInAsync(default);

        shell.Current.ShouldBeSameAs(queue);
        queue.DoctorName.ShouldBe("Dr. Kovács István");
    }

    [Fact]
    public async Task Two_overlapping_refreshes_do_not_show_every_patient_twice()
    {
        // Found by driving the real view models against the running server: the
        // shell starts a refresh when a doctor signs in, and anything that
        // refreshes at the same time used to clear-then-add alongside it, so the
        // list ended up with each patient once per refresh in flight.
        _handler.Respond(HttpStatusCode.OK, QueueAtEightUtc).Respond(HttpStatusCode.OK, QueueAtEightUtc);
        var queue = new QueueViewModel(Api, _session, _realtime, InZone("Europe/Budapest"));

        await Task.WhenAll(
            queue.RefreshAsync(default),
            queue.RefreshAsync(default));

        queue.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Signing_in_leaves_the_shell_with_a_single_load_running()
    {
        _handler.Respond(HttpStatusCode.OK, DoctorLogin).Respond(HttpStatusCode.OK, QueueAtEightUtc);
        var login = new LoginViewModel(Api, _session);
        var queue = new QueueViewModel(Api, _session, _realtime, InZone("Europe/Budapest"));
        _ = new ShellViewModel(login, queue);

        login.Username = "kovacs.istvan";
        login.Password = "MediQueue123!";
        await login.SignInAsync(default);

        // The shell drives the start through the command, which owns the task
        // — an async lambda on an event would be fire-and-forget and its
        // failures would go nowhere.
        // Discarded rather than bare: Shouldly returns the value it checked, and
        // an unawaited Task-typed expression statement is CS4014.
        var running = queue.StartCommand.ExecutionTask;
        _ = running.ShouldNotBeNull();
        await running;

        queue.Rows.Count.ShouldBe(1);
        queue.IsBusy.ShouldBeFalse();

        // Signing in both opens the push channel and loads the list, in that
        // order, so nothing happening during the fetch is missed.
        _realtime.StartCount.ShouldBe(1);
        queue.IsLive.ShouldBeTrue();
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused");
    }
}
