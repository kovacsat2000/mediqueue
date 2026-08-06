using MediQueue.Domain.Visits;

namespace MediQueue.Application.Abstractions;

/// <summary>Finds and stores visits.</summary>
/// <remarks>
/// Every method names the question it answers. There is no <c>IQueryable</c>
/// here and no <c>IRepository&lt;T&gt;</c>: an interface that returns a query
/// leaks the storage model through it and abstracts nothing.
/// </remarks>
public interface IVisitRepository
{
    /// <summary>Loads one visit. Soft-deleted visits are invisible, so this returns <c>null</c> for them.</summary>
    /// <param name="id">The visit.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The visit, or <c>null</c>.</returns>
    Task<Visit?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether the patient already has a visit that has not finished.</summary>
    /// <param name="patientId">The patient.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> if any visit of theirs is in a state other than <c>Done</c>.</returns>
    Task<bool> HasOpenVisitAsync(Guid patientId, CancellationToken cancellationToken);

    /// <summary>One doctor's waiting list, in arrival order.</summary>
    /// <param name="doctorId">The doctor.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Their waiting and in-treatment visits, oldest first by queue time.</returns>
    Task<IReadOnlyList<Visit>> GetQueueAsync(Guid doctorId, CancellationToken cancellationToken);

    /// <summary>Every visit that has not finished, across all doctors.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The open visits.</returns>
    Task<IReadOnlyList<Visit>> GetAllOpenVisitsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Visits that have arrived but have not been routed anywhere, oldest first.
    /// </summary>
    /// <remarks>
    /// Every other listing groups by doctor, and a registered visit has none —
    /// so without this, a patient registered without a specialty appeared in no
    /// list at all and could only be reached by an identifier nobody had seen.
    /// A state the specification names, which the system could enter and never
    /// show, was not really implemented.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Registered visits, in arrival order.</returns>
    Task<IReadOnlyList<Visit>> GetUnassignedAsync(CancellationToken cancellationToken);

    /// <summary>Stages a new visit. Nothing is written until the unit of work commits.</summary>
    /// <param name="visit">The visit.</param>
    void Add(Visit visit);
}
