using MediQueue.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediQueue.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="AuditEntry"/> and the changes it owns.</summary>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.EntityType)
            .HasMaxLength(AuditEntry.MaxEntityTypeLength)
            .IsRequired();

        builder.Property(entry => entry.Action)
            .HasConversion<int>()
            .IsRequired();

        // The changes are part of the entry, not an independent record: the
        // navigation is through the backing field because the domain exposes
        // the collection read-only, which is what stops anything outside the
        // aggregate adding to it.
        builder.HasMany(entry => entry.Changes)
            .WithOne()
            .HasForeignKey(change => change.AuditEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(AuditEntry.Changes))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // The specification's own filters. Both are descending on OccurredAt
        // because that is the only order the query offers, so the index serves
        // the sort as well as the predicate.
        builder.HasIndex(entry => new { entry.PatientId, entry.OccurredAt })
            .IsDescending(false, true);

        builder.HasIndex(entry => new { entry.UserId, entry.OccurredAt })
            .IsDescending(false, true);

        // Serves the unfiltered first page, which is what the demo opens on.
        builder.HasIndex(entry => entry.OccurredAt)
            .IsDescending(true);

        // Deliberately no foreign keys. An audit entry outlives what it
        // describes: it must survive a patient or a user being removed, or the
        // log would lose exactly the history that made the removal worth
        // recording. EntityId is a reference, not a relationship.
    }
}

/// <summary>Maps <see cref="AuditFieldChange"/>.</summary>
internal sealed class AuditFieldChangeConfiguration : IEntityTypeConfiguration<AuditFieldChange>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditFieldChange> builder)
    {
        // Named explicitly because this is the one entity with no DbSet, and
        // EF's default is then the type name — which would make it the only
        // singular table in a schema of plurals.
        builder.ToTable("AuditFieldChanges");

        builder.HasKey(change => change.Id);

        builder.Property(change => change.FieldName)
            .HasMaxLength(AuditFieldChange.MaxFieldNameLength)
            .IsRequired();

        // Unbounded on purpose. These hold whatever the audited property held,
        // and a truncated audit value is a wrong audit value — the one thing a
        // log of what changed must never be.
        builder.Property(change => change.OldValue);
        builder.Property(change => change.NewValue);

        builder.Property(change => change.IsSensitive).IsRequired();
    }
}
