namespace Unit.Test.Seat.Features.Domains;

using System;
using System.Linq;
using FluentAssertions;
using global::Flight.Seats.Features.ReservingSeat.V1;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class ReserveSeatTests
{
    [Fact]
    public void can_reserve_valid_seat()
    {
        // Arrange
        var seat = FakeSeatCreate.Generate();
        seat.IsDeleted.Should().BeFalse();
        seat.LastModified.Should().BeNull();

        // Act
        seat.ReserveSeat();

        // Assert
        seat.IsDeleted.Should().BeTrue();
        seat.LastModified.Should().NotBeNull();
        seat.LastModified.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void queue_domain_event_on_reserve()
    {
        // Arrange
        var seat = FakeSeatCreate.Generate();
        seat.ClearDomainEvents();

        // Act
        seat.ReserveSeat();

        // Assert
        seat.DomainEvents.Count.Should().Be(1);
        seat.DomainEvents.FirstOrDefault().Should().BeOfType(typeof(SeatReservedDomainEvent));
    }

    [Fact]
    public void reserved_domain_event_should_carry_seat_state()
    {
        // Arrange
        var seat = FakeSeatCreate.Generate();
        seat.ClearDomainEvents();

        // Act
        seat.ReserveSeat();

        // Assert
        var @event = seat.DomainEvents.Single().Should().BeOfType<SeatReservedDomainEvent>().Subject;
        @event.Id.Should().Be(seat.Id.Value);
        @event.SeatNumber.Should().Be(seat.SeatNumber.Value);
        @event.Type.Should().Be(seat.Type);
        @event.Class.Should().Be(seat.Class);
        @event.FlightId.Should().Be(seat.FlightId.Value);
        @event.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void reserve_should_not_change_seat_identity()
    {
        // Arrange
        var seat = FakeSeatCreate.Generate();
        var id = seat.Id;
        var seatNumber = seat.SeatNumber;
        var flightId = seat.FlightId;

        // Act
        seat.ReserveSeat();

        // Assert
        seat.Id.Should().Be(id);
        seat.SeatNumber.Should().Be(seatNumber);
        seat.FlightId.Should().Be(flightId);
    }
}
