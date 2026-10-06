using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Seat.Features;

using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.ReservingSeat.V1;

[Collection(nameof(UnitTestFixture))]
public class ReserveSeatCommandHandlerTests
{
    private readonly UnitTestFixture _fixture;
    private readonly ReserveSeatCommandHandler _handler;

    public ReserveSeatCommandHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
        _handler = new ReserveSeatCommandHandler(_fixture.DbContext);
    }

    public Task<ReserveSeatResult> Act(ReserveSeat command, CancellationToken cancellationToken)
    {
        return _handler.Handle(command, cancellationToken);
    }

    [Fact]
    public async Task handler_with_available_seat_should_reserve_seat_and_return_seat_id()
    {
        // Arrange
        var seat = await _fixture.DbContext.Seats.FirstAsync(x => x.SeatNumber.Value == "12A");
        var command = new ReserveSeat(seat.FlightId, seat.SeatNumber.Value);

        // Act
        var response = await Act(command, CancellationToken.None);

        // Assert
        response.Id.Should().Be(seat.Id.Value);
        seat.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task handler_with_already_reserved_seat_should_throw_seat_already_reserved_exception()
    {
        // Arrange
        var seat = await _fixture.DbContext.Seats.FirstAsync(x => x.SeatNumber.Value == "12B");
        var command = new ReserveSeat(seat.FlightId, seat.SeatNumber.Value);

        await Act(command, CancellationToken.None);
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        var act = async () => { await Act(command, CancellationToken.None); };

        // Assert
        await act.Should().ThrowAsync<SeatAlreadyReservedException>();
    }

    [Fact]
    public async Task handler_with_unknown_seat_should_throw_seat_number_incorrect_exception()
    {
        // Arrange
        var seat = await _fixture.DbContext.Seats.FirstAsync(x => x.SeatNumber.Value == "12C");
        var command = new ReserveSeat(seat.FlightId, "99Z");

        // Act
        var act = async () => { await Act(command, CancellationToken.None); };

        // Assert
        await act.Should().ThrowAsync<SeatNumberIncorrectException>();
    }
}
