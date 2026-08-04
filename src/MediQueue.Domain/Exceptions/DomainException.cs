namespace MediQueue.Domain.Exceptions;

/// <summary>
/// Base type for every error the domain raises deliberately, as opposed to the
/// ones the runtime raises when something is broken.
/// </summary>
/// <remarks>
/// The distinction earns its keep at the API boundary: anything deriving from
/// this is a rule the caller broke and can be told about, so it becomes a 4xx
/// with a useful message. Anything else is a defect and becomes a 500.
/// </remarks>
public class DomainException : Exception
{
    /// <summary>Creates the exception with a message describing the rule that was broken.</summary>
    /// <param name="message">A message meant to be readable by the caller, not only by a developer.</param>
    public DomainException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the error that caused it.</summary>
    /// <param name="message">A message meant to be readable by the caller, not only by a developer.</param>
    /// <param name="innerException">The underlying error.</param>
    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
