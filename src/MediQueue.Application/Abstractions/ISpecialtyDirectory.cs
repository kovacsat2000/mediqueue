using MediQueue.Contracts.Directory;

namespace MediQueue.Application.Abstractions;

/// <summary>Reads specialties out of storage.</summary>
public interface ISpecialtyDirectory
{
    /// <summary>Lists every specialty, ordered by name.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All specialties.</returns>
    Task<IReadOnlyList<SpecialtyDto>> ListAsync(CancellationToken cancellationToken);
}
