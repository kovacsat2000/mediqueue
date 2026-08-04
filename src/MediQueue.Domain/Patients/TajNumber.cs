using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MediQueue.Domain.Exceptions;

namespace MediQueue.Domain.Patients;

/// <summary>
/// A Hungarian social security number (TAJ), validated on the way in so that a
/// <see cref="TajNumber"/> in hand is always well-formed.
/// </summary>
/// <remarks>
/// <para>
/// Accepted input is the dashed form <c>123-123-123</c>. The value is kept as
/// nine bare digits — that is what gets stored and compared — and
/// <see cref="ToString"/> puts the dashes back for display.
/// </para>
/// <para>
/// The statutory checksum is implemented but <strong>off by default</strong>.
/// See <see cref="Create(string?, bool)"/> for why.
/// </para>
/// </remarks>
public sealed partial record TajNumber
{
    private const int DigitCount = 9;

    private TajNumber(string digits) => Digits = digits;

    /// <summary>The nine digits, without separators. This is the stored and compared form.</summary>
    public string Digits { get; }

    /// <summary>Parses a TAJ number, throwing if it is not valid.</summary>
    /// <param name="input">The dashed form, for example <c>123-123-123</c>.</param>
    /// <param name="validateChecksum">
    /// Whether to additionally apply the statutory check-digit rule. It ships
    /// <c>false</c> because the customer defined acceptance as format-only, and
    /// their own worked example (<c>123-123-123</c>) fails the checksum.
    /// Quietly tightening a rule the customer specified is a product decision,
    /// not an engineering one — so the rule is built, tested, and left to
    /// configuration.
    /// </param>
    /// <returns>The parsed number.</returns>
    /// <exception cref="ValidationException">The input is not a valid TAJ number.</exception>
    public static TajNumber Create(string? input, bool validateChecksum = false)
    {
        if (!TryCreate(input, validateChecksum, out var result, out var error))
        {
            throw new ValidationException(nameof(TajNumber), error);
        }

        return result;
    }

    /// <summary>Attempts to parse a TAJ number, applying the format rule only.</summary>
    /// <param name="input">The dashed form, for example <c>123-123-123</c>.</param>
    /// <param name="result">The parsed number, when this returns <c>true</c>.</param>
    /// <param name="error">Why parsing failed, when this returns <c>false</c>.</param>
    /// <returns><c>true</c> if the input was a valid TAJ number.</returns>
    public static bool TryCreate(
        string? input,
        [NotNullWhen(true)] out TajNumber? result,
        [NotNullWhen(false)] out string? error) =>
        TryCreate(input, validateChecksum: false, out result, out error);

    /// <summary>Attempts to parse a TAJ number.</summary>
    /// <param name="input">The dashed form, for example <c>123-123-123</c>.</param>
    /// <param name="validateChecksum">Whether to additionally apply the statutory check-digit rule.</param>
    /// <param name="result">The parsed number, when this returns <c>true</c>.</param>
    /// <param name="error">Why parsing failed, when this returns <c>false</c>.</param>
    /// <returns><c>true</c> if the input was a valid TAJ number.</returns>
    public static bool TryCreate(
        string? input,
        bool validateChecksum,
        [NotNullWhen(true)] out TajNumber? result,
        [NotNullWhen(false)] out string? error)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "TAJ number is required.";
            return false;
        }

        if (!AcceptedFormat().IsMatch(input))
        {
            error = "TAJ number must be nine digits in the form 123-123-123.";
            return false;
        }

        var digits = input.Replace("-", string.Empty, StringComparison.Ordinal);

        if (validateChecksum && ComputeCheckDigit(digits) != digits[DigitCount - 1] - '0')
        {
            error = "TAJ number check digit is incorrect.";
            return false;
        }

        result = new TajNumber(digits);
        error = null;
        return true;
    }

    /// <summary>Renders the dashed display form, for example <c>123-123-123</c>.</summary>
    /// <returns>The nine digits grouped in threes.</returns>
    public override string ToString() => $"{Digits[..3]}-{Digits[3..6]}-{Digits[6..]}";

    /// <summary>
    /// The statutory check digit: of the first eight digits, those in odd
    /// positions are weighted by three and those in even positions by seven;
    /// the sum modulo ten must equal the ninth digit.
    /// </summary>
    /// <remarks>
    /// Source: 1996. évi XX. törvény, 2. számú melléklet. Verified against the
    /// official worked example 673457015 — 18+49+9+28+15+49+0+7 = 175, and
    /// 175 mod 10 = 5, which is its ninth digit.
    /// </remarks>
    private static int ComputeCheckDigit(string digits)
    {
        var sum = 0;

        for (var index = 0; index < DigitCount - 1; index++)
        {
            // index 0 is the first digit, which is an odd position in the statute.
            var weight = index % 2 == 0 ? 3 : 7;
            sum += (digits[index] - '0') * weight;
        }

        return sum % 10;
    }

    /// <remarks>
    /// The customer wrote the rule as <c>^\d{3}-\d{3}-\d{3}$</c>, which in .NET
    /// is looser than it looks and was letting two malformed inputs through:
    /// <c>$</c> also matches immediately before a trailing newline, so a pasted
    /// value ending in one produced a "nine digit" number ten characters long;
    /// and <c>\d</c> matches every Unicode decimal digit, so Arabic-Indic and
    /// fullwidth forms were accepted for a Hungarian identifier and then fed to
    /// arithmetic that assumes ASCII. <c>\A</c>, <c>\z</c> and an explicit
    /// <c>[0-9]</c> say what the rule was always meant to say.
    /// </remarks>
    [GeneratedRegex(@"\A[0-9]{3}-[0-9]{3}-[0-9]{3}\z")]
    private static partial Regex AcceptedFormat();
}
