namespace MediQueue.Domain.Exceptions;

/// <summary>
/// Thrown when a value fails the rules of the type it was meant to become.
/// </summary>
/// <remarks>
/// It carries the field name separately from the message so the API can answer
/// with a per-field validation problem rather than a single flat sentence.
/// </remarks>
public sealed class ValidationException : DomainException
{
    /// <summary>Creates the exception for a named field.</summary>
    /// <param name="fieldName">The field that failed, as the caller knows it.</param>
    /// <param name="message">What was wrong with it.</param>
    public ValidationException(string fieldName, string message)
        : base(message)
    {
        FieldName = fieldName;
    }

    /// <summary>The field that failed validation.</summary>
    public string FieldName { get; }
}
