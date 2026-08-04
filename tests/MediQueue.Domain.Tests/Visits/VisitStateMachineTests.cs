using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Visits;

namespace MediQueue.Domain.Tests.Visits;

/// <summary>
/// The transition table is asserted exhaustively rather than along the happy
/// path. Every ordered pair of states appears below, so no transition can be
/// added, removed or reversed without a test failing.
/// </summary>
public class VisitStateMachineTests
{
    /// <summary>
    /// The three legal moves, declared here independently of the production
    /// table. If the two ever disagree, this file is the one that is right.
    /// </summary>
    private static readonly (VisitStatus From, VisitStatus To)[] LegalTransitions =
    [
        (VisitStatus.Registered, VisitStatus.Waiting),
        (VisitStatus.Waiting, VisitStatus.InTreatment),
        (VisitStatus.InTreatment, VisitStatus.Done),
    ];

    public static TheoryData<VisitStatus, VisitStatus, bool> AllOrderedPairs()
    {
        var data = new TheoryData<VisitStatus, VisitStatus, bool>();

        foreach (var from in Enum.GetValues<VisitStatus>())
        {
            foreach (var to in Enum.GetValues<VisitStatus>())
            {
                data.Add(from, to, LegalTransitions.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllOrderedPairs))]
    public void CanTransition_answers_correctly_for_every_ordered_pair(
        VisitStatus from,
        VisitStatus to,
        bool expected)
    {
        VisitStateMachine.CanTransition(from, to).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(AllOrderedPairs))]
    public void EnsureCanTransition_throws_for_exactly_the_disallowed_pairs(
        VisitStatus from,
        VisitStatus to,
        bool expected)
    {
        if (expected)
        {
            Should.NotThrow(() => VisitStateMachine.EnsureCanTransition(from, to));
        }
        else
        {
            Should.Throw<InvalidVisitTransitionException>(() => VisitStateMachine.EnsureCanTransition(from, to));
        }
    }

    [Fact]
    public void The_state_machine_has_exactly_four_states()
    {
        Enum.GetValues<VisitStatus>().Length.ShouldBe(4);
    }

    [Fact]
    public void The_state_machine_has_exactly_three_transitions()
    {
        VisitStateMachine.AllowedTransitions.Values.Sum(destinations => destinations.Count).ShouldBe(3);
    }

    [Fact]
    public void Every_state_appears_in_the_table_so_AllowedFrom_never_guesses()
    {
        foreach (var status in Enum.GetValues<VisitStatus>())
        {
            VisitStateMachine.AllowedTransitions.ShouldContainKey(status);
        }
    }

    [Fact]
    public void No_state_may_transition_to_itself()
    {
        foreach (var status in Enum.GetValues<VisitStatus>())
        {
            VisitStateMachine.CanTransition(status, status).ShouldBeFalse();
        }
    }

    [Fact]
    public void Done_is_terminal()
    {
        VisitStateMachine.AllowedFrom(VisitStatus.Done).ShouldBeEmpty();
    }

    [Fact]
    public void The_exception_carries_the_attempted_move_and_the_valid_alternatives()
    {
        var exception = Should.Throw<InvalidVisitTransitionException>(
            () => VisitStateMachine.EnsureCanTransition(VisitStatus.Registered, VisitStatus.Done));

        exception.From.ShouldBe(VisitStatus.Registered);
        exception.To.ShouldBe(VisitStatus.Done);
        exception.AllowedAlternatives.ShouldBe(new[] { VisitStatus.Waiting }, ignoreOrder: true);
    }

    [Fact]
    public void The_exception_message_names_the_state_the_attempt_and_the_alternatives()
    {
        var exception = Should.Throw<InvalidVisitTransitionException>(
            () => VisitStateMachine.EnsureCanTransition(VisitStatus.Registered, VisitStatus.Done));

        exception.Message.ShouldBe(
            "Cannot transition visit from 'Registered' to 'Done'. Valid transitions from 'Registered': Waiting.");
    }

    [Fact]
    public void The_exception_message_says_so_when_the_state_is_terminal()
    {
        var exception = Should.Throw<InvalidVisitTransitionException>(
            () => VisitStateMachine.EnsureCanTransition(VisitStatus.Done, VisitStatus.InTreatment));

        exception.AllowedAlternatives.ShouldBeEmpty();
        exception.Message.ShouldBe(
            "Cannot transition visit from 'Done' to 'InTreatment'. No transitions are valid from 'Done'.");
    }
}
