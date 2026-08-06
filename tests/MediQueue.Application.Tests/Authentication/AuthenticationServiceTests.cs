using MediQueue.Application.Abstractions;
using MediQueue.Application.Authentication;
using MediQueue.Contracts.Authentication;
using MediQueue.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using WireUserRole = MediQueue.Contracts.UserRole;

namespace MediQueue.Application.Tests.Authentication;

/// <summary>
/// Sign-in, with every collaborator substituted. The point of these tests is
/// that the use case can be exercised without a database, a web server, or a
/// single JWT type — which is the same thing as saying the dependencies point
/// the right way.
/// </summary>
public class AuthenticationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    private readonly IUserDirectory _users = Substitute.For<IUserDirectory>();
    private readonly IPasswordHasher<User> _passwordHasher = Substitute.For<IPasswordHasher<User>>();
    private readonly ITokenIssuer _tokenIssuer = Substitute.For<ITokenIssuer>();

    private AuthenticationService Service => new(_users, _passwordHasher, _tokenIssuer);

    private static User AnAssistant() =>
        User.CreateAssistant("horvath.anna", "Horváth Anna", "stored-hash", Now);

    private void GivenUser(User? user) =>
        _users.FindByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

    private void GivenPasswordVerdict(PasswordVerificationResult verdict) =>
        _passwordHasher
            .VerifyHashedPassword(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(verdict);

    private void GivenTokenExpiringAt(DateTimeOffset expiresAt) =>
        _tokenIssuer.Issue(Arg.Any<User>()).Returns(("issued-token", expiresAt));

    [Fact]
    public async Task Correct_credentials_produce_a_token_and_the_user()
    {
        var user = AnAssistant();
        GivenUser(user);
        GivenPasswordVerdict(PasswordVerificationResult.Success);
        GivenTokenExpiringAt(Now.AddHours(8));

        var response = await Service.LoginAsync(new LoginRequest("horvath.anna", "correct"), default);

        response.AccessToken.ShouldBe("issued-token");
        response.ExpiresAt.ShouldBe(Now.AddHours(8));
        response.User.Id.ShouldBe(user.Id);
        response.User.Username.ShouldBe("horvath.anna");
        response.User.FullName.ShouldBe("Horváth Anna");
        response.User.Role.ShouldBe(WireUserRole.Assistant);
        response.User.SpecialtyId.ShouldBeNull();
    }

    [Fact]
    public async Task A_rehash_hint_still_counts_as_a_correct_password()
    {
        // The stored hash used older parameters than the hasher would pick today.
        // The password is right, so the user gets in; upgrading the stored hash is
        // a password-lifecycle concern this system does not have.
        GivenUser(AnAssistant());
        GivenPasswordVerdict(PasswordVerificationResult.SuccessRehashNeeded);
        GivenTokenExpiringAt(Now.AddHours(8));

        var response = await Service.LoginAsync(new LoginRequest("horvath.anna", "correct"), default);

        response.AccessToken.ShouldBe("issued-token");
    }

    public static TheoryData<string> RefusalScenarios() =>
        ["unknown username", "wrong password", "inactive account"];

    [Theory]
    [MemberData(nameof(RefusalScenarios))]
    public async Task Every_refusal_looks_identical_from_the_outside(string scenario)
    {
        switch (scenario)
        {
            case "unknown username":
                GivenUser(null);
                break;
            case "wrong password":
                GivenUser(AnAssistant());
                GivenPasswordVerdict(PasswordVerificationResult.Failed);
                break;
            default:
                GivenUser(AnInactiveAssistant());
                GivenPasswordVerdict(PasswordVerificationResult.Success);
                break;
        }

        var exception = await Should.ThrowAsync<AuthenticationFailedException>(
            () => Service.LoginAsync(new LoginRequest("horvath.anna", "whatever"), default));

        // Identical type, identical message. Anything that distinguished them
        // would let an attacker enumerate usernames one request at a time.
        exception.Message.ShouldBe("Invalid username or password.");
        exception.Message.ShouldBe(AuthenticationFailedException.GenericMessage);
    }

    [Fact]
    public async Task An_inactive_user_is_refused_before_the_password_is_even_checked()
    {
        GivenUser(AnInactiveAssistant());
        GivenPasswordVerdict(PasswordVerificationResult.Success);

        await Should.ThrowAsync<AuthenticationFailedException>(
            () => Service.LoginAsync(new LoginRequest("horvath.anna", "correct"), default));

        _tokenIssuer.DidNotReceive().Issue(Arg.Any<User>());
    }

    [Fact]
    public async Task No_token_is_issued_when_the_password_is_wrong()
    {
        GivenUser(AnAssistant());
        GivenPasswordVerdict(PasswordVerificationResult.Failed);

        await Should.ThrowAsync<AuthenticationFailedException>(
            () => Service.LoginAsync(new LoginRequest("horvath.anna", "wrong"), default));

        _tokenIssuer.DidNotReceive().Issue(Arg.Any<User>());
    }

    [Fact]
    public async Task The_expiry_comes_from_the_issuer_rather_than_the_wall_clock()
    {
        // The issuer owns the clock; the service must report what it was given
        // and not consult a clock of its own. A fake time provider drives the
        // stub so the assertion is a fixed instant rather than "roughly now".
        var clock = new FakeTimeProvider(Now);
        GivenUser(AnAssistant());
        GivenPasswordVerdict(PasswordVerificationResult.Success);
        GivenTokenExpiringAt(clock.GetUtcNow().AddHours(8));

        var response = await Service.LoginAsync(new LoginRequest("horvath.anna", "correct"), default);

        response.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 8, 5, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_doctor_carries_their_specialty_into_the_response()
    {
        var specialtyId = Guid.CreateVersion7(Now);
        GivenUser(User.CreateDoctor("kovacs.istvan", "Dr. Kovács István", "hash", specialtyId, Now));
        GivenPasswordVerdict(PasswordVerificationResult.Success);
        GivenTokenExpiringAt(Now.AddHours(8));

        var response = await Service.LoginAsync(new LoginRequest("kovacs.istvan", "correct"), default);

        response.User.Role.ShouldBe(WireUserRole.Doctor);
        response.User.SpecialtyId.ShouldBe(specialtyId);
    }

    private static User AnInactiveAssistant()
    {
        var user = AnAssistant();
        user.Deactivate();

        return user;
    }
}
