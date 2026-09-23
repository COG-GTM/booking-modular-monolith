namespace Unit.Test.Booking.Features.Domains;

using System.Linq;
using FluentAssertions;
using global::Booking.Booking.Features.CreatingBook.V1;
using global::Booking.Booking.ValueObjects;
using MassTransit;
using Unit.Test.Fakes;
using Xunit;

public class CreateBookingTests
{
    [Fact]
    public void can_create_valid_booking()
    {
        // Arrange
        var id = NewId.NextGuid();

        // Act
        var booking = FakeBookingCreate.Generate(id);

        // Assert
        booking.Should().NotBeNull();
        booking.Id.Should().Be(id);
        booking.PassengerInfo.Name.Should().Be(FakeBookingCreate.PassengerName);
        booking.Trip.Should().Be(FakeTrip.Generate());
        booking.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void queue_domain_event_on_create()
    {
        // Arrange
        var id = NewId.NextGuid();

        // Act
        var booking = FakeBookingCreate.Generate(id, userId: 42);

        // Assert
        booking.DomainEvents.Count.Should().Be(1);
        var @event = booking.DomainEvents.Single().Should().BeOfType<BookingCreatedDomainEvent>().Subject;
        @event.Id.Should().Be(id);
        @event.PassengerInfo.Should().Be(booking.PassengerInfo);
        @event.Trip.Should().Be(booking.Trip);
        @event.IsDeleted.Should().BeFalse();
        @event.CreatedBy.Should().Be(42);
        @event.CreatedAt.Should().NotBeNull();
    }

    [Fact]
    public void create_applies_event_and_increments_version()
    {
        // Act
        var booking = FakeBookingCreate.Generate();

        // Assert
        booking.Version.Should().Be(1);
    }

    [Fact]
    public void create_propagates_is_deleted_flag()
    {
        // Act
        var booking = FakeBookingCreate.Generate(isDeleted: true);

        // Assert
        booking.IsDeleted.Should().BeTrue();
        booking
            .DomainEvents.Single()
            .Should()
            .BeOfType<BookingCreatedDomainEvent>()
            .Subject.IsDeleted.Should()
            .BeTrue();
    }

    [Fact]
    public void clear_domain_events_returns_and_removes_queued_events()
    {
        // Arrange
        var booking = FakeBookingCreate.Generate();

        // Act
        var dequeued = booking.ClearDomainEvents();

        // Assert
        dequeued.Should().ContainSingle().Which.Should().BeOfType<BookingCreatedDomainEvent>();
        booking.DomainEvents.Should().BeEmpty();
    }
}
