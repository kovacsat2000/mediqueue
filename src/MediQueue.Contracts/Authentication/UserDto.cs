namespace MediQueue.Contracts.Authentication;

/// <summary>A signed-in user, as the clients see them.</summary>
/// <param name="Id">The user's identifier.</param>
/// <param name="Username">The name they signed in with.</param>
/// <param name="FullName">The name to display.</param>
/// <param name="Role">Which of the two roles they hold.</param>
/// <param name="SpecialtyId">The doctor's specialty; always <c>null</c> for an assistant.</param>
public sealed record UserDto(Guid Id, string Username, string FullName, UserRole Role, Guid? SpecialtyId);
