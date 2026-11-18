using System.Text;
using BuildingBlocks.EventStoreDB.Serialization;
using EventStore.Client;
using FluentAssertions;
using global::Booking.Booking.Features.CreatingBook.V1;
using Xunit;

namespace Integration.Test.Booking;

public class BookingEventSerializationTests
{
    [Fact]
    public void should_read_a_legacy_booking_event_with_numeric_audit_ids()
    {
        var payload = """
            {
              "Id": "01687c34-6a5b-451a-8f06-ad6593251a5f",
              "PassengerInfo": { "Name": "Alice" },
              "Trip": {
                "FlightNumber": "AB123",
                "AircraftId": "912bfc24-dab9-4910-8847-93c65c02847c",
                "DepartureAirportId": "3f30d4d1-1f11-4c9b-a519-689d22c50711",
                "ArriveAirportId": "f375d93e-be88-4d7e-a7fe-4e1ecbec9fae",
                "FlightDate": "2026-12-01T10:00:00Z",
                "Price": 100,
                "Description": "Outbound",
                "SeatNumber": "1A"
              },
              "CreatedBy": 0,
              "LastModifiedBy": 0
            }
            """;
        var record = new EventRecord(
            "Booking-01687c34-6a5b-451a-8f06-ad6593251a5f",
            Uuid.NewUuid(),
            StreamPosition.Start,
            Position.Start,
            new Dictionary<string, string>
            {
                ["type"] = "Booking_Booking_Features_CreatingBook_V1_BookingCreatedDomainEvent",
                ["content-type"] = "application/json",
                ["created"] = "0",
            },
            Encoding.UTF8.GetBytes(payload),
            ReadOnlyMemory<byte>.Empty
        );

        var restored = new ResolvedEvent(
            record,
            link: null,
            commitPosition: null
        ).Deserialize<BookingCreatedDomainEvent>();

        restored.Should().NotBeNull();
        restored!.PassengerInfo.Name.Should().Be("Alice");
        restored.Trip.SeatNumber.Should().Be("1A");
        restored.CreatedBy.Should().BeNull();
        restored.LastModifiedBy.Should().BeNull();
    }
}
