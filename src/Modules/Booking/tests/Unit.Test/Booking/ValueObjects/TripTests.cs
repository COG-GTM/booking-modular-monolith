using Booking.Booking.Exceptions;
using Booking.Booking.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Booking.ValueObjects;

[Collection(nameof(Common.UnitTestFixture))]
public class TripTests
{
    [Fact]
    public void can_create_trip_with_valid_inputs()
    {
        var flightNumber = "FL100";
        var aircraftId = Guid.NewGuid();
        var departureAirportId = Guid.NewGuid();
        var arriveAirportId = Guid.NewGuid();
        var flightDate = DateTime.UtcNow.AddDays(1);
        var price = 250.50m;
        var description = "Test flight";
        var seatNumber = "12A";

        var trip = Trip.Of(
            flightNumber,
            aircraftId,
            departureAirportId,
            arriveAirportId,
            flightDate,
            price,
            description,
            seatNumber
        );

        trip.Should().NotBeNull();
        trip.FlightNumber.Should().Be(flightNumber);
        trip.AircraftId.Should().Be(aircraftId);
        trip.DepartureAirportId.Should().Be(departureAirportId);
        trip.ArriveAirportId.Should().Be(arriveAirportId);
        trip.FlightDate.Should().Be(flightDate);
        trip.Price.Should().Be(price);
        trip.Description.Should().Be(description);
        trip.SeatNumber.Should().Be(seatNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void empty_or_null_flight_number_throws_invalid_flight_number_exception(string? flightNumber)
    {
        var act = () =>
            Trip.Of(
                flightNumber!,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow.AddDays(1),
                100m,
                "desc",
                "1A"
            );

        act.Should().Throw<InvalidFlightNumberException>();
    }

    [Fact]
    public void empty_aircraft_id_throws_invalid_aircraft_id_exception()
    {
        var act = () =>
            Trip.Of("FL100", Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), 100m, "desc", "1A");

        act.Should().Throw<InvalidAircraftIdException>();
    }

    [Fact]
    public void empty_departure_airport_id_throws_invalid_departure_airport_id_exception()
    {
        var act = () =>
            Trip.Of("FL100", Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), DateTime.UtcNow.AddDays(1), 100m, "desc", "1A");

        act.Should().Throw<InvalidDepartureAirportIdException>();
    }

    [Fact]
    public void empty_arrive_airport_id_throws_invalid_arrive_airport_id_exception()
    {
        var act = () =>
            Trip.Of("FL100", Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, DateTime.UtcNow.AddDays(1), 100m, "desc", "1A");

        act.Should().Throw<InvalidArriveAirportIdException>();
    }

    [Fact]
    public void default_flight_date_throws_invalid_flight_date_exception()
    {
        var act = () =>
            Trip.Of("FL100", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), default, 100m, "desc", "1A");

        act.Should().Throw<InvalidFlightDateException>();
    }

    [Fact]
    public void negative_price_throws_invalid_price_exception()
    {
        var act = () =>
            Trip.Of("FL100", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), -10m, "desc", "1A");

        act.Should().Throw<InvalidPriceException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void empty_seat_number_throws_seat_number_exception(string? seatNumber)
    {
        var act = () =>
            Trip.Of("FL100", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1), 100m, "desc", seatNumber!);

        act.Should().Throw<SeatNumberException>();
    }
}
