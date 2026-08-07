using MediQueue.Application.Abstractions;
using MediQueue.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MediQueue.Infrastructure.Realtime;

/// <summary>
/// The push channel. Clients connect, are placed in the groups their role
/// entitles them to, and receive queue events until they disconnect.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This hub has no methods a client can invoke, deliberately.</strong>
/// It is one-directional: the API is where writes happen, and every rule about
/// who may change what — the role policies, the ownership check in
/// <c>VisitLifecycleService</c>, the audit interceptor's actor — is written
/// against an HTTP request. A hub method that wrote anything would need its own
/// authorization story and its own audit story, duplicating both. Refusing the
/// surface is cheaper than defending it twice.
/// </para>
/// <para>
/// <strong>Group membership is the authorization.</strong> A doctor is placed
/// in <c>doctor:{their own id}</c> and nothing else, so a message addressed to
/// another doctor's group cannot reach them — there is no filtering step to
/// forget, because they were never a recipient.
/// </para>
/// </remarks>
[Authorize]
public sealed class QueueHub(ICurrentUser currentUser, ILogger<QueueHub> logger) : Hub
{
    /// <summary>The route the hub is mapped at. Shared with the bearer handler's query-string rule.</summary>
    public const string Path = "/hubs/queue";

    /// <summary>Every connected assistant.</summary>
    public const string AssistantGroup = "role:assistant";

    /// <summary>The group carrying one doctor's queue events.</summary>
    /// <param name="doctorId">Whose queue.</param>
    /// <returns>The group name.</returns>
    public static string DoctorGroup(Guid doctorId) => $"doctor:{doctorId}";

    /// <summary>Places the connection in the groups its identity entitles it to.</summary>
    /// <remarks>
    /// <para>
    /// The identity is read through <see cref="ICurrentUser"/> rather than
    /// straight off <c>Context.User</c>, on purpose: it is the same seam every
    /// HTTP request reads, so if it ever stopped resolving inside a hub scope
    /// this method would place nobody in a group and the failure would be
    /// immediate and total rather than silent. D-37's failure mode was an
    /// identity that vanished while everything else kept working; here the
    /// identity is load-bearing, which is the cheapest possible test of it.
    /// </para>
    /// <para>
    /// A connection with neither role is aborted rather than left in no group.
    /// A client that is connected but receives nothing looks like a broken
    /// server; a refused connection is something the client can report.
    /// </para>
    /// </remarks>
    public override async Task OnConnectedAsync()
    {
        switch (currentUser.Role)
        {
            case UserRole.Assistant:
                await Groups.AddToGroupAsync(Context.ConnectionId, AssistantGroup).ConfigureAwait(false);
                break;

            case UserRole.Doctor when currentUser.UserId is { } doctorId:
                await Groups.AddToGroupAsync(Context.ConnectionId, DoctorGroup(doctorId)).ConfigureAwait(false);
                break;

            default:
                logger.LogWarning(
                    "Refusing hub connection {ConnectionId}: the identity carries no usable role.",
                    Context.ConnectionId);

                Context.Abort();

                return;
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}
