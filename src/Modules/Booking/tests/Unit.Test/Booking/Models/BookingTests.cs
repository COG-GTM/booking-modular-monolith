using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Booking.Models;

[Collection(nameof(Common.UnitTestFixture))]
public class BookingTests
{
    [Fact]
    public void create_booking_sets_correct_properties()
    {
        var id = Guid.NewGuid();
        var passengerInfo = PassengerInfo.Of("Jane Doe");
        var trip = Trip.Of(
            "FL200",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            300m,
            "Business trip",
            "5B"
        );

        var booking = global::Booking.Booking.Models.Booking.Create(id, passengerInfo, trip);

        booking.Should().NotBeNull();
        booking.Id.Should().Be(id);
        booking.PassengerInfo.Should().Be(passengerInfo);
        booking.Trip.Should().Be(trip);
    }

    [Fact]
    public void create_booking_enqueues_booking_created_domain_event()
    {
        var id = Guid.NewGuid();
        var passengerInfo = PassengerInfo.Of("Jane Doe");
        var trip = Trip.Of(
            "FL200",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            300m,
            "Business trip",
            "5B"
        );

        var booking = global::Booking.Booking.Models.Booking.Create(id, passengerInfo, trip);

        booking.DomainEvents.Should().HaveCount(1);
        booking.DomainEvents[0].Should().BeOfType<BookingCreatedDomainEvent>();

        var domainEvent = (BookingCreatedDomainEvent)booking.DomainEvents[0];
        domainEvent.Id.Should().Be(id);
        domainEvent.PassengerInfo.Should().Be(passengerInfo);
        domainEvent.Trip.Should().Be(trip);
    }

    [Fact]
    public void when_applies_booking_created_domain_event_and_increments_version()
    {
        var id = Guid.NewGuid();
        var passengerInfo = PassengerInfo.Of("Jane Doe");
        var trip = Trip.Of(
            "FL200",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow.AddDays(1),
            300m,
            "Business trip",
            "5B"
        );

        var booking = global::Booking.Booking.Models.Booking.Create(id, passengerInfo, trip);

        // Create calls Apply internally which increments Version once.
        // Calling When with the same event should increment Version again.
        var initialVersion = booking.Version;
        var domainEvent = (BookingCreatedDomainEvent)booking.DomainEvents[0];

        booking.When(domainEvent);

        booking.Version.Should().Be(initialVersion + 1);
        booking.Id.Should().Be(id);
        booking.PassengerInfo.Should().Be(passengerInfo);
        booking.Trip.Should().Be(trip);
    }
}
