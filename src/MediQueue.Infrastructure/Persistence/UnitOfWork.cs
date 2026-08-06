using MediQueue.Application.Abstractions;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>Commits the tracked changes.</summary>
/// <remarks>
/// A thin pass-through, deliberately. Its whole job is to let the application
/// layer name the transaction boundary without knowing what a
/// <see cref="MediQueueDbContext"/> is.
/// </remarks>
public sealed class UnitOfWork(MediQueueDbContext database) : IUnitOfWork
{
    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        database.SaveChangesAsync(cancellationToken);
}
