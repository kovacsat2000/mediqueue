using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Patients;

namespace MediQueue.Domain.Tests.Patients;

public class PatientNameTests
{
    [Theory]
    [InlineData("Kovács Anna")]
    [InlineData("Dr. Kovács-Nagy Anna")]   // full stop and hyphen
    [InlineData("O'Brien Seán")]           // apostrophe and a diacritic
    [InlineData("Nagy")]                   // a mononym is deliberately accepted
    public void Accepts_a_real_name(string input)
    {
        var name = PatientName.Create(input);

        name.Value.ShouldBe(input);
    }

    [Theory]
    [InlineData("Nagy  Péter", "Nagy Péter")]        // internal run collapses
    [InlineData("  Kovács Anna  ", "Kovács Anna")]   // ends are trimmed
    [InlineData("Nagy\tPéter", "Nagy Péter")]        // tabs count as whitespace
    public void Trims_the_ends_and_collapses_internal_whitespace(string input, string expected)
    {
        PatientName.Create(input).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("A")]          // shorter than two characters
    [InlineData("Kovács2")]    // contains a digit
    [InlineData("123")]        // all digits
    [InlineData("@@@")]        // no letter, disallowed characters
    [InlineData("Nagy_Péter")] // underscore is not an allowed separator
    public void Rejects_anything_that_is_not_a_usable_name(string? input)
    {
        PatientName.TryCreate(input, out var result, out var error).ShouldBeFalse();

        result.ShouldBeNull();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Reports_digits_specifically_rather_than_generically()
    {
        PatientName.TryCreate("Kovács2", out _, out var error).ShouldBeFalse();

        error.ShouldBe("Name must not contain digits.");
    }

    [Fact]
    public void Create_throws_a_validation_exception_naming_the_field()
    {
        var exception = Should.Throw<ValidationException>(() => PatientName.Create("123"));

        exception.FieldName.ShouldBe(nameof(PatientName));
        exception.ShouldBeAssignableTo<DomainException>();
    }

    [Fact]
    public void Two_names_that_normalise_to_the_same_text_are_equal()
    {
        PatientName.Create("Nagy  Péter").ShouldBe(PatientName.Create("  Nagy Péter "));
    }

    [Fact]
    public void ToString_renders_the_normalised_name()
    {
        PatientName.Create("  Nagy   Péter ").ToString().ShouldBe("Nagy Péter");
    }
}
