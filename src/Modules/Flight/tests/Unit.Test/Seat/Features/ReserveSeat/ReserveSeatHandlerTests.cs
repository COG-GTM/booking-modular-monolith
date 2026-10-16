namespace Unit.Test.Seat.Features.ReserveSeat;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.ReservingSeat.V1;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class ReserveSeatHandlerTests
{
    [Fact]
    public async Task handler_reserves_matching_seat_and_queues_reserved_event()
    {
        using var context = DbContextFactory.Create();
        var seat = await context.Seats.FirstAsync();
        seat.ClearDomainEvents();
        var handler = new ReserveSeatCommandHandler(context);

        var result = await handler.Handle(
            new ReserveSeat(seat.FlightId.Value, seat.SeatNumber.Value),
            CancellationToken.None
        );

        result.Id.Should().Be(seat.Id.Value);
        seat.IsDeleted.Should().BeTrue();
        seat.LastModified.Should().NotBeNull();
        var reservedEvent = seat
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<SeatReservedDomainEvent>()
            .Subject;
        reservedEvent.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task handler_rejects_a_seat_that_has_already_been_reserved()
    {
        using var context = DbContextFactory.Create();
        var seat = await context.Seats.FirstAsync();
        seat.IsDeleted = true;
        await context.SaveChangesAsync();
        var command = new ReserveSeat(seat.FlightId.Value, seat.SeatNumber.Value);
        var handler = new ReserveSeatCommandHandler(context);

        var act = () => handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<SeatNumberIncorrectException>();
    }

    [Fact]
    public async Task handler_with_unknown_seat_number_should_throw_incorrect_seat_exception()
    {
        using var context = DbContextFactory.Create();
        var seat = await context.Seats.FirstAsync();
        var handler = new ReserveSeatCommandHandler(context);

        var act = () => handler.Handle(new ReserveSeat(seat.FlightId.Value, "99Z"), CancellationToken.None);

        await act.Should().ThrowAsync<SeatNumberIncorrectException>();
    }

    [Fact]
    public async Task handler_with_unknown_flight_id_should_throw_incorrect_seat_exception()
    {
        using var context = DbContextFactory.Create();
        var seat = await context.Seats.FirstAsync();
        var handler = new ReserveSeatCommandHandler(context);

        var act = () => handler.Handle(new ReserveSeat(Guid.NewGuid(), seat.SeatNumber.Value), CancellationToken.None);

        await act.Should().ThrowAsync<SeatNumberIncorrectException>();
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_null()
    {
        using var context = DbContextFactory.Create();
        var handler = new ReserveSeatCommandHandler(context);

        var act = () => handler.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void validator_rejects_empty_flight_id_and_seat_number()
    {
        var result = new ReserveSeatValidator().Validate(new ReserveSeat(Guid.Empty, ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ReserveSeat.FlightId));
        result.Errors.Should().Contain(error => error.PropertyName == nameof(ReserveSeat.SeatNumber));
    }
}
