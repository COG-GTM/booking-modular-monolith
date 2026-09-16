namespace Unit.Test.Booking.Features.Domains;

using System;
using FluentAssertions;
using global::Booking.Booking.Exceptions;
using global::Booking.Booking.ValueObjects;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class TripTests
{
    private static readonly Guid _aircraftId = new("3c5c0000-97c6-fc34-fcd3-08db322230c8");
    private static readonly Guid _departureAirportId = new("3c5c0000-97c6-fc34-fc3c-08db322230c8");
    private static readonly Guid _arriveAirportId = new("3c5c0000-97c6-fc34-a0cb-08db322230c8");
    private static readonly DateTime _flightDate = new(2024, 1, 31, 12, 0, 0);

    [Fact]
    public void can_create_valid_trip()
    {
        var trip = FakeTrip.Generate();

        trip.Should().NotBeNull();
        trip.SeatNumber.Should().Be("33F");
        trip.Price.Should().Be(100m);
    }

    [Fact]
    public void empty_flight_number_should_throw()
    {
        Action act = () =>
            Trip.Of(" ", _aircraftId, _departureAirportId, _arriveAirportId, _flightDate, 100m, "d", "33F");

        act.Should().Throw<InvalidFlightNumberException>();
    }

    [Fact]
    public void empty_aircraft_id_should_throw()
    {
        Action act = () =>
            Trip.Of("1500B", Guid.Empty, _departureAirportId, _arriveAirportId, _flightDate, 100m, "d", "33F");

        act.Should().Throw<InvalidAircraftIdException>();
    }

    [Fact]
    public void empty_departure_airport_id_should_throw()
    {
        Action act = () => Trip.Of("1500B", _aircraftId, Guid.Empty, _arriveAirportId, _flightDate, 100m, "d", "33F");

        act.Should().Throw<InvalidDepartureAirportIdException>();
    }

    [Fact]
    public void empty_arrive_airport_id_should_throw()
    {
        Action act = () =>
            Trip.Of("1500B", _aircraftId, _departureAirportId, Guid.Empty, _flightDate, 100m, "d", "33F");

        act.Should().Throw<InvalidArriveAirportIdException>();
    }

    [Fact]
    public void default_flight_date_should_throw()
    {
        Action act = () =>
            Trip.Of("1500B", _aircraftId, _departureAirportId, _arriveAirportId, default, 100m, "d", "33F");

        act.Should().Throw<InvalidFlightDateException>();
    }

    [Fact]
    public void negative_price_should_throw()
    {
        Action act = () =>
            Trip.Of("1500B", _aircraftId, _departureAirportId, _arriveAirportId, _flightDate, -1m, "d", "33F");

        act.Should().Throw<InvalidPriceException>();
    }

    [Fact]
    public void empty_seat_number_should_throw()
    {
        Action act = () =>
            Trip.Of("1500B", _aircraftId, _departureAirportId, _arriveAirportId, _flightDate, 100m, "d", null);

        act.Should().Throw<SeatNumberException>();
    }
}
