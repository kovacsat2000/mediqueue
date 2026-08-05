using MediQueue.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediQueue.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Patient"/>, including its two value objects.</summary>
/// <remarks>
/// Both value objects wrap a single string, so each becomes one column through a
/// value converter. Owned or complex types earn their keep for multi-field value
/// objects; here they would add owned-entity identity semantics and backing-field
/// configuration for nothing. A converter leaves the domain literally untouched,
/// which keeps "the domain does not know EF Core exists" a fact rather than a
/// slogan.
/// </remarks>
internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.HasKey(patient => patient.Id);

        // The column holds the canonical nine digits; the read direction goes
        // back through the validating factory, so data that was valid on write
        // must still be valid on read. Under an audit requirement, throwing on
        // corrupt data is the correct failure mode — silence would be worse.
        builder.Property(patient => patient.Taj)
            .HasConversion(taj => taj.Digits, digits => FromStoredDigits(digits))
            .HasColumnType("char(9)")
            .IsRequired();

        builder.Property(patient => patient.FullName)
            .HasConversion(name => name.Value, value => PatientName.Create(value))
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(patient => patient.Address)
            .HasMaxLength(Patient.MaxAddressLength)
            .IsRequired();

        // TAJ is the natural key of a person. The unique index is what makes a
        // returning patient reuse their record instead of becoming a second one.
        builder.HasIndex(patient => patient.Taj).IsUnique();
    }

    /// <summary>
    /// Rebuilds the dashed form the factory accepts from the bare digits the
    /// column stores.
    /// </summary>
    /// <remarks>
    /// A separate method rather than an inline lambda because range operators
    /// are not permitted inside an expression tree.
    /// </remarks>
    private static TajNumber FromStoredDigits(string digits) =>
        TajNumber.Create($"{digits[..3]}-{digits[3..6]}-{digits[6..]}");
}
