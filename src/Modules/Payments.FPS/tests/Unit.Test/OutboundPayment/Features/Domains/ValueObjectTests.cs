using FluentAssertions;
using Payments.FPS.OutboundPayments.Exceptions;
using Payments.FPS.OutboundPayments.ValueObjects;
using Xunit;

namespace Unit.Test.OutboundPayment.Features.Domains;

public class ValueObjectTests
{
    [Theory, InlineData(0), InlineData(-1), InlineData(10.005)]
    public void invalid_amount_throws(decimal value)
    {
        var act = () => Amount.Of(value);
        act.Should().Throw<InvalidAmountException>();
    }

    [Fact]
    public void valid_amount_is_gbp()
    {
        var x = Amount.Of(10.50m);
        x.Currency.Should().Be("GBP");
    }

    [Theory, InlineData("12345", "12345678"), InlineData("123456", "1234567"), InlineData("12-34-56", "12345678")]
    public void invalid_account_throws(string sort, string account)
    {
        var act = () => UkAccount.Of(sort, account);
        act.Should().Throw<InvalidUkAccountException>();
    }

    [Fact]
    public void valid_account_is_created() => UkAccount.Of("123456", "12345678").Should().NotBeNull();
}
