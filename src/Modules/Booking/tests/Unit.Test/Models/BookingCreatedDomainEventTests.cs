using Booking;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Model;
using FluentAssertions;
using Xunit;
using BookingAggregate = Booking.Booking.Models.Booking;

namespace Unit.Test.Models;

// Regression tests for the AB-496 fix: BookingCreatedDomainEvent used to declare a positional `Id` that hid
// Entity<Guid>.Id, so every booking was keyed on Guid.Empty (stream "Booking-0000…", empty BookId).
public class BookingCreatedDomainEventTests
{
    private static PassengerInfo Passenger() => PassengerInfo.Of("Jane Doe");

    private static Trip Trip(string seat = "12A") =>
        Booking.Booking.ValueObjects.Trip.Of(
            "BA123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
            199.99m,
            "AB-496",
            seat
        );

    [Fact]
    public void domain_event_assigns_the_inherited_entity_id()
    {
        var id = Guid.NewGuid();
        var passenger = Passenger();
        var trip = Trip();

        var @event = new BookingCreatedDomainEvent(id, passenger, trip);

        @event.Id.Should().Be(id);
        @event.PassengerInfo.Should().BeSameAs(passenger);
        @event.Trip.Should().BeSameAs(trip);
    }

    [Fact]
    public void create_raises_a_domain_event_keyed_on_the_booking_id()
    {
        var id = Guid.NewGuid();
        var passenger = Passenger();
        var trip = Trip();

        var booking = BookingAggregate.Create(id, passenger, trip, userId: 7);

        booking.Id.Should().Be(id);
        booking.Version.Should().Be(1);

        var @event = booking
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<BookingCreatedDomainEvent>()
            .Subject;
        @event.Id.Should().Be(id);
        @event.PassengerInfo.Should().Be(passenger);
        @event.Trip.Should().Be(trip);
        @event.IsDeleted.Should().BeFalse();
        @event.CreatedBy.Should().Be(7);
        @event.CreatedAt.Should().NotBeNull();
    }

    [Fact]
    public void two_bookings_raise_events_with_distinct_ids()
    {
        var first = BookingAggregate.Create(Guid.NewGuid(), Passenger(), Trip("1A"));
        var second = BookingAggregate.Create(Guid.NewGuid(), Passenger(), Trip("1B"));

        var firstEvent = (BookingCreatedDomainEvent)first.DomainEvents.Single();
        var secondEvent = (BookingCreatedDomainEvent)second.DomainEvents.Single();

        firstEvent.Id.Should().NotBe(Guid.Empty);
        secondEvent.Id.Should().NotBe(Guid.Empty);
        firstEvent.Id.Should().NotBe(secondEvent.Id);
        firstEvent.Id.Should().Be(first.Id);
        secondEvent.Id.Should().Be(second.Id);
    }

    [Fact]
    public void when_rehydrates_the_aggregate_from_the_event()
    {
        var id = Guid.NewGuid();
        var passenger = Passenger();
        var trip = Trip();
        var @event = new BookingCreatedDomainEvent(id, passenger, trip) { IsDeleted = true };

        var booking = new BookingAggregate();
        booking.When(@event);

        booking.Id.Should().Be(id);
        booking.PassengerInfo.Should().Be(passenger);
        booking.Trip.Should().Be(trip);
        booking.IsDeleted.Should().BeTrue();
        booking.Version.Should().Be(1);
        booking.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void event_mapper_carries_the_booking_id_into_the_integration_event()
    {
        var id = Guid.NewGuid();
        var mapper = new BookingEventMapper();

        var integrationEvent = mapper.MapToIntegrationEvent(new BookingCreatedDomainEvent(id, Passenger(), Trip()));

        integrationEvent.Should().BeOfType<BookingCreated>().Which.Id.Should().Be(id);
        mapper.MapToInternalCommand(new BookingCreatedDomainEvent(id, Passenger(), Trip())).Should().BeNull();
    }
}
