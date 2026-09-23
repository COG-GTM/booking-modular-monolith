namespace Unit.Test.Flight.ValueObjects;

using System;
using FluentAssertions;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.ValueObjects;
using Xunit;

public class FlightNumberTests
{
    [Theory]
    [InlineData("BD467")]
    [InlineData("1")]
    [InlineData(" LH 123 ")]
    public void of_with_non_blank_value_should_return_flight_number_with_same_value(string value)
    {
        // Act
        var flightNumber = FlightNumber.Of(value);
        string converted = flightNumber;

        // Assert
        flightNumber.Value.Should().Be(value);
        converted.Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void of_with_null_or_whitespace_value_should_throw_invalid_flight_number_exception(string value)
    {
        // Act
        Action act = () => FlightNumber.Of(value);

        // Assert
        act.Should().Throw<InvalidFlightNumberException>();
    }
}
