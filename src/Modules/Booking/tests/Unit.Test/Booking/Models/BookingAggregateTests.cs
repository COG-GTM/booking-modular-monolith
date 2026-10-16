using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using FluentAssertions;
using Xunit;
using BookingAggregate = Booking.Booking.Models.Booking;

namespace Unit.Test.Models;

public class BookingAggregateTests
{
    [Fact]
    public void create_propagates_deleted_state_to_aggregate_and_event()
    {
        var booking = BookingAggregate.Create(Guid.NewGuid(), Passenger(), Trip(), isDeleted: true);

        booking.IsDeleted.Should().BeTrue();
        booking
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<BookingCreatedDomainEvent>()
            .Which.IsDeleted.Should()
            .BeTrue();
    }

    [Fact]
    public void create_without_user_id_sets_event_created_by_to_null()
    {
        var booking = BookingAggregate.Create(Guid.NewGuid(), Passenger(), Trip());

        booking
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<BookingCreatedDomainEvent>()
            .Which.CreatedBy.Should()
            .BeNull();
    }

    [Fact]
    public void create_sets_event_created_at_close_to_now()
    {
        var booking = BookingAggregate.Create(Guid.NewGuid(), Passenger(), Trip());

        var now = DateTime.Now;
        var createdAt = booking
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<BookingCreatedDomainEvent>()
            .Which.CreatedAt;
        createdAt.Should().NotBeNull();
        createdAt!.Value.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void when_receives_unrelated_event_leaves_aggregate_and_version_unchanged()
    {
        var booking = BookingAggregate.Create(Guid.NewGuid(), Passenger(), Trip());
        var version = booking.Version;
        var id = booking.Id;
        var passengerInfo = booking.PassengerInfo;
        var trip = booking.Trip;

        booking.When(new object());

        booking.Id.Should().Be(id);
        booking.PassengerInfo.Should().Be(passengerInfo);
        booking.Trip.Should().Be(trip);
        booking.IsDeleted.Should().BeFalse();
        booking.Version.Should().Be(version);
        booking.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void clear_domain_events_empties_event_collection()
    {
        var booking = BookingAggregate.Create(Guid.NewGuid(), Passenger(), Trip());

        booking.ClearDomainEvents().Should().ContainSingle();

        booking.DomainEvents.Should().BeEmpty();
    }

    private static PassengerInfo Passenger() => PassengerInfo.Of("Jane Doe");

    private static Trip Trip() =>
        Booking.Booking.ValueObjects.Trip.Of(
            "BA123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc),
            199.99m,
            "Trip",
            "12A"
        );
}
