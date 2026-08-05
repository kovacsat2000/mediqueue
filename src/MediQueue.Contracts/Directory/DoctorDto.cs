namespace MediQueue.Contracts.Directory;

/// <summary>A doctor a visit can be routed to.</summary>
/// <remarks>
/// The specialty name travels with the identifier so a client rendering a list
/// of doctors does not have to fetch the specialties separately and join them.
/// </remarks>
/// <param name="Id">The doctor's identifier.</param>
/// <param name="FullName">The name to display.</param>
/// <param name="SpecialtyId">The specialty they practise.</param>
/// <param name="SpecialtyName">The name of that specialty.</param>
public sealed record DoctorDto(Guid Id, string FullName, Guid SpecialtyId, string SpecialtyName);
