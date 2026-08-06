namespace MediQueue.Contracts.Visits;

/// <summary>Registers a patient and opens a visit for them.</summary>
/// <remarks>
/// The patient fields are validated by the domain's own value objects, not by
/// annotations on this type: the rules live in one place and this is the wire
/// format, not a second copy of them.
/// </remarks>
/// <param name="FullName">The patient's name.</param>
/// <param name="Address">Where the patient lives.</param>
/// <param name="Taj">The patient's TAJ number, in the dashed form <c>123-123-123</c>.</param>
/// <param name="Complaint">What the patient came in with.</param>
/// <param name="SpecialtyId">
/// Route the visit straight into a queue. Omit it to leave the visit
/// <see cref="VisitStatus.Registered"/> and choose a specialty later.
/// </param>
public sealed record RegisterVisitRequest(
    string FullName,
    string Address,
    string Taj,
    string Complaint,
    Guid? SpecialtyId);

/// <summary>Routes a registered visit to a specialty. The server picks the doctor.</summary>
/// <param name="SpecialtyId">The specialty to route to.</param>
public sealed record AssignSpecialtyRequest(Guid SpecialtyId);

/// <summary>Records what the doctor found.</summary>
/// <param name="Diagnosis">The finding.</param>
public sealed record RecordDiagnosisRequest(string Diagnosis);
