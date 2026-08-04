using MediQueue.Domain.Scheduling;

namespace MediQueue.Domain.Tests.Scheduling;

public class ShortestQueueAssignmentStrategyTests
{
    private static readonly DateTimeOffset Morning = new(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid SpecialtyId = Guid.CreateVersion7(Morning);

    private readonly ShortestQueueAssignmentStrategy _strategy = new();

    // Ordered identifiers, so "lowest id" is unambiguous in the tie-break tests.
    private static readonly Guid DoctorA = new("00000000-0000-7000-8000-00000000000a");
    private static readonly Guid DoctorB = new("00000000-0000-7000-8000-00000000000b");
    private static readonly Guid DoctorC = new("00000000-0000-7000-8000-00000000000c");

    [Fact]
    public void Picks_the_doctor_with_the_shortest_waiting_queue()
    {
        Guid?[] candidates =
        [
            _strategy.SelectDoctor(SpecialtyId,
            [
                new DoctorWorkload(DoctorA, WaitingCount: 5, InTreatmentCount: 0, LastAssignedAt: null),
                new DoctorWorkload(DoctorB, WaitingCount: 1, InTreatmentCount: 0, LastAssignedAt: null),
                new DoctorWorkload(DoctorC, WaitingCount: 3, InTreatmentCount: 0, LastAssignedAt: null),
            ]),
        ];

        candidates[0].ShouldBe(DoctorB);
    }

    [Fact]
    public void Falls_through_to_the_treatment_count_when_the_queues_are_equal()
    {
        var chosen = _strategy.SelectDoctor(SpecialtyId,
        [
            new DoctorWorkload(DoctorA, WaitingCount: 2, InTreatmentCount: 1, LastAssignedAt: null),
            new DoctorWorkload(DoctorB, WaitingCount: 2, InTreatmentCount: 0, LastAssignedAt: null),
        ]);

        chosen.ShouldBe(DoctorB);
    }

    [Fact]
    public void Falls_through_to_the_least_recently_assigned_when_both_counts_are_equal()
    {
        var chosen = _strategy.SelectDoctor(SpecialtyId,
        [
            new DoctorWorkload(DoctorA, WaitingCount: 2, InTreatmentCount: 1, LastAssignedAt: Morning),
            new DoctorWorkload(DoctorB, WaitingCount: 2, InTreatmentCount: 1, LastAssignedAt: Morning.AddHours(-1)),
        ]);

        chosen.ShouldBe(DoctorB);
    }

    [Fact]
    public void A_doctor_who_has_never_been_assigned_anything_goes_first()
    {
        var chosen = _strategy.SelectDoctor(SpecialtyId,
        [
            new DoctorWorkload(DoctorA, WaitingCount: 2, InTreatmentCount: 1, LastAssignedAt: Morning.AddYears(-5)),
            new DoctorWorkload(DoctorB, WaitingCount: 2, InTreatmentCount: 1, LastAssignedAt: null),
        ]);

        chosen.ShouldBe(DoctorB);
    }

    [Fact]
    public void Falls_through_to_the_lowest_identifier_when_everything_else_is_equal()
    {
        var chosen = _strategy.SelectDoctor(SpecialtyId,
        [
            new DoctorWorkload(DoctorC, WaitingCount: 0, InTreatmentCount: 0, LastAssignedAt: null),
            new DoctorWorkload(DoctorA, WaitingCount: 0, InTreatmentCount: 0, LastAssignedAt: null),
            new DoctorWorkload(DoctorB, WaitingCount: 0, InTreatmentCount: 0, LastAssignedAt: null),
        ]);

        chosen.ShouldBe(DoctorA);
    }

    [Fact]
    public void The_choice_does_not_depend_on_the_order_the_candidates_arrive_in()
    {
        // Whatever order the database returns rows in, the answer must be the
        // same — otherwise the rule is not really a rule.
        DoctorWorkload[] workloads =
        [
            new(DoctorA, WaitingCount: 0, InTreatmentCount: 0, LastAssignedAt: null),
            new(DoctorB, WaitingCount: 0, InTreatmentCount: 0, LastAssignedAt: null),
            new(DoctorC, WaitingCount: 0, InTreatmentCount: 0, LastAssignedAt: null),
        ];

        var permutations = new[]
        {
            new[] { workloads[0], workloads[1], workloads[2] },
            new[] { workloads[0], workloads[2], workloads[1] },
            new[] { workloads[1], workloads[0], workloads[2] },
            new[] { workloads[1], workloads[2], workloads[0] },
            new[] { workloads[2], workloads[0], workloads[1] },
            new[] { workloads[2], workloads[1], workloads[0] },
        };

        foreach (var permutation in permutations)
        {
            _strategy.SelectDoctor(SpecialtyId, permutation).ShouldBe(DoctorA);
        }
    }

    [Fact]
    public void Repeated_calls_with_the_same_input_give_the_same_answer()
    {
        DoctorWorkload[] workloads =
        [
            new(DoctorA, WaitingCount: 1, InTreatmentCount: 1, LastAssignedAt: Morning),
            new(DoctorB, WaitingCount: 1, InTreatmentCount: 1, LastAssignedAt: Morning),
        ];

        var answers = Enumerable.Range(0, 10)
            .Select(_ => _strategy.SelectDoctor(SpecialtyId, workloads))
            .Distinct()
            .ToList();

        answers.ShouldHaveSingleItem();
        answers[0].ShouldBe(DoctorA);
    }

    [Fact]
    public void No_candidates_means_no_choice_rather_than_an_error()
    {
        // The domain does not decide what "nobody available" means. The caller
        // turns it into a 409 and leaves the visit unassigned.
        _strategy.SelectDoctor(SpecialtyId, []).ShouldBeNull();
    }

    [Fact]
    public void A_single_candidate_is_chosen_regardless_of_how_busy_they_are()
    {
        var chosen = _strategy.SelectDoctor(SpecialtyId,
        [
            new DoctorWorkload(DoctorA, WaitingCount: 99, InTreatmentCount: 99, LastAssignedAt: Morning),
        ]);

        chosen.ShouldBe(DoctorA);
    }
}
