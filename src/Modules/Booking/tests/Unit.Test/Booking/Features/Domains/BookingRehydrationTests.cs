namespace Unit.Test.Booking.Features.Domains;

using FluentAssertions;
using global::Booking.Booking.Features.CreatingBook.V1;
using global::Booking.Booking.ValueObjects;
using MassTransit;
using Unit.Test.Fakes;
using Xunit;

public class BookingRehydrationTests
{
    [Fact]
    public void when_booking_created_event_applies_all_fields()
    {
        // Arrange
        var id = NewId.NextGuid();
        var passengerInfo = PassengerInfo.Of(FakeBookingCreate.PassengerName);
        var trip = FakeTrip.Generate();
        var @event = new BookingCreatedDomainEvent(id, passengerInfo, trip) { IsDeleted = true };
        var booking = new global::Booking.Booking.Models.Booking();

        // Act
        booking.When(@event);

        // Assert
        booking.Id.Should().Be(id);
        booking.PassengerInfo.Should().Be(passengerInfo);
        booking.Trip.Should().Be(trip);
        booking.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void when_booking_created_event_increments_version_from_zero_to_one()
    {
        // Arrange
        var booking = new global::Booking.Booking.Models.Booking();
        var @event = new BookingCreatedDomainEvent(
            NewId.NextGuid(),
            PassengerInfo.Of(FakeBookingCreate.PassengerName),
            FakeTrip.Generate()
        );

        // Act
        booking.Version.Should().Be(0);
        booking.When(@event);

        // Assert
        booking.Version.Should().Be(1);
    }

    [Fact]
    public void replaying_multiple_events_yields_last_state_and_cumulative_version()
    {
        // Arrange
        var booking = new global::Booking.Booking.Models.Booking();
        var firstId = NewId.NextGuid();
        var lastId = NewId.NextGuid();
        var lastTrip = Trip.Of(
            "BD999",
            FakeTrip.AircraftId,
            FakeTrip.DepartureAirportId,
            FakeTrip.ArriveAirportId,
            FakeTrip.FlightDate.AddDays(1),
            99m,
            "Updated",
            "1C"
        );
        var events = new object[]
        {
            new BookingCreatedDomainEvent(firstId, PassengerInfo.Of("First"), FakeTrip.Generate()),
            new BookingCreatedDomainEvent(NewId.NextGuid(), PassengerInfo.Of("Second"), FakeTrip.Generate()),
            new BookingCreatedDomainEvent(lastId, PassengerInfo.Of("Last"), lastTrip),
        };

        // Act
        foreach (var @event in events)
        {
            booking.When(@event);
        }

        // Assert
        booking.Version.Should().Be(3);
        booking.Id.Should().Be(lastId);
        booking.PassengerInfo.Name.Should().Be("Last");
        booking.Trip.Should().Be(lastTrip);
    }

    [Fact]
    public void when_does_not_queue_domain_events()
    {
        // Arrange
        var booking = new global::Booking.Booking.Models.Booking();
        var @event = new BookingCreatedDomainEvent(
            NewId.NextGuid(),
            PassengerInfo.Of(FakeBookingCreate.PassengerName),
            FakeTrip.Generate()
        );

        // Act
        booking.When(@event);

        // Assert
        booking.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void when_unknown_event_leaves_state_unchanged()
    {
        // Arrange
        var booking = FakeBookingCreate.Generate();
        var snapshot = booking with { };

        // Act
        booking.When(new UnknownEvent());
        booking.When("not an event");

        // Assert
        booking.Version.Should().Be(1);
        booking.Id.Should().Be(snapshot.Id);
        booking.PassengerInfo.Should().Be(snapshot.PassengerInfo);
        booking.Trip.Should().Be(snapshot.Trip);
        booking.IsDeleted.Should().Be(snapshot.IsDeleted);
    }

    private sealed record UnknownEvent;
}
