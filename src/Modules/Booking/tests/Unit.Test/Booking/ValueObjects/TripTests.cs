using Booking.Booking.Exceptions;
using FluentAssertions;
using Xunit;
using TripValueObject = Booking.Booking.ValueObjects.Trip;

namespace Unit.Test.ValueObjects;

public class TripTests
{
    [Fact]
    public void of_keeps_all_fields()
    {
        var flightDate = new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc);

        var trip = TripValueObject.Of(
            "BA123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            flightDate,
            199.99m,
            null,
            "12A"
        );

        trip.FlightNumber.Should().Be("BA123");
        trip.AircraftId.Should().NotBeEmpty();
        trip.DepartureAirportId.Should().NotBeEmpty();
        trip.ArriveAirportId.Should().NotBeEmpty();
        trip.FlightDate.Should().Be(flightDate);
        trip.Price.Should().Be(199.99m);
        trip.Description.Should().BeNull();
        trip.SeatNumber.Should().Be("12A");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void of_rejects_invalid_flight_number(string flightNumber)
    {
        var exception = FluentActions
            .Invoking(() => CreateTrip(flightNumber: flightNumber))
            .Should()
            .Throw<InvalidFlightNumberException>()
            .Which;

        exception.Message.Should().Contain($"'{flightNumber}'");
    }

    [Fact]
    public void of_rejects_empty_aircraft_id_with_value_in_message()
    {
        var exception = FluentActions
            .Invoking(() => CreateTrip(aircraftId: Guid.Empty))
            .Should()
            .Throw<InvalidAircraftIdException>()
            .Which;

        exception.Message.Should().Contain(Guid.Empty.ToString());
    }

    [Fact]
    public void of_rejects_empty_departure_airport_id_with_value_in_message()
    {
        var exception = FluentActions
            .Invoking(() => CreateTrip(departureAirportId: Guid.Empty))
            .Should()
            .Throw<InvalidDepartureAirportIdException>()
            .Which;

        exception.Message.Should().Contain(Guid.Empty.ToString());
    }

    [Fact]
    public void of_rejects_empty_arrive_airport_id()
    {
        FluentActions
            .Invoking(() => CreateTrip(arriveAirportId: Guid.Empty))
            .Should()
            .Throw<InvalidArriveAirportIdException>();
    }

    [Fact]
    public void of_rejects_default_flight_date_with_value_in_message()
    {
        var exception = FluentActions
            .Invoking(() => CreateTrip(flightDate: default(DateTime)))
            .Should()
            .Throw<InvalidFlightDateException>()
            .Which;

        exception.Message.Should().Contain(default(DateTime).ToString());
    }

    [Fact]
    public void of_rejects_negative_price_with_value_in_message()
    {
        var exception = FluentActions
            .Invoking(() => CreateTrip(price: -0.01m))
            .Should()
            .Throw<InvalidPriceException>()
            .Which;

        exception.Message.Should().Contain("-0.01");
    }

    [Fact]
    public void of_allows_zero_price()
    {
        CreateTrip(price: 0).Price.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void of_rejects_invalid_seat_number_with_value_in_message(string seatNumber)
    {
        var exception = FluentActions
            .Invoking(() => CreateTrip(seatNumber: seatNumber))
            .Should()
            .Throw<SeatNumberException>()
            .Which;

        exception.Message.Should().Contain($"'{seatNumber}'");
    }

    [Fact]
    public void of_returns_equal_records_for_equal_arguments()
    {
        var aircraftId = Guid.NewGuid();
        var departureAirportId = Guid.NewGuid();
        var arriveAirportId = Guid.NewGuid();
        var flightDate = new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc);

        var first = TripValueObject.Of(
            "BA123",
            aircraftId,
            departureAirportId,
            arriveAirportId,
            flightDate,
            99m,
            "Trip",
            "12A"
        );
        var second = TripValueObject.Of(
            "BA123",
            aircraftId,
            departureAirportId,
            arriveAirportId,
            flightDate,
            99m,
            "Trip",
            "12A"
        );

        first.Should().Be(second);
    }

    private static TripValueObject CreateTrip(
        string flightNumber = "BA123",
        Guid? aircraftId = null,
        Guid? departureAirportId = null,
        Guid? arriveAirportId = null,
        DateTime? flightDate = null,
        decimal price = 100,
        string description = "description",
        string seatNumber = "12A"
    ) =>
        TripValueObject.Of(
            flightNumber,
            aircraftId ?? Guid.NewGuid(),
            departureAirportId ?? Guid.NewGuid(),
            arriveAirportId ?? Guid.NewGuid(),
            flightDate ?? new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc),
            price,
            description,
            seatNumber
        );
}
