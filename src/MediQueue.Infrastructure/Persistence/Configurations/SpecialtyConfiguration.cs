using MediQueue.Domain.Specialties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediQueue.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Specialty"/>.</summary>
internal sealed class SpecialtyConfiguration : IEntityTypeConfiguration<Specialty>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Specialty> builder)
    {
        builder.HasKey(specialty => specialty.Id);

        // Sized from the domain constant rather than restated, so the column and
        // the rule cannot drift apart.
        builder.Property(specialty => specialty.Name)
            .HasMaxLength(Specialty.MaxNameLength)
            .IsRequired();

        builder.HasIndex(specialty => specialty.Name).IsUnique();
    }
}
