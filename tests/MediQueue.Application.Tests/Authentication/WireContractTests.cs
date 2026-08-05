using DomainUserRole = MediQueue.Domain.Users.UserRole;
using WireUserRole = MediQueue.Contracts.UserRole;

namespace MediQueue.Application.Tests.Authentication;

/// <summary>
/// The wire enum and the domain enum are separate types on purpose: a desktop
/// client depends on the contract without dragging in the domain model. They
/// must nonetheless agree exactly, because the numbers are what travel.
/// </summary>
public class WireContractTests
{
    [Fact]
    public void The_wire_role_and_the_domain_role_have_the_same_members()
    {
        Enum.GetNames<WireUserRole>().ShouldBe(Enum.GetNames<DomainUserRole>(), ignoreOrder: true);
    }

    [Fact]
    public void The_wire_role_and_the_domain_role_have_the_same_numeric_values()
    {
        // Serialised as numbers, so a mismatch would silently turn an assistant
        // into a doctor rather than failing anything.
        foreach (var name in Enum.GetNames<DomainUserRole>())
        {
            var domain = (int)Enum.Parse<DomainUserRole>(name);
            var wire = (int)Enum.Parse<WireUserRole>(name);

            wire.ShouldBe(domain, $"role '{name}' must have the same value on both sides");
        }
    }

    [Fact]
    public void They_are_not_the_same_type()
    {
        typeof(WireUserRole).ShouldNotBe(typeof(DomainUserRole));
        typeof(WireUserRole).Assembly.ShouldNotBe(typeof(DomainUserRole).Assembly);
    }
}
