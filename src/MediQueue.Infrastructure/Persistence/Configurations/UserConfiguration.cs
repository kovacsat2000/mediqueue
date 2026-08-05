using MediQueue.Domain.Specialties;
using MediQueue.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediQueue.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="User"/>.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Username)
            .HasMaxLength(User.MaxUsernameLength)
            .IsRequired();

        builder.Property(user => user.FullName)
            .HasMaxLength(User.MaxFullNameLength)
            .IsRequired();

        builder.Property(user => user.PasswordHash)
            .IsRequired();

        // Stored as int. The enum's explicit numeric values exist for this:
        // a string would make renaming a member a silent data migration.
        builder.Property(user => user.Role)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(user => user.Username).IsUnique();

        builder.HasOne<Specialty>()
            .WithMany()
            .HasForeignKey(user => user.SpecialtyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
