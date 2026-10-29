using System.Text;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Serialization;
using FluentAssertions;
using Xunit;
using BookingAggregate = Booking.Booking.Models.Booking;

namespace Unit.Test;

public sealed class BookingCreatedDomainEventTests
{
    [Fact]
    public void creating_a_booking_preserves_its_id_in_the_aggregate_event_and_replay()
    {
        var id = Guid.NewGuid();
        var passenger = PassengerInfo.Of("Booking Passenger");
        var trip = CreateTrip();

        var booking = BookingAggregate.Create(id, passenger, trip, isDeleted: true, userId: 42);

        booking.Id.Should().Be(id);
        var created = booking
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<BookingCreatedDomainEvent>()
            .Subject;
        created.Id.Should().Be(id);
        created.PassengerInfo.Should().Be(passenger);
        created.Trip.Should().Be(trip);
        created.IsDeleted.Should().BeTrue();
        created.CreatedBy.Should().Be(42);

        var replayed = new BookingAggregate();
        replayed.When(created);
        replayed.Id.Should().Be(id);
        replayed.PassengerInfo.Should().Be(passenger);
        replayed.Trip.Should().Be(trip);
        replayed.IsDeleted.Should().BeTrue();
        replayed.Version.Should().Be(1);
    }

    [Fact]
    public void different_bookings_have_distinct_non_empty_eventstore_streams()
    {
        var passenger = PassengerInfo.Of("Booking Passenger");
        var trip = CreateTrip();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var first = BookingAggregate.Create(firstId, passenger, trip);
        var second = BookingAggregate.Create(secondId, passenger, trip);

        var firstStream = StreamNameMapper.ToStreamId<BookingAggregate>(first.Id);
        var secondStream = StreamNameMapper.ToStreamId<BookingAggregate>(second.Id);
        firstStream.Should().Be(StreamNameMapper.ToStreamId<BookingAggregate>(firstId));
        secondStream.Should().Be(StreamNameMapper.ToStreamId<BookingAggregate>(secondId));
        firstStream.Should().NotBe(secondStream);
    }

    [Fact]
    public void eventstore_json_round_trip_preserves_identity_nested_values_and_metadata()
    {
        var original = new BookingCreatedDomainEvent(
            Guid.NewGuid(),
            PassengerInfo.Of("Booking Passenger"),
            CreateTrip()
        )
        {
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            CreatedBy = 42,
            IsDeleted = true,
            Version = 7,
        };

        var eventData = original.ToJsonEventData();
        var deserialized = Encoding.UTF8.GetString(eventData.Data.Span).FromJson<BookingCreatedDomainEvent>();

        eventData.Type.Should().Be(EventTypeMapper.ToName<BookingCreatedDomainEvent>());
        deserialized.Id.Should().Be(original.Id);
        deserialized.PassengerInfo.Should().Be(original.PassengerInfo);
        deserialized.Trip.Should().Be(original.Trip);
        deserialized.CreatedAt.Should().Be(original.CreatedAt);
        deserialized.CreatedBy.Should().Be(original.CreatedBy);
        deserialized.IsDeleted.Should().Be(original.IsDeleted);
        deserialized.Version.Should().Be(original.Version);
        var replayed = new BookingAggregate();
        replayed.When(deserialized);
        replayed.Id.Should().Be(original.Id);
        replayed.Trip.Should().Be(original.Trip);
        replayed.PassengerInfo.Should().Be(original.PassengerInfo);
    }

    private static Trip CreateTrip() =>
        Trip.Of(
            "BK123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            125.50m,
            "Booking trip",
            "12A"
        );
}
