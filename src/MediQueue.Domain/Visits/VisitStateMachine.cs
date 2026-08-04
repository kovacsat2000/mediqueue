using System.Collections.Frozen;
using MediQueue.Domain.Exceptions;

namespace MediQueue.Domain.Visits;

/// <summary>
/// The only place a visit's status is allowed to change, expressed as a table
/// of legal moves rather than as branching code.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a deliberate extension point.</strong> Adding a transition —
/// a no-show sending a patient back to <see cref="VisitStatus.Waiting"/>, a
/// handover between doctors, reopening a completed visit — is a one-line change
/// to <see cref="AllowedTransitions"/>. Nothing else has to move, and the
/// exhaustive test over all sixteen ordered pairs immediately fails, so the
/// change cannot be made silently.
/// </para>
/// <para>
/// A table also makes the rule readable in one glance, which branching code
/// spread across four methods would not be.
/// </para>
/// </remarks>
public static class VisitStateMachine
{
    private static readonly FrozenDictionary<VisitStatus, IReadOnlySet<VisitStatus>> Table =
        new Dictionary<VisitStatus, IReadOnlySet<VisitStatus>>
        {
            [VisitStatus.Registered] = Set(VisitStatus.Waiting),
            [VisitStatus.Waiting] = Set(VisitStatus.InTreatment),
            [VisitStatus.InTreatment] = Set(VisitStatus.Done),
            [VisitStatus.Done] = FrozenSet<VisitStatus>.Empty,
        }.ToFrozenDictionary();

    /// <summary>
    /// Every legal move, keyed by the state being left. All four states appear;
    /// <see cref="VisitStatus.Done"/> maps to an empty set because it is terminal.
    /// </summary>
    public static IReadOnlyDictionary<VisitStatus, IReadOnlySet<VisitStatus>> AllowedTransitions => Table;

    /// <summary>Whether a visit may move directly from one state to another.</summary>
    /// <param name="from">The current state.</param>
    /// <param name="to">The proposed state.</param>
    /// <returns><c>true</c> only for a move the table permits; self-transitions are not permitted.</returns>
    public static bool CanTransition(VisitStatus from, VisitStatus to) => AllowedFrom(from).Contains(to);

    /// <summary>The states reachable from <paramref name="from"/>, empty if it is terminal.</summary>
    /// <param name="from">The state being left.</param>
    /// <returns>The legal destinations.</returns>
    public static IReadOnlySet<VisitStatus> AllowedFrom(VisitStatus from) =>
        Table.TryGetValue(from, out var allowed) ? allowed : FrozenSet<VisitStatus>.Empty;

    /// <summary>Permits a transition, or throws explaining what would have been permitted.</summary>
    /// <param name="from">The current state.</param>
    /// <param name="to">The proposed state.</param>
    /// <exception cref="InvalidVisitTransitionException">The move is not in the table.</exception>
    public static void EnsureCanTransition(VisitStatus from, VisitStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidVisitTransitionException(from, to, AllowedFrom(from));
        }
    }

    private static FrozenSet<VisitStatus> Set(params VisitStatus[] statuses) => statuses.ToFrozenSet();
}
