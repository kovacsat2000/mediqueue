using System.Reflection;
using MediQueue.Client.Core.Api;
using MediQueue.Contracts.Visits;

namespace MediQueue.Client.Core.Tests;

/// <summary>
/// The shape of the assistant's API surface, asserted rather than reviewed.
/// </summary>
/// <remarks>
/// D-10 keeps a diagnosis away from an assistant by giving them a type that
/// cannot carry one. This is the same guarantee one layer earlier: the
/// assistant application registers only <see cref="IAssistantApi"/>, so if no
/// member of it can return a diagnosis then the application has no expressible
/// way to ask for one. A reflection test is what keeps that true after somebody
/// adds a convenience method in a hurry.
/// </remarks>
public class AssistantApiSurfaceTests
{
    /// <summary>Every method the interface offers, including the ones it inherits.</summary>
    /// <remarks>
    /// <c>GetMethods()</c> on an interface does not include members of the
    /// interfaces it extends, so walking the base list is not thoroughness — it
    /// is the difference between checking the surface and checking part of it.
    /// </remarks>
    private static IReadOnlyList<MethodInfo> MembersOf(Type contract) =>
    [
        .. contract.GetMethods(),
        .. contract.GetInterfaces().SelectMany(inherited => inherited.GetMethods()),
    ];

    /// <summary>Every type that appears in a signature, unwrapping Task and collections.</summary>
    private static IEnumerable<Type> TypesIn(MethodInfo method)
    {
        foreach (var type in method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
        {
            yield return type;

            foreach (var argument in Unwrap(type))
            {
                yield return argument;
            }
        }
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            yield return argument;

            foreach (var nested in Unwrap(argument))
            {
                yield return nested;
            }
        }
    }

    [Fact]
    public void The_assistants_api_cannot_express_a_request_that_returns_a_diagnosis()
    {
        // The assertion the whole role split exists for.
        var types = MembersOf(typeof(IAssistantApi)).SelectMany(TypesIn).Distinct().ToList();

        types.ShouldNotContain(
            typeof(VisitDetailDto),
            "no member of IAssistantApi may mention the type that carries a diagnosis");
    }

    [Fact]
    public void No_assistant_member_mentions_a_diagnosis_by_name_either()
    {
        // Belt and braces against a future member that returns a string called
        // Diagnosis, or a new DTO that carries one without being VisitDetailDto.
        foreach (var method in MembersOf(typeof(IAssistantApi)))
        {
            method.Name.ShouldNotContain("Diagnosis", Case.Insensitive);

            foreach (var type in TypesIn(method).Where(type => type.Namespace?.StartsWith("MediQueue", StringComparison.Ordinal) == true))
            {
                type.GetProperty("Diagnosis").ShouldBeNull(
                    $"'{method.Name}' reaches {type.Name}, which carries a diagnosis");
            }
        }
    }

    [Fact]
    public void The_doctors_api_does_carry_the_detail_type()
    {
        // So the test above is asserting a property of the assistant's surface
        // rather than a property of the reflection helper.
        MembersOf(typeof(IDoctorApi))
            .SelectMany(TypesIn)
            .ShouldContain(typeof(VisitDetailDto));
    }

    [Fact]
    public void The_assistants_lists_are_the_summary_type_which_has_no_diagnosis_member()
    {
        typeof(VisitSummaryDto).GetProperty("Diagnosis").ShouldBeNull();

        MembersOf(typeof(IAssistantApi))
            .SelectMany(TypesIn)
            .ShouldContain(typeof(VisitSummaryDto));
    }

    [Fact]
    public void One_implementation_satisfies_both_halves()
    {
        // The split is about what each shell can reach, not about two clients
        // to keep in step.
        typeof(IAssistantApi).IsAssignableFrom(typeof(MediQueueApiClient)).ShouldBeTrue();
        typeof(IDoctorApi).IsAssignableFrom(typeof(MediQueueApiClient)).ShouldBeTrue();
    }
}
