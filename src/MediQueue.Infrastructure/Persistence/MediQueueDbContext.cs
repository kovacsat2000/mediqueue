using MediQueue.Domain.Patients;
using MediQueue.Domain.Specialties;
using MediQueue.Domain.Users;
using MediQueue.Domain.Visits;
using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// The unit of work over the MediQueue database.
/// </summary>
/// <remarks>
/// There is deliberately no repository layer in front of this. A repository
/// earns its place when it hides a storage decision or provides a seam worth
/// substituting; here it would forward every call to a <see cref="DbSet{T}"/>
/// that is already an abstraction over the same thing, and the integration
/// tests run against real PostgreSQL rather than a substitute, so there is
/// nothing to fake.
/// </remarks>
public sealed class MediQueueDbContext(DbContextOptions<MediQueueDbContext> options) : DbContext(options)
{
    /// <summary>The fields of medicine patients are routed to.</summary>
    public DbSet<Specialty> Specialties => Set<Specialty>();

    /// <summary>Everyone who can sign in: assistants and doctors.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>The people the practice treats.</summary>
    public DbSet<Patient> Patients => Set<Patient>();

    /// <summary>Episodes of care. Soft-deleted rows are filtered out by default.</summary>
    public DbSet<Visit> Visits => Set<Visit>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediQueueDbContext).Assembly);
}
