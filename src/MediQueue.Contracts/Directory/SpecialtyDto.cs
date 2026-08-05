namespace MediQueue.Contracts.Directory;

/// <summary>A field of medicine a patient can be routed to.</summary>
/// <param name="Id">The specialty's identifier.</param>
/// <param name="Name">The name the practice uses.</param>
public sealed record SpecialtyDto(Guid Id, string Name);
