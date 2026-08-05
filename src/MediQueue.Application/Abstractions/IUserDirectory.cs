using MediQueue.Contracts.Directory;
using MediQueue.Domain.Users;

namespace MediQueue.Application.Abstractions;

/// <summary>Reads users out of storage.</summary>
/// <remarks>
/// Deliberately not a repository. It is a narrow, read-only lookup shaped by
/// what authentication and the directory endpoints actually need, and it exists
/// because the application layer may not reference a <c>DbContext</c>. Whether
/// anything broader sits in front of persistence is a P4 decision.
/// </remarks>
public interface IUserDirectory
{
    /// <summary>Finds a user by the name they sign in with.</summary>
    /// <param name="username">The username, matched exactly.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user, or <c>null</c> if there is no such username.</returns>
    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Finds a user by identifier.</summary>
    /// <param name="userId">The identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user, or <c>null</c> if there is no such user.</returns>
    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Lists the doctors a visit could be routed to, ordered by name.</summary>
    /// <param name="specialtyId">Restricts the list to one specialty, or <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Active doctors only.</returns>
    Task<IReadOnlyList<DoctorDto>> ListActiveDoctorsAsync(Guid? specialtyId, CancellationToken cancellationToken);
}
