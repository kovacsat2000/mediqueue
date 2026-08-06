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

    [Fact]
    public void A_user_starts_active()
    {
        User.CreateDoctor("nagy.peter", "Dr. Nagy Péter", "hash", SpecialtyId, Now).IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Deactivating_takes_the_user_out_of_service()
    {
        var doctor = User.CreateDoctor("nagy.peter", "Dr. Nagy Péter", "hash", SpecialtyId, Now);

        doctor.Deactivate();

        doctor.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Deactivating_twice_is_a_no_op_rather_than_an_error()
    {
        // Unlike a second soft delete, which would overwrite who deleted a visit
        // first, a second deactivation has no information to destroy.
        var doctor = User.CreateDoctor("nagy.peter", "Dr. Nagy Péter", "hash", SpecialtyId, Now);
        doctor.Deactivate();

        Should.NotThrow(() => doctor.Deactivate());

        doctor.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Reactivating_puts_the_user_back_into_service()
    {
        var doctor = User.CreateDoctor("nagy.peter", "Dr. Nagy Péter", "hash", SpecialtyId, Now);
        doctor.Deactivate();

        doctor.Reactivate();

        doctor.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Reactivating_twice_is_a_no_op_rather_than_an_error()
    {
        var doctor = User.CreateDoctor("nagy.peter", "Dr. Nagy Péter", "hash", SpecialtyId, Now);

        Should.NotThrow(() => doctor.Reactivate());

        doctor.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void A_deactivation_round_trip_leaves_everything_else_alone()
    {
        var doctor = User.CreateDoctor("nagy.peter", "Dr. Nagy Péter", "hash", SpecialtyId, Now);
        var (id, username, fullName, passwordHash, role, specialtyId) =
            (doctor.Id, doctor.Username, doctor.FullName, doctor.PasswordHash, doctor.Role, doctor.SpecialtyId);

        doctor.Deactivate();
        doctor.Reactivate();

        doctor.Id.ShouldBe(id);
        doctor.Username.ShouldBe(username);
        doctor.FullName.ShouldBe(fullName);
        doctor.PasswordHash.ShouldBe(passwordHash);
        doctor.Role.ShouldBe(role);
        doctor.SpecialtyId.ShouldBe(specialtyId);
        doctor.IsActive.ShouldBeTrue();
    }
}
