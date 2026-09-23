namespace Unit.Test.Flight.ValueObjects;

using System;
using FluentAssertions;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.ValueObjects;
using Xunit;

public class PriceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(1500.75)]
    public void of_with_non_negative_value_should_return_price_with_same_value(decimal value)
    {
        // Act
        var price = Price.Of(value);

        // Assert
        price.Value.Should().Be(value);
    }

    [Fact]
    public void implicit_conversion_should_return_underlying_value()
    {
        // Arrange
        var price = Price.Of(250m);

        // Act
        decimal value = price;

        // Assert
        value.Should().Be(250m);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [InlineData(-9999.99)]
    public void of_with_negative_value_should_throw_invalid_price_exception(decimal value)
    {
        // Act
        Action act = () => Price.Of(value);

        // Assert
        act.Should().Throw<InvalidPriceException>();
    }
}
