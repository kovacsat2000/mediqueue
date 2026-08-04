using MediQueue.Domain.Visits;

namespace MediQueue.Domain.Exceptions;

/// <summary>
/// Thrown when a visit is asked to move to a state it cannot legally reach.
/// </summary>
/// <remarks>
/// It carries the attempted move <em>and</em> the moves that would have been
/// legal, so the API can answer "you cannot do that, here is what you can do"
/// rather than a bare "invalid operation". That is the difference between an
/// error a client can act on and one it can only display.
/// </remarks>
public sealed class InvalidVisitTransitionException : DomainException
{
    /// <summary>Creates the exception for a rejected transition.</summary>
    /// <param name="from">The state the visit is in.</param>
    /// <param name="to">The state that was asked for.</param>
    /// <param name="allowedAlternatives">The states that <em>are</em> reachable from <paramref name="from"/>.</param>
    public InvalidVisitTransitionException(
        VisitStatus from,
        VisitStatus to,
        IReadOnlySet<VisitStatus> allowedAlternatives)
        : base(BuildMessage(from, to, allowedAlternatives))
    {
        From = from;
        To = to;
        AllowedAlternatives = allowedAlternatives;
    }

    /// <summary>The state the visit was in when the move was attempted.</summary>
    public VisitStatus From { get; }

    /// <summary>The state that was asked for and refused.</summary>
    public VisitStatus To { get; }

    /// <summary>The states that are reachable from <see cref="From"/>. Empty if it is terminal.</summary>
    public IReadOnlySet<VisitStatus> AllowedAlternatives { get; }

    private static string BuildMessage(
        VisitStatus from,
        VisitStatus to,
        IReadOnlySet<VisitStatus> allowedAlternatives)
    {
        var opening = $"Cannot transition visit from '{from}' to '{to}'.";

        if (allowedAlternatives.Count == 0)
        {
            return $"{opening} No transitions are valid from '{from}'.";
        }

        // Ordered so the message is identical every time it is produced;
        // a set has no inherent order and this text ends up in API responses.
        var alternatives = string.Join(", ", allowedAlternatives.Order());

        return $"{opening} Valid transitions from '{from}': {alternatives}.";
    }
}
