namespace Unit.Test.Booking.Features.Domains;

using System;
using System.Linq;
using FluentAssertions;
using global::Booking.Booking.Features.CreatingBook.V1;
using global::Booking.Booking.ValueObjects;
using MassTransit;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class BookingCreatedDomainEventTests
{
    [Fact]
    public void created_event_should_carry_booking_id()
    {
        // Arrange
        var bookingId = NewId.NextGuid();

        // Act
        var booking = global::Booking.Booking.Models.Booking.Create(bookingId, PassengerInfo.Of("Sam"), FakeTrip.Generate());
        var @event = (BookingCreatedDomainEvent)booking.DomainEvents.Single();

        // Assert
        @event.Id.Should().Be(bookingId);
        @event.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void created_event_should_carry_passenger_info_trip_and_audit_fields()
    {
        // Arrange
        var passengerInfo = PassengerInfo.Of("Sam");
        var trip = FakeTrip.Generate();

        // Act
        var booking = global::Booking.Booking.Models.Booking.Create(NewId.NextGuid(), passengerInfo, trip, isDeleted: true, userId: 42);
        var @event = (BookingCreatedDomainEvent)booking.DomainEvents.Single();

        // Assert
        @event.PassengerInfo.Should().Be(passengerInfo);
        @event.Trip.Should().Be(trip);
        @event.IsDeleted.Should().BeTrue();
        @event.CreatedBy.Should().Be(42);
        @event.CreatedAt.Should().NotBeNull();
    }

    [Fact]
    public void rehydrating_from_created_event_should_restore_id_and_deleted_flag()
    {
        // Arrange
        var bookingId = NewId.NextGuid();
        var source = global::Booking.Booking.Models.Booking.Create(bookingId, PassengerInfo.Of("Sam"), FakeTrip.Generate(), isDeleted: true);
        var @event = (BookingCreatedDomainEvent)source.DomainEvents.Single();
        var booking = new global::Booking.Booking.Models.Booking();

        // Act
        booking.When(@event);

        // Assert
        booking.Id.Should().Be(bookingId);
        booking.IsDeleted.Should().BeTrue();
    }
}
