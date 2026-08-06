using MediQueue.Domain.Auditing;
using MediQueue.Domain.Exceptions;

namespace MediQueue.Domain.Tests.Auditing;

/// <summary>The audit entities' own rules, with no interceptor and no database.</summary>
public class AuditEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid EntityId = Guid.CreateVersion7(Now);

    private static AuditEntry AnEntry(string entityType = "Visit", Guid? entityId = null) =>
        AuditEntry.For(AuditAction.Update, entityType, entityId ?? EntityId, null, null, Now);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_entry_must_say_what_kind_of_record_it_describes(string? entityType)
    {
        Should.Throw<ValidationException>(() => AnEntry(entityType!));
    }

    [Fact]
    public void An_entry_must_name_the_record_it_describes()
    {
        // A default id is indistinguishable from an id that was never written,
        // and an entry that names no record is evidence of nothing.
        Should.Throw<DomainException>(() => AnEntry(entityId: Guid.Empty));
    }

    [Fact]
    public void An_entity_type_longer_than_the_column_is_rejected()
    {
        Should.Throw<ValidationException>(
            () => AnEntry(new string('a', AuditEntry.MaxEntityTypeLength + 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_change_must_name_the_field_that_moved(string? fieldName)
    {
        Should.Throw<ValidationException>(
            () => AuditFieldChange.Record(fieldName!, "before", "after", isSensitive: false, Now));
    }

    [Fact]
    public void A_field_name_longer_than_the_column_is_rejected()
    {
        Should.Throw<ValidationException>(() => AuditFieldChange.Record(
            new string('a', AuditFieldChange.MaxFieldNameLength + 1),
            "before",
            "after",
            isSensitive: false,
            Now));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sensitivity_survives_being_attached_to_an_entry(bool isSensitive)
    {
        // The flag is what carries the redaction rule from the property's
        // attribute all the way to the reader. If it were lost on the way in,
        // every diagnosis in the log would be readable by an assistant.
        var entry = AnEntry();
        entry.Add(AuditFieldChange.Record("Diagnosis", null, "Migrén", isSensitive, Now));

        entry.Changes.ShouldHaveSingleItem().IsSensitive.ShouldBe(isSensitive);
    }

    [Fact]
    public void A_change_belongs_to_the_entry_it_was_added_to()
    {
        var entry = AnEntry();
        var change = AuditFieldChange.Record("Status", "Waiting", "InTreatment", isSensitive: false, Now);

        change.AuditEntryId.ShouldBe(Guid.Empty);
        entry.Add(change);

        change.AuditEntryId.ShouldBe(entry.Id);
    }

    [Fact]
    public void One_action_touching_two_fields_is_one_entry_with_two_changes()
    {
        // Not two entries: it was one action. This is the shape the interceptor
        // must produce, and the shape the query returns.
        var entry = AnEntry();
        entry.Add(AuditFieldChange.Record("Status", "Waiting", "InTreatment", isSensitive: false, Now));
        entry.Add(AuditFieldChange.Record("CalledInAt", null, "09:41", isSensitive: false, Now));

        entry.Changes.Count.ShouldBe(2);
        entry.Changes.Select(change => change.AuditEntryId).ShouldAllBe(id => id == entry.Id);
    }

    [Fact]
    public void An_entry_with_no_actor_is_still_a_valid_entry()
    {
        // The nullable actor is a deliberate risk accepted: an entry with no
        // actor is worth more than no entry at all, because the interceptor
        // shouts about it and a missing entry is silent.
        var entry = AuditEntry.For(AuditAction.Create, "Patient", EntityId, null, null, Now);

        entry.UserId.ShouldBeNull();
        entry.OccurredAt.ShouldBe(Now);
    }

    [Fact]
    public void The_entry_id_is_derived_from_the_time_it_occurred()
    {
        // Version-7 ids are time-ordered, so the audit table's primary key index
        // stays dense as entries are appended, and ordering by id agrees with
        // ordering by OccurredAt.
        var earlier = AuditEntry.For(AuditAction.Create, "Visit", EntityId, null, null, Now);
        var later = AuditEntry.For(AuditAction.Create, "Visit", EntityId, null, null, Now.AddMinutes(1));

        earlier.Id.CompareTo(later.Id).ShouldBeLessThan(0);
    }

    [Fact]
    public void Adding_nothing_is_a_programmer_error_rather_than_a_domain_one()
    {
        Should.Throw<ArgumentNullException>(() => AnEntry().Add(null!));
    }
}
