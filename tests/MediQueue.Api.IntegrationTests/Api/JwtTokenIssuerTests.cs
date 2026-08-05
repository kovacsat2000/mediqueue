using System.Net;
using System.Net.Http.Headers;
using MediQueue.Api.IntegrationTests.Persistence;
using MediQueue.Domain.Users;
using MediQueue.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace MediQueue.Api.IntegrationTests.Api;

/// <summary>
/// The token issuer, and what the running application does with the tokens it
/// produces.
/// </summary>
[Collection(PostgresCollection.Name)]
public class JwtTokenIssuerTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    private static JwtOptions Options => new()
    {
        Issuer = "mediqueue-dev",
        Audience = "mediqueue-clients",
        SigningKey = new string('k', 64),
        LifetimeHours = 8,
    };

    private static JwtTokenIssuer IssuerAt(DateTimeOffset now) =>
        new(Microsoft.Extensions.Options.Options.Create(Options), new FakeTimeProvider(now));

    private static User ADoctor() =>
        User.CreateDoctor("kovacs.istvan", "Dr. Kovács István", "hash", Guid.CreateVersion7(Now), Now);

    [Fact]
    public void The_expiry_is_the_configured_lifetime_after_the_injected_clock()
    {
        // Not "roughly now plus eight hours" — exactly the injected instant plus
        // the configured lifetime, which is what makes it assertable at all.
        var (_, expiresAt) = IssuerAt(Now).Issue(ADoctor());

        expiresAt.ShouldBe(new DateTimeOffset(2026, 8, 5, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Moving_the_clock_moves_the_expiry_with_it()
    {
        var (_, later) = IssuerAt(Now.AddDays(1)).Issue(ADoctor());

        later.ShouldBe(new DateTimeOffset(2026, 8, 6, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_assistant_gets_no_specialty_claim_at_all()
    {
        var (token, _) = IssuerAt(Now).Issue(
            User.CreateAssistant("horvath.anna", "Horváth Anna", "hash", Now));

        // Absent rather than null: a claim that is not there cannot be misread.
        token.ShouldNotContain(JwtTokenIssuer.SpecialtyIdClaim);
    }

    [Fact]
    public async Task A_token_issued_in_the_past_is_refused_by_the_running_application()
    {
        await using var factory = new MediQueueApiFactory(postgres);
        await factory.CreateReadyClientAsync();

        // Issued with the application's real signing key, but dated far enough
        // back that even the one-minute clock skew cannot rescue it.
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var live = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;

        var expired = new JwtTokenIssuer(
            Microsoft.Extensions.Options.Options.Create(live),
            new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(-2)));

        var (token, expiresAt) = expired.Issue(ADoctor());
        expiresAt.ShouldBeLessThan(DateTimeOffset.UtcNow);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await client.GetAsync("/api/specialties")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_that_expired_two_minutes_ago_is_already_refused()
    {
        // This is what actually pins ClockSkew. The two-day-old token above
        // would be refused at any skew setting, so it says nothing about the
        // configured value; a token two minutes past expiry is accepted under
        // the five-minute default and refused under the one minute configured.
        await using var factory = new MediQueueApiFactory(postgres);
        await factory.CreateReadyClientAsync();

        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var live = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;

        var oneHour = new JwtOptions
        {
            Issuer = live.Issuer,
            Audience = live.Audience,
            SigningKey = live.SigningKey,
            LifetimeHours = 1,
        };

        // Issued 62 minutes ago with a one-hour lifetime, so it is a perfectly
        // well-formed token that expired exactly two minutes ago. A zero-length
        // lifetime would not do: that puts `exp` on `nbf` and the handler
        // rejects it whatever the skew, which would prove nothing.
        var issuedInThePast = new JwtTokenIssuer(
            Microsoft.Extensions.Options.Options.Create(oneHour),
            new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-62)));

        var (token, expiredAt) = issuedInThePast.Issue(ADoctor());
        (DateTimeOffset.UtcNow - expiredAt).ShouldBeGreaterThan(TimeSpan.FromMinutes(1));
        (DateTimeOffset.UtcNow - expiredAt).ShouldBeLessThan(TimeSpan.FromMinutes(5));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Deliberately not /api/me: that looks the user up and answers 401 when
        // it finds nobody, so it cannot tell a rejected token from an unknown
        // user. /api/specialties needs a valid token and nothing else.
        (await client.GetAsync("/api/specialties")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("", "missing")]
    [InlineData("too-short", "shorter than HS256 allows")]
    [InlineData("0123456789012345678901234567890", "one byte short of the minimum")]
    public void A_signing_key_that_cannot_work_stops_the_application_at_startup(string key, string why)
    {
        // A misconfigured key must not wait until the first sign-in to surface as
        // a confusing 500 for one unlucky user.
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "mediqueue-dev",
                ["Jwt:Audience"] = "mediqueue-clients",
                ["Jwt:SigningKey"] = key,
            })
            .Build();

        var exception = Should.Throw<InvalidOperationException>(
            () => services.AddMediQueueAuthentication(configuration),
            $"a key that is {why} must be refused");

        exception.Message.ShouldContain("SigningKey");
    }

    [Fact]
    public void A_signing_key_of_exactly_the_minimum_length_is_accepted()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "mediqueue-dev",
                ["Jwt:Audience"] = "mediqueue-clients",
                ["Jwt:SigningKey"] = new string('k', JwtOptions.MinimumSigningKeyBytes),
            })
            .Build();

        Should.NotThrow(() => services.AddMediQueueAuthentication(configuration));
    }
}
