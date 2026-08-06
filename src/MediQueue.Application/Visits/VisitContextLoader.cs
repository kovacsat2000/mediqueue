using MediQueue.Application.Abstractions;
using MediQueue.Application.Exceptions;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Visits;

namespace MediQueue.Application.Visits;

/// <summary>
/// Gathers the names a visit projection needs but the visit itself does not
/// hold.
/// </summary>
/// <remarks>
/// A visit stores a specialty id and a doctor id; the wire types carry their
/// names, so a client rendering a queue needs one call rather than three. This
/// exists so that gathering is written once rather than in each of the four
/// services that project a visit.
/// </remarks>
public sealed class VisitContextLoader(
    IPatientRepository patients,
    ISpecialtyDirectory specialties,
    IDoctorDirectory doctors)
{
    /// <summary>The names surrounding one visit.</summary>
    /// <param name="Patient">Its patient.</param>
    /// <param name="SpecialtyName">The specialty's name, if assigned.</param>
    /// <param name="DoctorName">The doctor's name, if assigned.</param>
    public sealed record VisitContext(Patient Patient, string? SpecialtyName, string? DoctorName);

    /// <summary>Loads the names for one visit.</summary>
    /// <param name="visit">The visit.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The surrounding names.</returns>
    /// <exception cref="NotFoundException">The visit's patient is missing, which would be a broken foreign key.</exception>
    public async Task<VisitContext> LoadAsync(Visit visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        var patient = await patients.FindByIdAsync(visit.PatientId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Patient '{visit.PatientId}' was not found.");

        var specialtyNames = await SpecialtyNamesAsync(cancellationToken).ConfigureAwait(false);
        var doctorNames = (await doctors.GetActiveAsync(null, cancellationToken).ConfigureAwait(false))
            .ToNameLookup();

        return new VisitContext(
            patient,
            specialtyNames.NameOf(visit.SpecialtyId),
            doctorNames.NameOf(visit.DoctorId));
    }

    /// <summary>
    /// Loads only the surrounding names, for a caller that already holds the
    /// patient.
    /// </summary>
    /// <remarks>
    /// Registration has just created or found the patient, so reloading it would
    /// be a query for something already in hand — and, in a test with a
    /// substituted repository, a lookup for a row that was never written.
    /// </remarks>
    /// <param name="visit">The visit.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The specialty and doctor names, if assigned.</returns>
    public async Task<(string? SpecialtyName, string? DoctorName)> LoadNamesAsync(
        Visit visit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        var specialtyNames = await SpecialtyNamesAsync(cancellationToken).ConfigureAwait(false);
        var doctorNames = (await doctors.GetActiveAsync(null, cancellationToken).ConfigureAwait(false))
            .ToNameLookup();

        return (specialtyNames.NameOf(visit.SpecialtyId), doctorNames.NameOf(visit.DoctorId));
    }

    /// <summary>Loads the lookups needed to project many visits at once.</summary>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>Specialty and doctor names, keyed by identifier.</returns>
    public async Task<(IReadOnlyDictionary<Guid, string> Specialties, IReadOnlyDictionary<Guid, string> Doctors)>
        LoadLookupsAsync(CancellationToken cancellationToken) =>
        (await SpecialtyNamesAsync(cancellationToken).ConfigureAwait(false),
         (await doctors.GetActiveAsync(null, cancellationToken).ConfigureAwait(false)).ToNameLookup());

    private async Task<IReadOnlyDictionary<Guid, string>> SpecialtyNamesAsync(CancellationToken cancellationToken) =>
        (await specialties.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(specialty => specialty.Id, specialty => specialty.Name);
}
