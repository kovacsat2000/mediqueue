namespace MediQueue.Application.Abstractions;

/// <summary>Commits everything the current use case has changed, as one transaction.</summary>
/// <remarks>
/// Explicit, rather than folded into each repository. Registering a patient
/// creates a <c>Patient</c> and a <c>Visit</c> in one business action; if each
/// repository saved for itself that would be two transactions and, from P5, two
/// disjoint audit boundaries. The transaction boundary is a business-visible
/// fact in a system with an audit log, so it gets a name. That it is a thin
/// pass-through is fine — its job is to let this layer say "commit now" without
/// knowing what a <c>DbContext</c> is.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Writes every pending change.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
