using System.Diagnostics.CodeAnalysis;
using System.Text;
using MediQueue.Domain.Exceptions;

namespace MediQueue.Domain.Patients;

/// <summary>
/// A patient's name, normalised and validated on the way in.
/// </summary>
/// <remarks>
/// <para>
/// The customer named three rules: it must be a real name, it must not be
/// empty, and it must not contain digits. Those are operationalised as: trimmed
/// and with internal whitespace runs collapsed; at least two characters; at
/// least one letter; no digits; and nothing outside letters, spaces, hyphens,
/// apostrophes and full stops — so <c>Dr. Kovács-Nagy Anna</c> and
/// <c>O'Brien Seán</c> both pass.
/// </para>
/// <para>
/// <strong>A minimum of two name parts is deliberately not enforced.</strong>
/// Mononyms exist, naming conventions vary between cultures, and the customer
/// did not ask for it. Rejecting a legal name is a worse failure than accepting
/// a terse one.
/// </para>
/// </remarks>
public sealed record PatientName
{
    private const int MinimumLength = 2;

    private PatientName(string value) => Value = value;

    /// <summary>The normalised name: trimmed, with internal whitespace collapsed to single spaces.</summary>
    public string Value { get; }

    /// <summary>Parses a patient name, throwing if it is not valid.</summary>
    /// <param name="input">The name as entered.</param>
    /// <returns>The normalised name.</returns>
    /// <exception cref="ValidationException">The input is not a usable name.</exception>
    public static PatientName Create(string? input)
    {
        if (!TryCreate(input, out var result, out var error))
        {
            throw new ValidationException(nameof(PatientName), error);
        }

        return result;
    }

    /// <summary>Attempts to parse a patient name.</summary>
    /// <param name="input">The name as entered.</param>
    /// <param name="result">The normalised name, when this returns <c>true</c>.</param>
    /// <param name="error">Why parsing failed, when this returns <c>false</c>.</param>
    /// <returns><c>true</c> if the input was a usable name.</returns>
    public static bool TryCreate(
        string? input,
        [NotNullWhen(true)] out PatientName? result,
        [NotNullWhen(false)] out string? error)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Name is required.";
            return false;
        }

        var normalised = CollapseWhitespace(input);

        if (normalised.Length < MinimumLength)
        {
            error = $"Name must be at least {MinimumLength} characters.";
            return false;
        }

        if (normalised.Any(char.IsDigit))
        {
            error = "Name must not contain digits.";
            return false;
        }

        if (!normalised.Any(char.IsLetter))
        {
            error = "Name must contain at least one letter.";
            return false;
        }

        if (!normalised.All(IsAllowed))
        {
            error = "Name may only contain letters, spaces, hyphens, apostrophes and full stops.";
            return false;
        }

        result = new PatientName(normalised);
        error = null;
        return true;
    }

    /// <summary>Renders the normalised name.</summary>
    /// <returns>The name as stored.</returns>
    public override string ToString() => Value;

    private static bool IsAllowed(char character) =>
        char.IsLetter(character) || character is ' ' or '-' or '\'' or '.';

    // Splitting on whitespace and rejoining trims the ends and collapses runs
    // in one step, so "  Nagy   Péter " becomes "Nagy Péter".
    //
    // Composing first matters more than it looks. "á" can arrive either as one
    // character or as "a" followed by a combining acute — macOS and some input
    // methods produce the second form — and a combining mark is not a letter,
    // so a decomposed "Kovács" was being rejected as containing a disallowed
    // character. Normalising also makes Value canonical, so two spellings of
    // the same name compare equal and hit the same unique index.
    private static string CollapseWhitespace(string input) =>
        string.Join(
            ' ',
            input.Normalize(NormalizationForm.FormC)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
