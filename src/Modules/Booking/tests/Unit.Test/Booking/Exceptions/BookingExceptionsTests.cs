using System.Net;
using Booking.Booking.Exceptions;
using BuildingBlocks.Exception;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Exceptions;

public class BookingExceptionsTests
{
    [Fact]
    public void booking_already_exists_exposes_conflict_status_message_and_code()
    {
        var exception = new BookingAlreadyExistException(7);

        exception.StatusCode.Should().Be(HttpStatusCode.Conflict);
        exception.Message.Should().Be("Booking already exist!");
        exception.Code.Should().Be(7);
    }

    [Fact]
    public void booking_already_exists_code_defaults_to_null()
    {
        new BookingAlreadyExistException().Code.Should().BeNull();
    }

    [Fact]
    public void flight_not_found_exposes_not_found_status_and_message()
    {
        var exception = new FlightNotFoundException();

        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.Message.Should().Be("Flight doesn't exist!");
    }

    [Theory]
    [InlineData(typeof(InvalidFlightNumberException))]
    [InlineData(typeof(InvalidAircraftIdException))]
    [InlineData(typeof(InvalidDepartureAirportIdException))]
    [InlineData(typeof(InvalidArriveAirportIdException))]
    [InlineData(typeof(InvalidFlightDateException))]
    [InlineData(typeof(InvalidPriceException))]
    [InlineData(typeof(InvalidPassengerNameException))]
    [InlineData(typeof(SeatNumberException))]
    public void domain_exceptions_derive_from_domain_exception(Type exceptionType)
    {
        typeof(SmartCharging.Infrastructure.Exceptions.DomainException)
            .IsAssignableFrom(exceptionType)
            .Should()
            .BeTrue();
    }
}
