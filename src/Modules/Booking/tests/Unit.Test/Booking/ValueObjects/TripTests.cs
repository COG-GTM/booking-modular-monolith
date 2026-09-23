namespace Unit.Test.Booking.ValueObjects;

using FluentAssertions;
using global::Booking.Booking.Exceptions;
using global::Booking.Booking.ValueObjects;
using Unit.Test.Fakes;
using Xunit;

public class TripTests
{
    [Fact]
    public void can_create_valid_trip()
    {
        // Arrange + Act
        var trip = FakeTrip.Generate();

        // Assert
        trip.Should().NotBeNull();
        trip.FlightNumber.Should().Be(FakeTrip.FlightNumber);
        trip.AircraftId.Should().Be(FakeTrip.AircraftId);
        trip.DepartureAirportId.Should().Be(FakeTrip.DepartureAirportId);
        trip.ArriveAirportId.Should().Be(FakeTrip.ArriveAirportId);
        trip.FlightDate.Should().Be(FakeTrip.FlightDate);
        trip.Price.Should().Be(FakeTrip.Price);
        trip.Description.Should().Be(FakeTrip.Description);
        trip.SeatNumber.Should().Be(FakeTrip.SeatNumber);
    }

    [Fact]
    public void zero_price_is_allowed()
    {
        // Arrange + Act
        var trip = Trip.Of(
            FakeTrip.FlightNumber,
            FakeTrip.AircraftId,
            FakeTrip.DepartureAirportId,
            FakeTrip.ArriveAirportId,
            FakeTrip.FlightDate,
            0m,
            FakeTrip.Description,
            FakeTrip.SeatNumber
        );

        // Assert
        trip.Price.Should().Be(0m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_flight_number_is_blank(string? flightNumber)
    {
        // Act
        var act = () =>
            Trip.Of(
                flightNumber!,
                FakeTrip.AircraftId,
                FakeTrip.DepartureAirportId,
                FakeTrip.ArriveAirportId,
                FakeTrip.FlightDate,
                FakeTrip.Price,
                FakeTrip.Description,
                FakeTrip.SeatNumber
            );

        // Assert
        act.Should().ThrowExactly<InvalidFlightNumberException>();
    }

    [Fact]
    public void should_throw_when_aircraft_id_is_empty()
    {
        // Act
        var act = () =>
            Trip.Of(
                FakeTrip.FlightNumber,
                Guid.Empty,
                FakeTrip.DepartureAirportId,
                FakeTrip.ArriveAirportId,
                FakeTrip.FlightDate,
                FakeTrip.Price,
                FakeTrip.Description,
                FakeTrip.SeatNumber
            );

        // Assert
        act.Should().ThrowExactly<InvalidAircraftIdException>();
    }

    [Fact]
    public void should_throw_when_departure_airport_id_is_empty()
    {
        // Act
        var act = () =>
            Trip.Of(
                FakeTrip.FlightNumber,
                FakeTrip.AircraftId,
                Guid.Empty,
                FakeTrip.ArriveAirportId,
                FakeTrip.FlightDate,
                FakeTrip.Price,
                FakeTrip.Description,
                FakeTrip.SeatNumber
            );

        // Assert
        act.Should().ThrowExactly<InvalidDepartureAirportIdException>();
    }

    [Fact]
    public void should_throw_when_arrive_airport_id_is_empty()
    {
        // Act
        var act = () =>
            Trip.Of(
                FakeTrip.FlightNumber,
                FakeTrip.AircraftId,
                FakeTrip.DepartureAirportId,
                Guid.Empty,
                FakeTrip.FlightDate,
                FakeTrip.Price,
                FakeTrip.Description,
                FakeTrip.SeatNumber
            );

        // Assert
        act.Should().ThrowExactly<InvalidArriveAirportIdException>();
    }

    [Fact]
    public void should_throw_when_flight_date_is_default()
    {
        // Act
        var act = () =>
            Trip.Of(
                FakeTrip.FlightNumber,
                FakeTrip.AircraftId,
                FakeTrip.DepartureAirportId,
                FakeTrip.ArriveAirportId,
                default,
                FakeTrip.Price,
                FakeTrip.Description,
                FakeTrip.SeatNumber
            );

        // Assert
        act.Should().ThrowExactly<InvalidFlightDateException>();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void should_throw_when_price_is_negative(decimal price)
    {
        // Act
        var act = () =>
            Trip.Of(
                FakeTrip.FlightNumber,
                FakeTrip.AircraftId,
                FakeTrip.DepartureAirportId,
                FakeTrip.ArriveAirportId,
                FakeTrip.FlightDate,
                price,
                FakeTrip.Description,
                FakeTrip.SeatNumber
            );

        // Assert
        act.Should().ThrowExactly<InvalidPriceException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_seat_number_is_blank(string? seatNumber)
    {
        // Act
        var act = () =>
            Trip.Of(
                FakeTrip.FlightNumber,
                FakeTrip.AircraftId,
                FakeTrip.DepartureAirportId,
                FakeTrip.ArriveAirportId,
                FakeTrip.FlightDate,
                FakeTrip.Price,
                FakeTrip.Description,
                seatNumber!
            );

        // Assert
        act.Should().ThrowExactly<SeatNumberException>();
    }

    [Fact]
    public void description_is_not_validated()
    {
        // Act
        var trip = Trip.Of(
            FakeTrip.FlightNumber,
            FakeTrip.AircraftId,
            FakeTrip.DepartureAirportId,
            FakeTrip.ArriveAirportId,
            FakeTrip.FlightDate,
            FakeTrip.Price,
            string.Empty,
            FakeTrip.SeatNumber
        );

        // Assert
        trip.Description.Should().BeEmpty();
    }
}
