namespace Unit.Test.Seat.Features.Domains
{
    using System.Linq;
    using FluentAssertions;
    using global::Flight.Flights.ValueObjects;
    using global::Flight.Seats.Enums;
    using global::Flight.Seats.Exceptions;
    using global::Flight.Seats.Features.ReservingSeat.V1;
    using global::Flight.Seats.ValueObjects;
    using MassTransit;
    using Unit.Test.Common;
    using Xunit;

    [Collection(nameof(UnitTestFixture))]
    public class ReserveSeatTests
    {
        [Fact]
        public void reserve_available_seat_should_mark_seat_reserved_and_queue_domain_event()
        {
            // Arrange
            var seat = CreateSeat(isDeleted: false);

            // Act
            seat.ReserveSeat();

            // Assert
            seat.IsDeleted.Should().BeTrue();
            seat.DomainEvents.Count.Should().Be(1);
            seat.DomainEvents.Single().Should().BeOfType<SeatReservedDomainEvent>();
        }

        [Fact]
        public void reserve_already_reserved_seat_should_throw_seat_already_reserved_exception()
        {
            // Arrange
            var seat = CreateSeat(isDeleted: true);

            // Act
            var act = () => seat.ReserveSeat();

            // Assert
            act.Should().Throw<SeatAlreadyReservedException>();
            seat.IsDeleted.Should().BeTrue();
            seat.DomainEvents.Should().BeEmpty();
        }

        private static global::Flight.Seats.Models.Seat CreateSeat(bool isDeleted)
        {
            var seat = global::Flight.Seats.Models.Seat.Create(
                SeatId.Of(NewId.NextGuid()),
                SeatNumber.Of("1A"),
                SeatType.Window,
                SeatClass.Economy,
                FlightId.Of(NewId.NextGuid()),
                isDeleted
            );

            seat.ClearDomainEvents();

            return seat;
        }
    }
}
