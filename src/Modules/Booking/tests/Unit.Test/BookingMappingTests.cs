using FluentAssertions;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test;

using global::Booking.Booking.Dtos;
using global::Booking.Booking.Features.CreatingBook.V1;

[Collection(nameof(UnitTestFixture))]
public class BookingMappingTests
{
    private readonly UnitTestFixture _fixture;

    public BookingMappingTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void should_map_create_booking_request_dto_to_command()
    {
        // Arrange
        var request = new FakeCreateBookingRequestDto().Generate();

        // Act
        var command = _fixture.Mapper.Map<CreateBooking>(request);

        // Assert
        command.Should().NotBeNull();
        command.FlightId.Should().Be(request.FlightId);
        command.PassengerId.Should().Be(request.PassengerId);
        command.Description.Should().Be(request.Description);
        command.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void should_map_booking_to_booking_response_dto()
    {
        // Arrange
        var booking = FakeBookingCreate.Generate();

        // Act
        var dto = _fixture.Mapper.Map<BookingResponseDto>(booking);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(booking.Id);
        dto.Name.Should().Be(booking.PassengerInfo.Name);
        dto.FlightNumber.Should().Be(booking.Trip.FlightNumber);
        dto.AircraftId.Should().Be(booking.Trip.AircraftId);
        dto.Price.Should().Be(booking.Trip.Price);
        dto.FlightDate.Should().Be(booking.Trip.FlightDate);
        dto.SeatNumber.Should().Be(booking.Trip.SeatNumber);
        dto.DepartureAirportId.Should().Be(booking.Trip.DepartureAirportId);
        dto.ArriveAirportId.Should().Be(booking.Trip.ArriveAirportId);
        dto.Description.Should().Be(booking.Trip.Description);
    }
}
