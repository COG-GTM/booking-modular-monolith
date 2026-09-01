namespace Unit.Test.Seat.Features;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.ReservingSeat.V1;
using global::Flight.Seats.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class ReserveSeatCommandHandlerTests
{
    private readonly UnitTestFixture _fixture;
    private readonly ReserveSeatCommandHandler _handler;

    public Task<ReserveSeatResult> Act(ReserveSeat command, CancellationToken cancellationToken) =>
        _handler.Handle(command, cancellationToken);

    public ReserveSeatCommandHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
        _handler = new ReserveSeatCommandHandler(fixture.DbContext);
    }

    [Fact]
    public async Task handler_with_valid_command_should_reserve_seat_and_return_correct_id()
    {
        // Arrange
        var existingSeat = await _fixture.DbContext.Seats
            .FirstOrDefaultAsync(x => !x.IsDeleted);
        existingSeat.Should().NotBeNull();

        var command = new ReserveSeat(existingSeat!.FlightId, existingSeat.SeatNumber);

        // Act
        var response = await Act(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be(existingSeat.Id);

        var updatedSeat = await _fixture.DbContext.Seats.FindAsync(SeatId.Of(response.Id));
        updatedSeat.Should().NotBeNull();
        updatedSeat!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task handler_with_incorrect_seat_number_should_throw_seat_number_incorrect_exception()
    {
        // Arrange
        var existingSeat = await _fixture.DbContext.Seats.FirstOrDefaultAsync();
        existingSeat.Should().NotBeNull();

        var command = new ReserveSeat(existingSeat!.FlightId, "ZZ99");

        // Act
        Func<Task> act = async () => { await Act(command, CancellationToken.None); };

        // Assert
        await act.Should().ThrowAsync<SeatNumberIncorrectException>();
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_exception()
    {
        // Arrange
        ReserveSeat command = null;

        // Act
        Func<Task> act = async () => { await Act(command, CancellationToken.None); };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
