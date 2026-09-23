namespace Unit.Test.Flight.ValueObjects;

using System;
using FluentAssertions;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.ValueObjects;
using Xunit;

public class DurationMinutesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(120.5)]
    public void of_with_non_negative_value_should_return_duration_with_same_value(decimal value)
    {
        // Act
        var duration = DurationMinutes.Of(value);

        // Assert
        duration.Value.Should().Be(value);
    }

    [Fact]
    public void implicit_conversion_should_return_underlying_value()
    {
        // Arrange
        var duration = DurationMinutes.Of(90m);

        // Act
        decimal value = duration;

        // Assert
        value.Should().Be(90m);
    }

    [Fact]
    public void explicit_conversion_from_decimal_should_wrap_value()
    {
        // Act
        var duration = (DurationMinutes)75m;

        // Assert
        duration.Value.Should().Be(75m);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(-1)]
    [InlineData(-600)]
    public void of_with_negative_value_should_throw_invalid_duration_exception(decimal value)
    {
        // Act
        Action act = () => DurationMinutes.Of(value);

        // Assert
        act.Should().Throw<InvalidDurationException>();
    }
}
