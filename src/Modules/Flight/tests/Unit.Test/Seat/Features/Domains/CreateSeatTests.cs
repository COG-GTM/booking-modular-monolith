namespace Unit.Test.Seat.Features.Domains;

using System.Linq;
using FluentAssertions;
using global::Flight.Flights.ValueObjects;
using global::Flight.Seats.Enums;
using global::Flight.Seats.Features.CreatingSeat.V1;
using global::Flight.Seats.ValueObjects;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class CreateSeatTests
{
    [Fact]
    public void can_create_valid_seat()
    {
        // Arrange
        var command = new FakeCreateSeatCommand().Generate();

        // Act
        var seat = global::Flight.Seats.Models.Seat.Create(
            SeatId.Of(command.Id),
            SeatNumber.Of(command.SeatNumber),
            command.Type,
            command.Class,
            FlightId.Of(command.FlightId)
        );

        // Assert
        seat.Should().NotBeNull();
        seat.Id.Value.Should().Be(command.Id);
        seat.SeatNumber.Value.Should().Be(command.SeatNumber);
        seat.Type.Should().Be(command.Type);
        seat.Class.Should().Be(command.Class);
        seat.FlightId.Value.Should().Be(command.FlightId);
        seat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void can_create_seat_with_is_deleted_flag()
    {
        // Arrange
        var command = new FakeCreateSeatCommand().Generate();

        // Act
        var seat = global::Flight.Seats.Models.Seat.Create(
            SeatId.Of(command.Id),
            SeatNumber.Of(command.SeatNumber),
            command.Type,
            command.Class,
            FlightId.Of(command.FlightId),
            isDeleted: true
        );

        // Assert
        seat.IsDeleted.Should().BeTrue();
        seat.DomainEvents.Single().Should().BeOfType<SeatCreatedDomainEvent>().Which.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void queue_domain_event_on_create()
    {
        // Arrange + Act
        var seat = FakeSeatCreate.Generate();

        // Assert
        seat.DomainEvents.Count.Should().Be(1);
        seat.DomainEvents.FirstOrDefault().Should().BeOfType(typeof(SeatCreatedDomainEvent));
    }

    [Fact]
    public void created_domain_event_should_carry_seat_state()
    {
        // Arrange + Act
        var seat = FakeSeatCreate.Generate();

        // Assert
        var @event = seat.DomainEvents.Single().Should().BeOfType<SeatCreatedDomainEvent>().Subject;
        @event.Id.Should().Be(seat.Id.Value);
        @event.SeatNumber.Should().Be(seat.SeatNumber.Value);
        @event.Type.Should().Be(seat.Type);
        @event.Class.Should().Be(seat.Class);
        @event.FlightId.Should().Be(seat.FlightId.Value);
        @event.IsDeleted.Should().BeFalse();
    }
}
