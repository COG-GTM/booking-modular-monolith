namespace Unit.Test.Booking.Features.Domains;

using System.Linq;
using FluentAssertions;
using global::Booking.Booking.Features.CreatingBook.V1;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class CreateBookingTests
{
    [Fact]
    public void can_create_valid_booking()
    {
        // Arrange + Act
        var booking = FakeBookingCreate.Generate();

        // Assert
        booking.Should().NotBeNull();
        booking.Id.Should().NotBeEmpty();
        booking.PassengerInfo.Name.Should().Be("Sam");
        booking.Trip.FlightNumber.Should().Be("1500B");
        booking.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void queue_domain_event_on_create()
    {
        // Arrange + Act
        var booking = FakeBookingCreate.Generate();

        // Assert
        booking.DomainEvents.Count.Should().Be(1);
        booking.DomainEvents.FirstOrDefault().Should().BeOfType(typeof(BookingCreatedDomainEvent));
    }

    [Fact]
    public void apply_created_event_should_increase_version()
    {
        // Arrange + Act
        var booking = FakeBookingCreate.Generate();

        // Assert
        booking.Version.Should().Be(1);
    }

    [Fact]
    public void when_should_rehydrate_booking_from_created_event()
    {
        // Arrange
        var source = FakeBookingCreate.Generate();
        var @event = (BookingCreatedDomainEvent)source.DomainEvents.Single();
        var booking = new global::Booking.Booking.Models.Booking();

        // Act
        booking.When(@event);

        // Assert
        booking.Id.Should().Be(source.Id);
        booking.Trip.Should().Be(source.Trip);
        booking.PassengerInfo.Should().Be(source.PassengerInfo);
        booking.Version.Should().Be(1);
    }
}
