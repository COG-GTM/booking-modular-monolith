namespace Unit.Test.Passenger.ValueObjects;

using FluentAssertions;
using global::Passenger.Passengers.Exceptions;
using global::Passenger.Passengers.ValueObjects;
using Xunit;

public class PassportNumberTests
{
    [Theory]
    [InlineData("123456789")]
    [InlineData("AB-1234")]
    public void of_should_wrap_non_empty_value(string value)
    {
        // Act
        var passportNumber = PassportNumber.Of(value);

        // Assert
        passportNumber.Value.Should().Be(value);
        ((string)passportNumber).Should().Be(value);
        passportNumber.ToString().Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void of_should_throw_invalid_passport_number_exception_when_value_is_null_or_whitespace(string? value)
    {
        // Act
        var act = () => PassportNumber.Of(value!);

        // Assert
        act.Should().Throw<InvalidPassportNumberException>();
    }
}
