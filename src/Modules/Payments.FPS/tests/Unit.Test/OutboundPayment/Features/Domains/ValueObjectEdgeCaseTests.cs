using FluentAssertions;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.ValueObjects;
using Xunit;

namespace Unit.Test.OutboundPayment.Features.Domains;

public class ValueObjectEdgeCaseTests
{
    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(10.5)]
    [InlineData(10.50)]
    [InlineData(999999.99)]
    public void amount_accepts_positive_values_with_at_most_two_decimals(decimal value)
    {
        var amount = Amount.Of(value);

        amount.Value.Should().Be(value);
        amount.Currency.Should().Be("GBP");
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(-0.01)]
    [InlineData(1.999)]
    public void amount_rejects_negative_or_more_than_two_decimals(decimal value)
    {
        var act = () => Amount.Of(value);

        act.Should()
            .Throw<InvalidAmountException>()
            .WithMessage("Amount must be a positive GBP value with at most 2 decimal places.");
    }

    [Fact]
    public void amount_implicitly_converts_to_decimal()
    {
        decimal value = Amount.Of(42.42m);

        value.Should().Be(42.42m);
    }

    [Fact]
    public void outbound_payment_id_wraps_guid_and_converts_implicitly()
    {
        var guid = Guid.NewGuid();
        var id = OutboundPaymentId.Of(guid);

        id.Value.Should().Be(guid);
        Guid converted = id;
        converted.Should().Be(guid);
    }

    [Fact]
    public void outbound_payment_id_rejects_empty_guid()
    {
        var act = () => OutboundPaymentId.Of(Guid.Empty);

        act.Should()
            .Throw<InvalidOutboundPaymentIdException>()
            .WithMessage($"Outbound payment id {Guid.Empty} is invalid.");
    }

    [Fact]
    public void outbound_payment_ids_with_same_value_are_equal()
    {
        var guid = Guid.NewGuid();

        OutboundPaymentId.Of(guid).Should().Be(OutboundPaymentId.Of(guid));
        OutboundPaymentId.Of(guid).Should().NotBe(OutboundPaymentId.Of(Guid.NewGuid()));
    }

    [Fact]
    public void uk_account_exposes_sort_code_and_account_number()
    {
        var account = UkAccount.Of("040004", "12345678");

        account.SortCode.Should().Be("040004");
        account.AccountNumber.Should().Be("12345678");
    }

    [Fact]
    public void uk_accounts_with_same_values_are_equal()
    {
        UkAccount.Of("040004", "12345678").Should().Be(UkAccount.Of("040004", "12345678"));
        UkAccount.Of("040004", "12345678").Should().NotBe(UkAccount.Of("040004", "87654321"));
    }

    [Theory]
    [InlineData("", "12345678")]
    [InlineData("123456", "")]
    [InlineData("1234567", "12345678")]
    [InlineData("123456", "123456789")]
    [InlineData("12345a", "12345678")]
    [InlineData("123456", "1234567b")]
    [InlineData("１２３４５６", "12345678")]
    [InlineData(" 12345", "12345678")]
    public void uk_account_rejects_non_ascii_digit_or_wrong_length_values(string sortCode, string accountNumber)
    {
        var act = () => UkAccount.Of(sortCode, accountNumber);

        act.Should()
            .Throw<InvalidUkAccountException>()
            .WithMessage("Sort code must be 6 digits and account number 8 digits.");
    }

    [Fact]
    public void uk_account_rejects_null_values()
    {
        var nullSortCode = () => UkAccount.Of(null!, "12345678");
        var nullAccountNumber = () => UkAccount.Of("040004", null!);

        nullSortCode.Should().Throw<InvalidUkAccountException>();
        nullAccountNumber.Should().Throw<InvalidUkAccountException>();
    }
}
