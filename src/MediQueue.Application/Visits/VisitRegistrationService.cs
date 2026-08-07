using MediQueue.Application.Abstractions;
using MediQueue.Application.Exceptions;
using MediQueue.Contracts.Visits;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Visits;

namespace MediQueue.Application.Visits;

/// <summary>
/// Registers a patient's arrival and opens a visit for them.
/// </summary>
public sealed class VisitRegistrationService(
    IPatientRepository patients,
    IVisitRepository visits,
    IUnitOfWork unitOfWork,
    VisitAssignmentService assignment,
    VisitContextLoader context,
    VisitAnnouncer announcer,
    TimeProvider timeProvider)
{
    /// <summary>Registers an arrival, optionally routing it straight to a queue.</summary>
    /// <param name="request">The patient's details and their complaint.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new visit as an assistant sees it.</returns>
    /// <exception cref="Domain.Exceptions.ValidationException">A field failed the domain's rules.</exception>
    /// <exception cref="ConflictException">The patient already has an unfinished visit, or the specialty has no active doctor.</exception>
    public async Task<VisitSummaryDto> RegisterAsync(
        RegisterVisitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The domain owns these rules. Re-stating them here as annotations would
        // give two definitions of a valid name that could disagree.
        var fullName = PatientName.Create(request.FullName);
        var taj = TajNumber.Create(request.Taj);

        var now = timeProvider.GetUtcNow();

        var patient = await patients.FindByTajAsync(taj, cancellationToken).ConfigureAwait(false);

        if (patient is null)
        {
            patient = Patient.Create(fullName, request.Address, taj, now);
            patients.Add(patient);
        }
        else if (await visits.HasOpenVisitAsync(patient.Id, cancellationToken).ConfigureAwait(false))
        {
            // A person cannot be in two queues at once. Only a returning patient
            // can hit this, which is why the check sits inside this branch.
            throw new ConflictException(
                $"Patient '{patient.FullName.Value}' already has a visit in progress.");
        }

        // An existing patient's record is reused unchanged. A registration form
        // is not the place to silently correct someone's name or address: the
        // TAJ number identified them, and overwriting details from a hurried
        // retype is how records quietly rot.
        var visit = Visit.Register(patient.Id, request.Complaint, now);
        visits.Add(visit);

        if (request.SpecialtyId is { } specialtyId)
        {
            await assignment.AssignAsync(visit, specialtyId, cancellationToken).ConfigureAwait(false);
        }

        // One commit. The patient and the visit are one business action, and from
        // P5 that is also one audit boundary.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The patient is already in hand, so only the names are fetched.
        var (specialtyName, doctorName) =
            await context.LoadNamesAsync(visit, cancellationToken).ConfigureAwait(false);

        var summary = visit.ToSummary(patient, specialtyName, doctorName);

        // After the commit. The announcer decides whether this arrival was
        // routed or is still waiting for a specialty.
        await announcer.RegisteredAsync(summary, cancellationToken).ConfigureAwait(false);

        return summary;
    }
}
