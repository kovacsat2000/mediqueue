using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Patients;
using MediQueue.Domain.Specialties;
using MediQueue.Domain.Users;
using MediQueue.Domain.Visits;

namespace MediQueue.Domain.Tests.Validation;

/// <summary>
/// Every required free-text field stands on the same floor: present, trimmed,
/// and within a stated length. The rules differ by trust boundary — reception
/// data gets value objects, provisioned data gets this — but "not blank" is not
/// gold-plating on either side of that line, it is the floor.
/// </summary>
public class RequiredTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientId = Guid.CreateVersion7(Now);
    private static readonly Guid SpecialtyId = Guid.CreateVersion7(Now);

    private static PatientName AName() => PatientName.Create("Kovács Anna");
    private static TajNumber ATaj() => TajNumber.Create("123-123-123");

    /// <summary>Each required field, with the action that sets it and its stated limit.</summary>
    public static TheoryData<string, int> RequiredFields() => new()
    {
        { nameof(Visit.Complaint), Visit.MaxComplaintLength },
        { nameof(Visit.Diagnosis), Visit.MaxDiagnosisLength },
        { nameof(Patient.Address), Patient.MaxAddressLength },
        { nameof(Specialty.Name), Specialty.MaxNameLength },
        { nameof(User.Username), User.MaxUsernameLength },
        { nameof(User.FullName), User.MaxFullNameLength },
    };

    private static void SetField(string field, string value)
    {
        switch (field)
        {
            case nameof(Visit.Complaint):
                Visit.Register(PatientId, value, Now);
                break;
            case nameof(Visit.Diagnosis):
                var visit = Visit.Register(PatientId, "Fejfájás", Now);
                visit.AssignToQueue(SpecialtyId, Guid.CreateVersion7(Now), Now);
                visit.CallIn(Now);
                visit.RecordDiagnosis(value);
                break;
            case nameof(Patient.Address):
                Patient.Create(AName(), value, ATaj(), Now);
                break;
            case nameof(Specialty.Name):
                Specialty.Create(value, Now);
                break;
            case nameof(User.Username):
                User.CreateAssistant(value, "Kovács Anna", "hash", Now);
                break;
            default:
                User.CreateAssistant("kovacs.anna", value, "hash", Now);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void A_required_field_refuses_a_blank_value(string field, int maxLength)
    {
        _ = maxLength;

        foreach (var blank in new[] { "", "   ", "\t", "\n" })
        {
            var exception = Should.Throw<ValidationException>(() => SetField(field, blank));

            exception.FieldName.ShouldBe(field);
            exception.Message.ShouldContain(field);
            exception.ShouldBeAssignableTo<DomainException>();
        }
    }

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void A_required_field_refuses_a_value_over_its_limit(string field, int maxLength)
    {
        var exception = Should.Throw<ValidationException>(() => SetField(field, new string('a', maxLength + 1)));

        exception.FieldName.ShouldBe(field);
        exception.Message.ShouldContain(maxLength.ToString());
    }

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public void A_required_field_accepts_a_value_exactly_at_its_limit(string field, int maxLength)
    {
        // The boundary is inclusive. Off-by-one here would silently reject data
        // the database would have accepted.
        Should.NotThrow(() => SetField(field, new string('a', maxLength)));
    }

    [Fact]
    public void A_complaint_is_stored_trimmed()
    {
        Visit.Register(PatientId, "   Fejfájás   ", Now).Complaint.ShouldBe("Fejfájás");
    }

    [Fact]
    public void A_diagnosis_is_stored_trimmed()
    {
        var visit = Visit.Register(PatientId, "Fejfájás", Now);
        visit.AssignToQueue(SpecialtyId, Guid.CreateVersion7(Now), Now);
        visit.CallIn(Now);

        visit.RecordDiagnosis("  Migrén  ");

        visit.Diagnosis.ShouldBe("Migrén");
    }

    [Fact]
    public void An_address_is_stored_trimmed()
    {
        Patient.Create(AName(), "  Budapest, Fő utca 1.  ", ATaj(), Now)
            .Address.ShouldBe("Budapest, Fő utca 1.");
    }

    [Fact]
    public void A_specialty_name_is_stored_trimmed()
    {
        Specialty.Create("  Belgyógyászat  ", Now).Name.ShouldBe("Belgyógyászat");
    }

    [Fact]
    public void A_user_name_and_username_are_stored_trimmed()
    {
        var user = User.CreateAssistant("  kovacs.anna  ", "  Kovács Anna  ", "hash", Now);

        user.Username.ShouldBe("kovacs.anna");
        user.FullName.ShouldBe("Kovács Anna");
    }

    [Theory]
    [InlineData("kovacs anna")]
    [InlineData("kovacs\tanna")]
    public void A_username_refuses_internal_whitespace(string username)
    {
        // A username is an identifier rather than prose. Full names, which are
        // prose, are deliberately left alone.
        var exception = Should.Throw<ValidationException>(
            () => User.CreateAssistant(username, "Kovács Anna", "hash", Now));

        exception.FieldName.ShouldBe(nameof(User.Username));
    }

    [Fact]
    public void A_full_name_still_allows_spaces()
    {
        User.CreateAssistant("kovacs.anna", "Dr. Kovács-Nagy Anna", "hash", Now)
            .FullName.ShouldBe("Dr. Kovács-Nagy Anna");
    }

    [Fact]
    public void No_further_rule_is_imposed_on_provisioned_or_free_text_fields()
    {
        // Deliberately permissive: these arrive through a controlled path or are
        // a human describing a symptom. Presence and length are the whole rule —
        // no character classes, no format, no digit ban.
        Should.NotThrow(() => Specialty.Create("Fül-orr-gégészet (2. rendelő)", Now));
        Should.NotThrow(() => User.CreateAssistant("k.anna_2", "Kovács Anna 2.", "hash", Now));
        Should.NotThrow(() => Visit.Register(PatientId, "Fáj a fejem 3 napja, 38.2 fok", Now));
        Should.NotThrow(() => Patient.Create(AName(), "1052 Budapest, Váci u. 12/A 3. em.", ATaj(), Now));
    }
}
