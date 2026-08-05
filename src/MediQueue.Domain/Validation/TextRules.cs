using MediQueue.Domain.Exceptions;

namespace MediQueue.Domain.Validation;

/// <summary>
/// The floor every required text field stands on: present, trimmed, and within
/// a stated length.
/// </summary>
/// <remarks>
/// <para>
/// This is not the same thing as the rules on <c>PatientName</c> and
/// <c>TajNumber</c>. Those describe what a value <em>is</em>, and belong to a
/// type. This describes what any required field must satisfy regardless of what
/// it means, and applies to fields whose content the system has no opinion
/// about — a complaint, an address, a diagnosis.
/// </para>
/// <para>
/// It is internal because it is a shared implementation detail rather than part
/// of the domain's vocabulary; callers see the entity factories, not this.
/// </para>
/// </remarks>
internal static class TextRules
{
    /// <summary>Requires a non-blank value within <paramref name="maxLength"/>, and returns it trimmed.</summary>
    internal static string Required(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(fieldName, $"{fieldName} is required.");
        }

        var trimmed = value.Trim();

        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(
                fieldName,
                $"{fieldName} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// As <see cref="Required"/>, and additionally rejects internal whitespace —
    /// for values that are identifiers rather than prose.
    /// </summary>
    internal static string RequiredSingleWord(string? value, string fieldName, int maxLength)
    {
        var trimmed = Required(value, fieldName, maxLength);

        if (trimmed.Any(char.IsWhiteSpace))
        {
            throw new ValidationException(fieldName, $"{fieldName} must not contain spaces.");
        }

        return trimmed;
    }
}
