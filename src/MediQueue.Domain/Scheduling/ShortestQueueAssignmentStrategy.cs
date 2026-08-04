namespace MediQueue.Domain.Scheduling;

/// <summary>
/// Sends the patient to whichever doctor is least busy, breaking ties in a fixed
/// order so the answer never depends on luck.
/// </summary>
/// <remarks>
/// The cascade is: fewest waiting, then fewest in treatment, then longest since
/// they were last given a patient, then lowest identifier. The last step exists
/// purely so the result is deterministic — without it, two equally idle doctors
/// would be separated by whatever order the database happened to return, which
/// is exactly the kind of rule that cannot be unit-tested.
/// </remarks>
public sealed class ShortestQueueAssignmentStrategy : IDoctorAssignmentStrategy
{
    /// <inheritdoc />
    public Guid? SelectDoctor(Guid specialtyId, IReadOnlyCollection<DoctorWorkload> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .OrderBy(candidate => candidate.WaitingCount)
            .ThenBy(candidate => candidate.InTreatmentCount)
            // A doctor who has never been assigned anything sorts first.
            .ThenBy(candidate => candidate.LastAssignedAt ?? DateTimeOffset.MinValue)
            .ThenBy(candidate => candidate.DoctorId)
            .Select(candidate => (Guid?)candidate.DoctorId)
            .FirstOrDefault();
    }
}
