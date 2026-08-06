using MediQueue.Application.Abstractions;
using MediQueue.Application.Exceptions;
using MediQueue.Contracts.Visits;
using MediQueue.Domain.Visits;

namespace MediQueue.Application.Visits;

/// <summary>
/// What a doctor does with a visit once it reaches their queue, and the
/// assistant's ability to withdraw one.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The ownership rule lives here.</strong> <c>[Authorize(Policy =
/// "DoctorOnly")]</c> on the endpoint is the coarse layer — it establishes that
/// the caller is a doctor at all. This is the fine one: that they are
/// <em>this</em> visit's doctor.
/// </para>
/// <para>
/// It is in the application service rather than the controller because it is
/// business logic — it is the entire content of the specification's doctor role
/// — and because a rule enforced here cannot be forgotten by a new endpoint,
/// can be unit-tested with every collaborator substituted, and does not need
/// the visit loaded twice.
/// </para>
/// </remarks>
public sealed class VisitLifecycleService(
    IVisitRepository visits,
    IUnitOfWork unitOfWork,
    VisitContextLoader context,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    /// <summary>Calls the patient in from the waiting list.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The visit as its doctor sees it.</returns>
    /// <exception cref="NotFoundException">There is no such visit.</exception>
    /// <exception cref="ForbiddenException">The visit belongs to another doctor.</exception>
    /// <exception cref="InvalidVisitTransitionException">The visit is not waiting.</exception>
    public Task<VisitDetailDto> CallInAsync(Guid visitId, CancellationToken cancellationToken) =>
        ActOnOwnVisitAsync(visitId, visit => visit.CallIn(timeProvider.GetUtcNow()), cancellationToken);

    /// <summary>Records what the doctor found.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="diagnosis">The finding.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The visit as its doctor sees it.</returns>
    /// <exception cref="NotFoundException">There is no such visit.</exception>
    /// <exception cref="ForbiddenException">The visit belongs to another doctor.</exception>
    public Task<VisitDetailDto> RecordDiagnosisAsync(
        Guid visitId,
        string diagnosis,
        CancellationToken cancellationToken) =>
        ActOnOwnVisitAsync(visitId, visit => visit.RecordDiagnosis(diagnosis), cancellationToken);

    /// <summary>Releases the patient and completes the visit.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The visit as its doctor sees it.</returns>
    /// <exception cref="NotFoundException">There is no such visit.</exception>
    /// <exception cref="ForbiddenException">The visit belongs to another doctor.</exception>
    /// <exception cref="InvalidVisitTransitionException">The visit is not in treatment.</exception>
    public Task<VisitDetailDto> ReleaseAsync(Guid visitId, CancellationToken cancellationToken) =>
        ActOnOwnVisitAsync(visitId, visit => visit.Release(timeProvider.GetUtcNow()), cancellationToken);

    /// <summary>Withdraws a visit. Assistant-only, and a logical delete.</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="NotFoundException">There is no such visit, or it is already deleted.</exception>
    public async Task SoftDeleteAsync(Guid visitId, CancellationToken cancellationToken)
    {
        var visit = await visits.GetByIdAsync(visitId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Visit '{visitId}' was not found.");

        // The acting user comes from the token, never from the request body.
        visit.SoftDelete(
            currentUser.UserId ?? throw new ForbiddenException("The request carries no user identity."),
            timeProvider.GetUtcNow());

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads a visit, checks the caller owns it, acts, commits and projects.</summary>
    /// <remarks>
    /// The three doctor actions differ only in the domain call they make, so the
    /// ownership check is written once. Three copies of a security rule is three
    /// places for it to be edited out of one of them.
    /// </remarks>
    private async Task<VisitDetailDto> ActOnOwnVisitAsync(
        Guid visitId,
        Action<Visit> act,
        CancellationToken cancellationToken)
    {
        var visit = await visits.GetByIdAsync(visitId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Visit '{visitId}' was not found.");

        EnsureCallerOwns(visit);

        // Before the domain call, so a refused action cannot have changed
        // anything: the visit is only mutated once the caller is known to own it.
        act(visit);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var (patient, specialtyName, doctorName) =
            await context.LoadAsync(visit, cancellationToken).ConfigureAwait(false);

        return visit.ToDetail(patient, specialtyName, doctorName);
    }

    private void EnsureCallerOwns(Visit visit)
    {
        if (currentUser.UserId is not { } callerId || visit.DoctorId != callerId)
        {
            // The message says nothing about whose visit it is. Confirming that a
            // visit exists and belongs to a named colleague is itself a
            // disclosure, and the caller can do nothing with the answer.
            throw new ForbiddenException("This visit is not in your queue.");
        }
    }
}
