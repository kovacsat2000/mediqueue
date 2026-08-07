using System.Text;
using MediQueue.Infrastructure.Realtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MediQueue.Infrastructure.Authentication;

/// <summary>Registers token issuance and validation.</summary>
public static class AuthenticationExtensions
{
    /// <summary>Adds JWT bearer authentication and the two role policies.</summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Supplies the <c>Jwt</c> section.</param>
    /// <returns>The container, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The signing key is missing or too short.</exception>
    public static IServiceCollection AddMediQueueAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        // Checked here, at startup, rather than left to fail on the first login.
        // A signing key that is absent or too short is a deployment mistake, and
        // it should stop the application with a sentence that says what to fix —
        // not surface hours later as an opaque 500 for one unlucky user.
        EnsureSigningKeyIsUsable(options);

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                // Without this, the handler rewrites "sub" and "role" into long
                // WS-Federation URIs on the way in. Authorisation then looks for
                // a role claim that is no longer called "role", finds nothing,
                // and refuses every request with a 403 that explains nothing.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),

                    // The default is five minutes, which silently keeps expired
                    // tokens working. One minute is enough for ordinary clock
                    // drift between a client and the server.
                    ClockSkew = TimeSpan.FromMinutes(1),

                    // The other half of the mapping fix: tell the handler which
                    // claims actually carry the name and the role.
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    RoleClaimType = JwtTokenIssuer.RoleClaim,
                };

                bearer.Events = new JwtBearerEvents { OnMessageReceived = ReadHubAccessToken };
            });

        // Named policies rather than [Authorize(Roles = "...")] scattered around.
        // The names are the vocabulary the rest of the system reads in, and a
        // rule that lives in one place can be changed in one place.
        services
            .AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.AssistantOnly, policy =>
                policy.RequireAuthenticatedUser().RequireRole(nameof(Domain.Users.UserRole.Assistant)))
            .AddPolicy(AuthorizationPolicies.DoctorOnly, policy =>
                policy.RequireAuthenticatedUser().RequireRole(nameof(Domain.Users.UserRole.Doctor)))
            // Authentication is the default. A new endpoint is protected unless
            // somebody deliberately opens it, which is the right way round: the
            // cost of forgetting is then a 401 in testing rather than an open
            // door in production.
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }

    /// <summary>
    /// Accepts a bearer token from the <c>access_token</c> query string, for hub
    /// requests and for nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A browser WebSocket cannot set an <c>Authorization</c> header, so SignalR
    /// puts the token in the query string. That is the only reason this exists.
    /// </para>
    /// <para>
    /// <strong>The path check is the point of the method, not a detail of it.</strong>
    /// A token in a query string is a token in every access log, every proxy
    /// record, every referrer header and every URL somebody pastes into a bug
    /// report. Honouring <c>access_token</c> on <em>all</em> paths would mean
    /// any request in the system could be authenticated by a live credential
    /// sitting in plain text in a log file — and it would do so silently,
    /// because everything would keep working. Restricting it to the hub confines
    /// that exposure to one route whose clients genuinely cannot do better.
    /// </para>
    /// <para>
    /// <c>StartsWith</c> rather than equality because SignalR appends its own
    /// segments — <c>/negotiate</c> — beneath the hub path.
    /// </para>
    /// </remarks>
    private static Task ReadHubAccessToken(MessageReceivedContext context)
    {
        var path = context.HttpContext.Request.Path;

        if (path.StartsWithSegments(QueueHub.Path, StringComparison.OrdinalIgnoreCase))
        {
            context.Token = context.Request.Query["access_token"];
        }

        return Task.CompletedTask;
    }

    private static void EnsureSigningKeyIsUsable(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            throw new InvalidOperationException(
                $"Configuration '{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}' is missing. " +
                "Set it in appsettings.Development.json for local runs, or as an environment variable.");
        }

        var keyBytes = Encoding.UTF8.GetByteCount(options.SigningKey);

        if (keyBytes < JwtOptions.MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"Configuration '{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}' is {keyBytes} bytes; " +
                $"HS256 requires at least {JwtOptions.MinimumSigningKeyBytes}. Use a longer key.");
        }
    }
}
