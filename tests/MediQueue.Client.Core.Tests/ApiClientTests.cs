using System.Net;
using MediQueue.Client.Core.Api;
using MediQueue.Contracts;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.Tests;

public class ApiClientTests
{
    private readonly StubHttpMessageHandler _handler = new();
    private readonly AuthSession _session = new();

    private MediQueueApiClient Client => new(_handler.CreateClient(), _session);

    private const string LoginBody = """
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

    private const string QueueBody = """
        [
          {
            "id": "019fd702-388a-790f-b10a-b82cc6053f25",
            "patientId": "019fd702-388a-7b41-84fb-d500a9e450fd",
            "patientFullName": "Kis Elemér",
            "taj": "123-456-788",
            "complaint": "Fejfájás",
            "specialtyId": "019fd616-1800-721f-88d1-65bd80be4c48",
            "specialtyName": "Belgyógyászat",
            "doctorId": "019fd616-1800-77f2-ae0b-1b6bf243505d",
            "doctorFullName": "Dr. Kovács István",
            "status": 3,
            "registeredAt": "2026-08-06T08:00:00+00:00",
            "queuedAt": "2026-08-06T08:00:00+00:00",
            "calledInAt": null,
            "completedAt": null
          }
        ]
        """;

    [Fact]
    public async Task Signing_in_records_the_user_and_the_token()
    {
        _handler.Respond(HttpStatusCode.OK, LoginBody);

        var login = await Client.LoginAsync("kovacs.istvan", "MediQueue123!", default);

        login.AccessToken.ShouldBe("a-token");
        _session.IsSignedIn.ShouldBeTrue();
        _session.CurrentUser!.FullName.ShouldBe("Dr. Kovács István");
        _session.CurrentUser.Role.ShouldBe(UserRole.Doctor);
    }

    [Fact]
    public async Task The_sign_in_request_itself_carries_no_bearer_token()
    {
        _handler.Respond(HttpStatusCode.OK, LoginBody);

        await Client.LoginAsync("kovacs.istvan", "MediQueue123!", default);

        _handler.Requests[0].Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task Every_call_after_signing_in_carries_the_bearer_token()
    {
        _handler.Respond(HttpStatusCode.OK, LoginBody).Respond(HttpStatusCode.OK, QueueBody);

        await Client.LoginAsync("kovacs.istvan", "MediQueue123!", default);
        await Client.GetMyQueueAsync(default);

        var authorization = _handler.LastRequest.Headers.Authorization;
        authorization.ShouldNotBeNull();
        authorization.Scheme.ShouldBe("Bearer");
        authorization.Parameter.ShouldBe("a-token");
    }

    [Fact]
    public async Task A_call_made_before_signing_in_carries_no_token()
    {
        _handler.Respond(HttpStatusCode.OK, QueueBody);

        await Client.GetMyQueueAsync(default);

        _handler.LastRequest.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task Signing_out_stops_the_token_being_attached()
    {
        _handler.Respond(HttpStatusCode.OK, LoginBody).Respond(HttpStatusCode.OK, QueueBody);
        await Client.LoginAsync("kovacs.istvan", "MediQueue123!", default);

        _session.SignOut();
        await Client.GetMyQueueAsync(default);

        _handler.LastRequest.Headers.Authorization.ShouldBeNull();
        _session.IsSignedIn.ShouldBeFalse();
        _session.CurrentUser.ShouldBeNull();
    }

    [Fact]
    public async Task The_status_deserialises_from_the_numeric_form_the_server_actually_sends()
    {
        // The API serialises enums as numbers. A client expecting strings would
        // fail only against the real server, which is the worst place to find out.
        _handler.Respond(HttpStatusCode.OK, QueueBody);

        var queue = await Client.GetMyQueueAsync(default);

        queue.ShouldHaveSingleItem().Status.ShouldBe(VisitStatus.InTreatment);
    }

    [Fact]
    public async Task A_problem_response_becomes_an_exception_carrying_the_servers_own_words()
    {
        _handler.Respond(
            HttpStatusCode.Forbidden,
            """
            {
              "type": "https://mediqueue.example/problems/forbidden",
              "title": "You may not do that",
              "status": 403,
              "detail": "This visit is not in your queue.",
              "traceId": "00-abc123-def456-00"
            }
            """,
            "application/problem+json");

        var exception = await Should.ThrowAsync<ApiException>(() => Client.GetMyQueueAsync(default));

        exception.Status.ShouldBe(403);
        exception.Title.ShouldBe("You may not do that");
        exception.Detail.ShouldBe("This visit is not in your queue.");
        exception.TraceId.ShouldBe("00-abc123-def456-00");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>", "text/html")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", "text/plain")]
    [InlineData(HttpStatusCode.InternalServerError, "not json at all {{{", "application/json")]
    public async Task A_body_that_is_not_a_problem_document_still_produces_a_usable_message(
        HttpStatusCode status,
        string body,
        string contentType)
    {
        // A proxy page or an empty 502 must not make the client throw while it
        // is handling a throw.
        _handler.Respond(status, body, contentType);

        var exception = await Should.ThrowAsync<ApiException>(() => Client.GetMyQueueAsync(default));

        exception.Status.ShouldBe((int)status);
        exception.Detail.ShouldNotBeNullOrWhiteSpace();
        exception.Detail.ShouldNotContain("{");
        exception.Detail.ShouldNotContain("<html");
    }

    [Fact]
    public async Task A_401_says_something_a_receptionist_could_act_on()
    {
        _handler.RespondEmpty(HttpStatusCode.Unauthorized);

        var exception = await Should.ThrowAsync<ApiException>(() => Client.GetMyQueueAsync(default));

        exception.Detail.ShouldBe("Your session is not valid. Sign in again.");
    }

    [Fact]
    public async Task No_exception_message_ever_contains_the_token()
    {
        _handler.Respond(HttpStatusCode.OK, LoginBody).RespondEmpty(HttpStatusCode.Forbidden);
        await Client.LoginAsync("kovacs.istvan", "MediQueue123!", default);

        var exception = await Should.ThrowAsync<ApiException>(() => Client.GetMyQueueAsync(default));

        exception.ToString().ShouldNotContain("a-token");
    }
}
