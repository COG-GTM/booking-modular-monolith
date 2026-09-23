namespace Unit.Test.Flight.ValueObjects;

using System;
using FluentAssertions;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.ValueObjects;
using Xunit;

public class FlightIdTests
{
    [Fact]
    public void of_with_non_empty_guid_should_return_flight_id_with_same_value()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var flightId = FlightId.Of(guid);
        Guid converted = flightId;

        // Assert
        flightId.Value.Should().Be(guid);
        converted.Should().Be(guid);
    }

    [Fact]
    public void of_with_empty_guid_should_throw_invalid_flight_id_exception()
    {
        // Act
        Action act = () => FlightId.Of(Guid.Empty);

        // Assert
        act.Should().Throw<InvalidFlightIdException>();
    }

    [Fact]
    public void two_flight_ids_with_same_value_should_be_equal()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act + Assert
        FlightId.Of(guid).Should().Be(FlightId.Of(guid));
    }
}
