using FluentAssertions;
using FluentValidation.TestHelper;
using Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.OutboundPayment.Features.Handlers.SubmitPayment;

public class SubmitPaymentCommandValidatorRulesTests
{
    private readonly SubmitPaymentValidator _validator = new();

    private static Payments.FPS.OutboundPayments.Features.SubmittingPayment.V1.SubmitPayment Valid() =>
        new FakeSubmitPaymentCommand().Generate();

    [Fact]
    public void valid_command_has_no_errors()
    {
        var result = _validator.TestValidate(Valid());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(10.001)]
    public void amount_must_be_positive_with_at_most_two_decimals(decimal amount)
    {
        var result = _validator.TestValidate(Valid() with { Amount = amount });

        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(10.5)]
    [InlineData(1000)]
    public void amount_with_two_or_fewer_decimals_is_valid(decimal amount)
    {
        var result = _validator.TestValidate(Valid() with { Amount = amount });

        result.ShouldNotHaveValidationErrorFor(x => x.Amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12-34-56")]
    [InlineData("12345a")]
    public void sort_codes_must_be_exactly_six_digits(string sortCode)
    {
        var result = _validator.TestValidate(Valid() with { DebtorSortCode = sortCode, CreditorSortCode = sortCode });

        result.ShouldHaveValidationErrorFor(x => x.DebtorSortCode);
        result.ShouldHaveValidationErrorFor(x => x.CreditorSortCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("1234567a")]
    public void account_numbers_must_be_exactly_eight_digits(string accountNumber)
    {
        var result = _validator.TestValidate(
            Valid() with
            {
                DebtorAccountNumber = accountNumber,
                CreditorAccountNumber = accountNumber,
            }
        );

        result.ShouldHaveValidationErrorFor(x => x.DebtorAccountNumber);
        result.ShouldHaveValidationErrorFor(x => x.CreditorAccountNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void reference_must_not_be_empty(string reference)
    {
        var result = _validator.TestValidate(Valid() with { Reference = reference });

        result.ShouldHaveValidationErrorFor(x => x.Reference);
    }

    [Fact]
    public void reference_must_not_exceed_35_characters()
    {
        var tooLong = _validator.TestValidate(Valid() with { Reference = new string('A', 36) });
        var maxLength = _validator.TestValidate(Valid() with { Reference = new string('A', 35) });

        tooLong.ShouldHaveValidationErrorFor(x => x.Reference);
        maxLength.ShouldNotHaveValidationErrorFor(x => x.Reference);
    }

    [Fact]
    public void creditor_fields_are_validated_independently_of_debtor_fields()
    {
        var result = _validator.TestValidate(Valid() with { CreditorSortCode = "12", CreditorAccountNumber = "abc" });

        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(x => x.CreditorSortCode);
        result.ShouldHaveValidationErrorFor(x => x.CreditorAccountNumber);
        result.ShouldNotHaveValidationErrorFor(x => x.DebtorSortCode);
        result.ShouldNotHaveValidationErrorFor(x => x.DebtorAccountNumber);
    }
}
