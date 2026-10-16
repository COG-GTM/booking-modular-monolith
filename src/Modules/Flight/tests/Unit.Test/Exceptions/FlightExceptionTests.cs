namespace Unit.Test.Exceptions;

using System;
using System.Linq;
using System.Net;
using BuildingBlocks.Exception;
using FluentAssertions;
using global::Flight.Aircrafts.Exceptions;
using global::Flight.Airports.Exceptions;
using global::Flight.Flights.Exceptions;
using global::Flight.Seats.Exceptions;
using SmartCharging.Infrastructure.Exceptions;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class FlightExceptionTests
{
    [Fact]
    public void not_found_and_conflict_exceptions_expose_expected_status_messages_and_codes()
    {
        var notFound = new FlightNotFountException();
        var flightConflict = new FlightAlreadyExistException(21);
        var seatConflict = new SeatAlreadyExistException(34);

        notFound.Should().BeOfType<FlightNotFountException>();
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        notFound.Message.Should().Be("Flight not found!");
        flightConflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        flightConflict.Message.Should().Be("Flight already exist!");
        flightConflict.Code.Should().Be(21);
        seatConflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        seatConflict.Message.Should().Be("Seat already exist!");
        seatConflict.Code.Should().Be(34);
    }

    [Fact]
    public void conflict_and_capacity_exceptions_use_app_exception_statuses()
    {
        var aircraftConflict = new AircraftAlreadyExistException();
        var airportConflict = new AirportAlreadyExistException(17);
        var allSeatsFull = new AllSeatsFullException();
        var incorrectSeat = new SeatNumberIncorrectException();

        aircraftConflict.Should().BeAssignableTo<AppException>();
        aircraftConflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        airportConflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        airportConflict.Code.Should().Be(17);
        allSeatsFull.Should().BeAssignableTo<AppException>();
        allSeatsFull.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        allSeatsFull.Message.Should().Be("All seats are full!");
        incorrectSeat.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        incorrectSeat.Message.Should().Be("Seat number is incorrect!");
    }

    [Fact]
    public void invalid_value_exceptions_are_bad_request_domain_exceptions()
    {
        Exception[] exceptions =
        [
            new InvalidAircraftIdException(Guid.Empty),
            new InvalidManufacturingYearException(),
            new InvalidModelException(),
            new global::Flight.Aircrafts.Exceptions.InvalidNameException(),
            new InvalidAddressException(),
            new InvalidAirportIdException(Guid.Empty),
            new InvalidCodeException(),
            new global::Flight.Airports.Exceptions.InvalidNameException(),
            new InvalidArriveDateException(default),
            new InvalidDepartureDateException(default),
            new InvalidDurationException(),
            new InvalidFlightDateException(default),
            new InvalidFlightIdException(Guid.Empty),
            new InvalidFlightNumberException(""),
            new InvalidPriceException(),
            new InvalidSeatIdException(Guid.Empty),
            new InvalidSeatNumberException(),
        ];

        foreach (var exception in exceptions)
        {
            exception
                .Should()
                .BeAssignableTo<DomainException>()
                .Which.StatusCode.Should()
                .Be(HttpStatusCode.BadRequest);
        }
        new InvalidFlightNumberException("").Message.Should().Contain("Flight Number");
    }
}
