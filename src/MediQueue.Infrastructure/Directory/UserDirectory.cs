using MediQueue.Application.Abstractions;
using MediQueue.Contracts.Directory;
using MediQueue.Domain.Users;
using MediQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Directory;

/// <summary>Reads users straight out of the database.</summary>
public sealed class UserDirectory(MediQueueDbContext database) : IUserDirectory
{
    /// <inheritdoc />
    public Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
        database.Users.SingleOrDefaultAsync(user => user.Username == username, cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        database.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DoctorDto>> ListActiveDoctorsAsync(
        Guid? specialtyId,
        CancellationToken cancellationToken)
    {
        // Projected in SQL and joined to the specialty, so a client rendering the
        // list needs one round trip rather than two plus a join of its own.
        var doctors = database.Users
            .Where(user => user.Role == UserRole.Doctor && user.IsActive);

        if (specialtyId is { } wanted)
        {
            doctors = doctors.Where(user => user.SpecialtyId == wanted);
        }

        // A correlated subquery rather than a Join. The specialty key is nullable
        // on User — assistants share the table — and every Join spelling of that
        // made EF box both key selectors to object and refuse to translate it.
        // This form says the same thing, translates to a single LEFT JOIN, and
        // does not depend on getting nullable key inference exactly right.
        return await doctors
            .Where(user => user.SpecialtyId != null)
            .OrderBy(user => user.FullName)
            .Select(user => new DoctorDto(
                user.Id,
                user.FullName,
                user.SpecialtyId!.Value,
                database.Specialties
                    .Where(specialty => specialty.Id == user.SpecialtyId)
                    .Select(specialty => specialty.Name)
                    .First()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
