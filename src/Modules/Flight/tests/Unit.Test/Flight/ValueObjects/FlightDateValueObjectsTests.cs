namespace Unit.Test.Flight.ValueObjects;

using System;
using FluentAssertions;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.ValueObjects;
using Xunit;

public class FlightDateValueObjectsTests
{
    private static readonly DateTime ValidDate = new(2030, 6, 15, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void arrive_date_of_with_valid_value_should_return_same_value()
    {
        // Act
        var arriveDate = ArriveDate.Of(ValidDate);
        DateTime converted = arriveDate;

        // Assert
        arriveDate.Value.Should().Be(ValidDate);
        converted.Should().Be(ValidDate);
    }

    [Fact]
    public void arrive_date_of_with_default_value_should_throw_invalid_arrive_date_exception()
    {
        // Act
        Action act = () => ArriveDate.Of(default);

        // Assert
        act.Should().Throw<InvalidArriveDateException>();
    }

    [Fact]
    public void departure_date_of_with_valid_value_should_return_same_value()
    {
        // Act
        var departureDate = DepartureDate.Of(ValidDate);
        DateTime converted = departureDate;

        // Assert
        departureDate.Value.Should().Be(ValidDate);
        converted.Should().Be(ValidDate);
    }

    [Fact]
    public void departure_date_of_with_default_value_should_throw_invalid_departure_date_exception()
    {
        // Act
        Action act = () => DepartureDate.Of(default);

        // Assert
        act.Should().Throw<InvalidDepartureDateException>();
    }

    [Fact]
    public void flight_date_of_with_valid_value_should_return_same_value()
    {
        // Act
        var flightDate = FlightDate.Of(ValidDate);
        DateTime converted = flightDate;

        // Assert
        flightDate.Value.Should().Be(ValidDate);
        converted.Should().Be(ValidDate);
    }

    [Fact]
    public void flight_date_of_with_default_value_should_throw_invalid_flight_date_exception()
    {
        // Act
        Action act = () => FlightDate.Of(default);

        // Assert
        act.Should().Throw<InvalidFlightDateException>();
    }

    [Fact]
    public void date_value_objects_should_accept_min_value_plus_one_tick()
    {
        // Arrange
        var almostDefault = DateTime.MinValue.AddTicks(1);

        // Act + Assert
        ArriveDate.Of(almostDefault).Value.Should().Be(almostDefault);
        DepartureDate.Of(almostDefault).Value.Should().Be(almostDefault);
        FlightDate.Of(almostDefault).Value.Should().Be(almostDefault);
    }
}
