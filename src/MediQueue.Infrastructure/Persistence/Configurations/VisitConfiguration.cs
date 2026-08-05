using MediQueue.Domain.Patients;
using MediQueue.Domain.Specialties;
using MediQueue.Domain.Users;
using MediQueue.Domain.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediQueue.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Visit"/>.</summary>
internal sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.HasKey(visit => visit.Id);

        builder.Property(visit => visit.Complaint)
            .HasMaxLength(Visit.MaxComplaintLength)
            .IsRequired();

        builder.Property(visit => visit.Diagnosis)
            .HasMaxLength(Visit.MaxDiagnosisLength);

        builder.Property(visit => visit.Status)
            .HasConversion<int>()
            .IsRequired();

        // Two doctor clients, or one impatient double-click, can both read a
        // Waiting visit and both write InTreatment. The state machine cannot see
        // that — each request looks legal on its own. PostgreSQL stamps every row
        // version with the transaction that wrote it, in the system column xmin,
        // so using it as the concurrency token makes the second write fail
        // deterministically at no storage cost and with no field on the domain
        // entity.
        //
        // Npgsql's UseXminAsConcurrencyToken() helper was removed in version 9;
        // this is what it did. ValueGeneratedOnAddOrUpdate is what keeps xmin out
        // of INSERT and UPDATE column lists — the database owns the value.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Defence in depth behind Visit.EnsureNotDeleted. The aggregate refuses
        // to change once deleted; this makes a deleted visit not turn up in the
        // first place. Callers that genuinely need them — the P5 audit query —
        // opt out per query with IgnoreQueryFilters().
        builder.HasQueryFilter(visit => !visit.IsDeleted);

        // Serves the doctor's waiting list: their queue, in arrival order.
        builder.HasIndex(visit => new { visit.DoctorId, visit.Status, visit.QueuedAt });

        // Nothing cascades anywhere in this schema. The system soft-deletes, so
        // a delete that reaches the database at all is a mistake, and it should
        // fail loudly rather than take rows with it.
        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(visit => visit.PatientId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<Specialty>()
            .WithMany()
            .HasForeignKey(visit => visit.SpecialtyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(visit => visit.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
