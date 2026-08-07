using MediQueue.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// A hub that exists only to answer one question: does a hub <em>invocation</em>
/// resolve <see cref="ICurrentUser"/> the way an HTTP request does?
/// </summary>
/// <remarks>
/// <para>
/// It lives in the test assembly and is mapped only by the test factory, the
/// same arrangement as <c>TestOnlyController</c> (D-41): a cross-cutting concern
/// made observable, never business logic, and no production build can contain
/// it.
/// </para>
/// <para>
/// It has to exist because <c>QueueHub</c> deliberately has no invokable
/// methods, and the two moments have genuinely different scopes in SignalR.
/// <c>OnConnectedAsync</c> still runs under the HTTP request that established
/// the connection; a later invocation does not, and
/// <c>IHttpContextAccessor</c> — which is what <c>ICurrentUser</c> reads — is
/// the classic thing to find null there. <c>QueueHub</c>'s grouping proves the
/// first moment. This proves the second, so that a P7 hub method which writes
/// does not discover D-37's failure a third time.
/// </para>
/// </remarks>
[Authorize]
public sealed class ScopeProbeHub(ICurrentUser currentUser) : Hub
{
    /// <summary>Where the factory maps this hub.</summary>
    public const string Path = "/hubs/test-scope-probe";

    /// <summary>What the ambient identity looks like from inside a hub invocation.</summary>
    /// <returns>The user id, role name and authentication flag, or nulls.</returns>
    public IdentitySnapshot WhoAmI() =>
        new(currentUser.UserId, currentUser.Role?.ToString(), currentUser.IsAuthenticated);

    /// <summary>What <see cref="ICurrentUser"/> reported during an invocation.</summary>
    /// <param name="UserId">The user id, or <c>null</c> if the scope lost it.</param>
    /// <param name="Role">The role name, or <c>null</c>.</param>
    /// <param name="IsAuthenticated">Whether the principal was authenticated.</param>
    public sealed record IdentitySnapshot(Guid? UserId, string? Role, bool IsAuthenticated);
}

/// <summary>Maps the probe hub without replacing the application's own pipeline.</summary>
/// <remarks>
/// An <c>IStartupFilter</c> rather than <c>builder.Configure</c>, which would
/// discard everything <c>Program.cs</c> sets up and leave the tests running
/// against a different application from the one that ships.
/// </remarks>
internal sealed class ScopeProbeHubStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        builder =>
        {
            next(builder);
            builder.UseEndpoints(endpoints => endpoints.MapHub<ScopeProbeHub>(ScopeProbeHub.Path));
        };
}
