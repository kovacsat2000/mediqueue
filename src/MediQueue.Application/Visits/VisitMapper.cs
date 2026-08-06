using MediQueue.Contracts.Visits;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Users;
using DomainVisit = MediQueue.Domain.Visits.Visit;
using DomainVisitStatus = MediQueue.Domain.Visits.VisitStatus;
using WireVisitStatus = MediQueue.Contracts.Visits.VisitStatus;

namespace MediQueue.Application.Visits;

/// <summary>
/// Turns a visit into the projection a given role is allowed to see.
/// </summary>
/// <remarks>
/// <para>
/// Both projections are written out by hand and live in this layer, not in
/// persistence. These types are a security boundary: which fields a role may
/// see is the single most important rule in the system, and a boundary should
/// be readable in one file rather than inferred from a convention.
/// </para>
/// <para>
/// The names travel with the visit because a client rendering a queue would
/// otherwise need a second call and a join of its own.
/// </para>
/// </remarks>
public static class VisitMapper
{
    /// <summary>Projects a visit for an assistant. Cannot carry a diagnosis.</summary>
    /// <param name="visit">The visit.</param>
    /// <param name="patient">Its patient.</param>
    /// <param name="specialtyName">The specialty's name, if assigned.</param>
    /// <param name="doctorName">The doctor's name, if assigned.</param>
    /// <returns>The summary projection.</returns>
    public static VisitSummaryDto ToSummary(
        this DomainVisit visit,
        Patient patient,
        string? specialtyName,
        string? doctorName)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(patient);

        return new VisitSummaryDto(
            visit.Id,
            visit.PatientId,
            patient.FullName.Value,
            patient.Taj.ToString(),
            visit.Complaint,
            visit.SpecialtyId,
            specialtyName,
            visit.DoctorId,
            doctorName,
            visit.Status.ToWire(),
            visit.RegisteredAt,
            visit.QueuedAt,
            visit.CalledInAt,
            visit.CompletedAt);
    }

    /// <summary>Projects a visit for the doctor treating it.</summary>
    /// <param name="visit">The visit.</param>
    /// <param name="patient">Its patient.</param>
    /// <param name="specialtyName">The specialty's name, if assigned.</param>
    /// <param name="doctorName">The doctor's name, if assigned.</param>
    /// <returns>The detail projection, including the diagnosis.</returns>
    public static VisitDetailDto ToDetail(
        this DomainVisit visit,
        Patient patient,
        string? specialtyName,
        string? doctorName)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(patient);

        return new VisitDetailDto(
            visit.Id,
            visit.PatientId,
            patient.FullName.Value,
            patient.Taj.ToString(),
            patient.Address,
            visit.Complaint,
            visit.SpecialtyId,
            specialtyName,
            visit.DoctorId,
            doctorName,
            visit.Status.ToWire(),
            visit.Diagnosis,
            visit.RegisteredAt,
            visit.QueuedAt,
            visit.CalledInAt,
            visit.CompletedAt);
    }

    /// <summary>Maps the domain status onto the wire status.</summary>
    /// <remarks>
    /// A switch rather than a cast, so adding a state to the domain is a compile
    /// error here instead of a number quietly reinterpreted on the wire.
    /// </remarks>
    /// <param name="status">The domain status.</param>
    /// <returns>The wire status.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The status is not one this system knows.</exception>
    public static WireVisitStatus ToWire(this DomainVisitStatus status) => status switch
    {
        DomainVisitStatus.Registered => WireVisitStatus.Registered,
        DomainVisitStatus.Waiting => WireVisitStatus.Waiting,
        DomainVisitStatus.InTreatment => WireVisitStatus.InTreatment,
        DomainVisitStatus.Done => WireVisitStatus.Done,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown visit status."),
    };

    /// <summary>Finds a name in a lookup, tolerating an id that is not there.</summary>
    /// <param name="names">The lookup.</param>
    /// <param name="id">The identifier, possibly null.</param>
    /// <returns>The name, or <c>null</c>.</returns>
    public static string? NameOf(this IReadOnlyDictionary<Guid, string> names, Guid? id)
    {
        ArgumentNullException.ThrowIfNull(names);

        return id is { } key && names.TryGetValue(key, out var name) ? name : null;
    }

    /// <summary>Builds an id-to-name lookup from a set of users.</summary>
    /// <param name="users">The users.</param>
    /// <returns>The lookup.</returns>
    public static IReadOnlyDictionary<Guid, string> ToNameLookup(this IEnumerable<User> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        return users.ToDictionary(user => user.Id, user => user.FullName);
    }
}
