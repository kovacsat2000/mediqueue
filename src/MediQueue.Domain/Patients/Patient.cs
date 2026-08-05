using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Validation;

namespace MediQueue.Domain.Patients;

/// <summary>
/// A person the practice treats, identified by their TAJ number.
/// </summary>
/// <remarks>
/// A patient is a <em>person</em>, deliberately separate from the visits they
/// make. The TAJ number is the natural key of a human being, whereas the state
/// machine describes one episode of care — so a returning patient is a second
/// visit against the same patient, and "done" is never a permanent property of
/// a person.
/// </remarks>
public sealed class Patient
{
    /// <summary>The longest address the system accepts. The database column is sized from this.</summary>
    public const int MaxAddressLength = 300;

    private Patient(Guid id, PatientName fullName, string address, TajNumber taj, DateTimeOffset createdAt)
    {
        Id = id;
        FullName = fullName;
        Address = address;
        Taj = taj;
        CreatedAt = createdAt;
    }

    /// <summary>The identifier. Time-ordered, so index pages stay dense as rows are inserted.</summary>
    public Guid Id { get; private set; }

    /// <summary>The patient's name, already validated and normalised.</summary>
    public PatientName FullName { get; private set; }

    /// <summary>Where the patient lives.</summary>
    public string Address { get; private set; }

    /// <summary>The patient's TAJ number. Unique across the practice.</summary>
    public TajNumber Taj { get; private set; }

    /// <summary>When the patient was first recorded.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a person the practice has not seen before.</summary>
    /// <param name="fullName">The patient's name.</param>
    /// <param name="address">Where the patient lives.</param>
    /// <param name="taj">The patient's TAJ number.</param>
    /// <param name="now">The current time, supplied by the caller so the result is deterministic.</param>
    /// <returns>The new patient.</returns>
    /// <exception cref="ValidationException"><paramref name="address"/> is blank or too long.</exception>
    public static Patient Create(PatientName fullName, string address, TajNumber taj, DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(now),
            fullName,
            TextRules.Required(address, nameof(Address), MaxAddressLength),
            taj,
            now);
}
