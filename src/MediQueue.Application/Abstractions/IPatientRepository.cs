using MediQueue.Domain.Patients;

namespace MediQueue.Application.Abstractions;

/// <summary>Finds and stores patients.</summary>
public interface IPatientRepository
{
    /// <summary>Finds a patient by TAJ number, which is the natural key of a person.</summary>
    /// <remarks>
    /// Takes the value object rather than a string. The column is value-converted,
    /// so EF cannot translate a comparison against <c>Taj.Digits</c> — it sees one
    /// column of type <see cref="TajNumber"/>, not a string with a property. Taking
    /// the parsed type also means no caller can look a patient up by something that
    /// was never validated.
    /// </remarks>
    /// <param name="taj">The parsed TAJ number.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The patient, or <c>null</c> if the practice has not seen them before.</returns>
    Task<Patient?> FindByTajAsync(TajNumber taj, CancellationToken cancellationToken);

    /// <summary>Loads a patient by identifier.</summary>
    /// <param name="patientId">The patient.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The patient, or <c>null</c>.</returns>
    Task<Patient?> FindByIdAsync(Guid patientId, CancellationToken cancellationToken);

    /// <summary>Stages a new patient. Nothing is written until the unit of work commits.</summary>
    /// <param name="patient">The patient.</param>
    void Add(Patient patient);
}
