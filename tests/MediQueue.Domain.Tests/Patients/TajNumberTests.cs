using MediQueue.Domain.Exceptions;
using MediQueue.Domain.Patients;

namespace MediQueue.Domain.Tests.Patients;

public class TajNumberTests
{
    // The statutory worked example: 18+49+9+28+15+49+0+7 = 175, and 175 mod 10 = 5,
    // which is its ninth digit. Source: 1996. évi XX. törvény, 2. számú melléklet.
    private const string ChecksumValid = "673-457-015";

    // The customer's own example. Well-formed, but its check digit computes to 7
    // against an actual ninth digit of 3 — which is precisely why the checksum
    // rule ships disabled.
    private const string ChecksumInvalid = "123-123-123";

    [Fact]
    public void Accepts_the_dashed_form_when_the_checksum_rule_is_off()
    {
        var taj = TajNumber.Create(ChecksumInvalid);

        taj.Digits.ShouldBe("123123123");
    }

    [Theory]
    [InlineData("123123123")]      // no separators
    [InlineData("12-123-123")]     // wrong grouping
    [InlineData("123-123-1234")]   // too long
    [InlineData("123-123-12")]     // too short
    [InlineData("abc-123-123")]    // not digits
    [InlineData("123 123 123")]    // wrong separator
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_anything_that_is_not_the_dashed_nine_digit_form(string? input)
    {
        TajNumber.TryCreate(input, out var result, out var error).ShouldBeFalse();

        result.ShouldBeNull();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_throws_a_validation_exception_naming_the_field()
    {
        var exception = Should.Throw<ValidationException>(() => TajNumber.Create("nonsense"));

        exception.FieldName.ShouldBe(nameof(TajNumber));
        exception.ShouldBeAssignableTo<DomainException>();
    }

    [Fact]
    public void Accepts_the_statutory_example_when_the_checksum_rule_is_on()
    {
        var taj = TajNumber.Create(ChecksumValid, validateChecksum: true);

        taj.Digits.ShouldBe("673457015");
    }

    [Fact]
    public void Rejects_a_wrong_check_digit_when_the_checksum_rule_is_on()
    {
        TajNumber.TryCreate(ChecksumInvalid, validateChecksum: true, out var result, out var error)
            .ShouldBeFalse();

        result.ShouldBeNull();
        error.ShouldBe("TAJ number check digit is incorrect.");
    }

    [Fact]
    public void The_checksum_rule_is_off_by_default()
    {
        // The same input, accepted or rejected purely on the flag. This is the
        // switch the customer owns.
        TajNumber.TryCreate(ChecksumInvalid, out _, out _).ShouldBeTrue();
        TajNumber.TryCreate(ChecksumInvalid, validateChecksum: true, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Stores_bare_digits_and_renders_the_dashed_form()
    {
        var taj = TajNumber.Create("123-456-789");

        taj.Digits.ShouldBe("123456789");
        taj.ToString().ShouldBe("123-456-789");
    }

    [Fact]
    public void Two_instances_parsed_from_the_same_input_are_equal()
    {
        var first = TajNumber.Create(ChecksumInvalid);
        var second = TajNumber.Create(ChecksumInvalid);

        first.ShouldBe(second);
        (first == second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Instances_parsed_from_different_inputs_are_not_equal()
    {
        TajNumber.Create("123-123-123").ShouldNotBe(TajNumber.Create("123-456-789"));
    }
}
