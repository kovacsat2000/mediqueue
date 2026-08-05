using MediQueue.Application.Abstractions;
using MediQueue.Contracts.Directory;
using MediQueue.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Directory;

/// <summary>Reads specialties straight out of the database.</summary>
public sealed class SpecialtyDirectory(MediQueueDbContext database) : ISpecialtyDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SpecialtyDto>> ListAsync(CancellationToken cancellationToken) =>
        await database.Specialties
            .OrderBy(specialty => specialty.Name)
            .Select(specialty => new SpecialtyDto(specialty.Id, specialty.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
