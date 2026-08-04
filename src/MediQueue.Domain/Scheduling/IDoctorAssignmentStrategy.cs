namespace MediQueue.Domain.Scheduling;

/// <summary>
/// Decides which doctor a newly routed visit should go to.
/// </summary>
/// <remarks>
/// The customer put this on the system, not on the assistant: the assistant
/// picks a <em>specialty</em> and the server picks the <em>doctor</em>. That
/// makes it the one genuinely algorithmic rule in the assignment, so it is named
/// and swappable rather than buried inside a service method. Round-robin, manual
/// override or specialisation weighting are then a registration change, not a
/// rewrite.
/// </remarks>
public interface IDoctorAssignmentStrategy
{
    /// <summary>Chooses a doctor from the candidates.</summary>
    /// <param name="specialtyId">The specialty being routed to. The candidates are already filtered to it.</param>
    /// <param name="candidates">The available doctors and their current workloads.</param>
    /// <returns>The chosen doctor, or <c>null</c> if there were no candidates.</returns>
    Guid? SelectDoctor(Guid specialtyId, IReadOnlyCollection<DoctorWorkload> candidates);
}
