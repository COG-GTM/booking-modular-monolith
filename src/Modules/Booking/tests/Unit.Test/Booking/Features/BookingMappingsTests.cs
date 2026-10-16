using Booking.Booking.Dtos;
using Booking.Booking.Features;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using FluentAssertions;
using Mapster;
using Xunit;
using BookingAggregate = Booking.Booking.Models.Booking;
using TripValueObject = Booking.Booking.ValueObjects.Trip;

namespace Unit.Test.Features;

public class BookingMappingsTests
{
    [Fact]
    public void booking_maps_to_response_with_every_field()
    {
        var id = Guid.NewGuid();
        var passenger = PassengerInfo.Of("Jane Doe");
        var trip = TripValueObject.Of(
            "BA123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc),
            199.99m,
            "Window seat",
            "12A"
        );
        var config = CreateConfig();
        var booking = BookingAggregate.Create(id, passenger, trip);

        var response = booking.Adapt<BookingResponseDto>(config);

        response.Id.Should().Be(id);
        response.Name.Should().Be("Jane Doe");
        response.FlightNumber.Should().Be("BA123");
        response.AircraftId.Should().Be(trip.AircraftId);
        response.Price.Should().Be(199.99m);
        response.FlightDate.Should().Be(trip.FlightDate);
        response.SeatNumber.Should().Be("12A");
        response.DepartureAirportId.Should().Be(trip.DepartureAirportId);
        response.ArriveAirportId.Should().Be(trip.ArriveAirportId);
        response.Description.Should().Be("Window seat");
    }

    [Fact]
    public void request_maps_to_create_booking_and_generates_a_fresh_id()
    {
        var request = new CreateBookingRequestDto(Guid.NewGuid(), Guid.NewGuid(), "Window seat");
        var config = CreateConfig();

        var first = request.Adapt<CreateBooking>(config);
        var second = request.Adapt<CreateBooking>(config);

        first.PassengerId.Should().Be(request.PassengerId);
        first.FlightId.Should().Be(request.FlightId);
        first.Description.Should().Be(request.Description);
        first.Id.Should().NotBeEmpty();
        second.Id.Should().NotBeEmpty().And.NotBe(first.Id);
    }

    [Fact]
    public void booking_mapping_configuration_compiles()
    {
        CreateConfig().Compile();
    }

    private static TypeAdapterConfig CreateConfig()
    {
        var config = new TypeAdapterConfig();
        new BookingMappings().Register(config);
        return config;
    }
}
