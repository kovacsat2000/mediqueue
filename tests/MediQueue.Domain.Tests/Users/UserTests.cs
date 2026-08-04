using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Users;

namespace MediQueue.Domain.Tests.Users;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid SpecialtyId = Guid.CreateVersion7(Now);

    [Fact]
    public void An_assistant_is_created_without_a_specialty()
    {
        var user = User.CreateAssistant("kovacs.anna", "Kovács Anna", "hash", Now);

        user.Role.ShouldBe(UserRole.Assistant);
        user.SpecialtyId.ShouldBeNull();
        user.IsActive.ShouldBeTrue();
        user.Username.ShouldBe("kovacs.anna");
        user.FullName.ShouldBe("Kovács Anna");
    }

    [Fact]
    public void A_doctor_is_created_with_a_specialty()
    {
        var user = User.CreateDoctor("nagy.peter", "Dr. Nagy Péter", "hash", SpecialtyId, Now);

        user.Role.ShouldBe(UserRole.Doctor);
        user.SpecialtyId.ShouldBe(SpecialtyId);
        user.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void A_doctor_without_a_specialty_is_refused()
    {
        var exception = Should.Throw<DomainException>(
            () => User.CreateDoctor("nagy.peter", "Dr. Nagy Péter", "hash", Guid.Empty, Now));

        exception.Message.ShouldBe("A doctor must belong to a specialty.");
    }

    [Fact]
    public void An_assistant_can_never_be_given_a_specialty()
    {
        // There is deliberately no parameter through which to try: the factory
        // makes the illegal state unrepresentable rather than merely rejected.
        // The guard in the constructor remains as the backstop for any future
        // construction path, including the one EF Core will add in P2.
        typeof(User)
            .GetMethod(nameof(User.CreateAssistant))!
            .GetParameters()
            .ShouldNotContain(parameter => parameter.ParameterType == typeof(Guid));

        User.CreateAssistant("kovacs.anna", "Kovács Anna", "hash", Now).SpecialtyId.ShouldBeNull();
    }

    [Fact]
    public void Identifiers_are_version_7_so_they_sort_by_creation_time()
    {
        User.CreateAssistant("kovacs.anna", "Kovács Anna", "hash", Now).Id.Version.ShouldBe(7);
    }
}
