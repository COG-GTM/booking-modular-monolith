using System.Text;
using System.Text.Json;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using BuildingBlocks.EventStoreDB.Serialization;
using Xunit;

namespace Booking.Unit.Test;

public class BookingCreatedDomainEventTests
{
    [Fact]
    public void ConstructorAssignsInheritedIdAndEventDataRoundTrips()
    {
        var id = Guid.NewGuid();
        var passengerInfo = PassengerInfo.Of("Test passenger");
        var trip = CreateTrip();
        var domainEvent = new BookingCreatedDomainEvent(id, passengerInfo, trip);

        Assert.Equal(id, domainEvent.Id);
        Assert.NotEqual(Guid.Empty, domainEvent.Id);
        Assert.Same(passengerInfo, domainEvent.PassengerInfo);
        Assert.Same(trip, domainEvent.Trip);

        var eventData = domainEvent.ToJsonEventData();
        var json = Encoding.UTF8.GetString(eventData.Data.Span);
        using var jsonDocument = JsonDocument.Parse(json);
        Assert.Equal(id, jsonDocument.RootElement.GetProperty("Id").GetGuid());

        var deserialized = json.FromJson<BookingCreatedDomainEvent>();

        Assert.Equal(id, deserialized.Id);
        Assert.Equal(passengerInfo.Name, deserialized.PassengerInfo.Name);
        Assert.Equal(trip.FlightNumber, deserialized.Trip.FlightNumber);
        Assert.Equal(trip.AircraftId, deserialized.Trip.AircraftId);
        Assert.Equal(trip.DepartureAirportId, deserialized.Trip.DepartureAirportId);
        Assert.Equal(trip.ArriveAirportId, deserialized.Trip.ArriveAirportId);
        Assert.Equal(trip.FlightDate, deserialized.Trip.FlightDate);
        Assert.Equal(trip.Price, deserialized.Trip.Price);
        Assert.Equal(trip.Description, deserialized.Trip.Description);
        Assert.Equal(trip.SeatNumber, deserialized.Trip.SeatNumber);
    }

    private static Trip CreateTrip() =>
        Trip.Of(
            "AB123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2030, 4, 5, 12, 30, 0, DateTimeKind.Utc),
            249.99m,
            "Round trip",
            "12A"
        );
}
