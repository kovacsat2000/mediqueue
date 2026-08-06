using MediQueue.Application.Abstractions;
using MediQueue.Domain.Patients;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>Patients, straight out of the database.</summary>
public sealed class PatientRepository(MediQueueDbContext database) : IPatientRepository
{
    /// <inheritdoc />
    public Task<Patient?> FindByTajAsync(TajNumber taj, CancellationToken cancellationToken) =>
        // Compared as a whole value: the converter turns both sides into the
        // stored nine digits. Reaching for .Digits here does not translate,
        // because to EF this is one column and not a string with a property.
        database.Patients.SingleOrDefaultAsync(patient => patient.Taj == taj, cancellationToken);

    /// <inheritdoc />
    public Task<Patient?> FindByIdAsync(Guid patientId, CancellationToken cancellationToken) =>
        database.Patients.SingleOrDefaultAsync(patient => patient.Id == patientId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, Patient>> GetByIdsAsync(
        IReadOnlyCollection<Guid> patientIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patientIds);

        if (patientIds.Count == 0)
        {
            return new Dictionary<Guid, Patient>();
        }

        return await database.Patients
            .Where(patient => patientIds.Contains(patient.Id))
            .ToDictionaryAsync(patient => patient.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(Patient patient) => database.Patients.Add(patient);
}
