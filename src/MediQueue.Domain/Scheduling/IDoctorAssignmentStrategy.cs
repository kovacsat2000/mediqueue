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
    /// <param name="specialtyId">
    /// The specialty being routed to. <strong>The default strategy does not read
    /// this</strong> — the caller has already filtered <paramref name="candidates"/>
    /// to the specialty, so shortest-queue selection needs nothing more.
    /// <para>
    /// It is on the interface anyway, and deliberately. The whole argument for
    /// having this seam is that a different assignment policy should be a
    /// configuration change rather than an interface change — and a policy that
    /// reads per-specialty rules, such as a specialty where the senior
    /// consultant always takes new arrivals, or one with its own queue cap,
    /// needs the id. Removing it would leave the seam supporting only those
    /// policies that happen to be specialty-blind, which is most of its value
    /// gone.
    /// </para>
    /// </param>
    /// <param name="candidates">
    /// The available doctors and their current workloads, already filtered to
    /// <paramref name="specialtyId"/> by the caller.
    /// </param>
    /// <returns>The chosen doctor, or <c>null</c> if there were no candidates.</returns>
    Guid? SelectDoctor(Guid specialtyId, IReadOnlyCollection<DoctorWorkload> candidates);
}
