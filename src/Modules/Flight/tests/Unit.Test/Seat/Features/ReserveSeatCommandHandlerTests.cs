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
    public async Task handler_with_valid_command_should_reserve_available_seat()
    {
        // Arrange
        var seat = await _fixture.DbContext.Seats.FirstAsync();
        var command = new ReserveSeat(seat.FlightId, seat.SeatNumber);

        // Act
        var response = await Act(command, CancellationToken.None);
        await _fixture.DbContext.SaveChangesAsync();

        // Assert
        response.Id.Should().Be(seat.Id);
        var entity = await _fixture.DbContext.Seats.IgnoreQueryFilters().SingleAsync(x => x.Id == seat.Id);
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task handler_with_already_reserved_seat_should_throw_seat_already_reserved_exception()
    {
        // Arrange
        var seat = await _fixture.DbContext.Seats.FirstAsync();
        var command = new ReserveSeat(seat.FlightId, seat.SeatNumber);

        await Act(command, CancellationToken.None);
        await _fixture.DbContext.SaveChangesAsync();

        // Act
        var act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<SeatAlreadyReservedException>();
    }

    [Fact]
    public async Task handler_with_unknown_seat_should_throw_seat_number_incorrect_exception()
    {
        // Arrange
        var seat = await _fixture.DbContext.Seats.FirstAsync();
        var command = new ReserveSeat(seat.FlightId, "ZZ99");

        // Act
        var act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<SeatNumberIncorrectException>();
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_exception()
    {
        // Arrange
        ReserveSeat command = null;

        // Act
        var act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task concurrent_reservations_of_same_seat_should_only_commit_once()
    {
        // Arrange: two independent contexts (two concurrent requests) over the same database
        var databaseName = Guid.NewGuid().ToString();
        await using var seedContext = DbContextFactory.Create();
        var seededSeat = await seedContext.Seats.FirstAsync();

        await using var firstContext = DbContextFactory.Create(databaseName);
        await using var secondContext = DbContextFactory.Create(databaseName);

        firstContext.Seats.Add(
            global::Flight.Seats.Models.Seat.Create(
                seededSeat.Id,
                seededSeat.SeatNumber,
                seededSeat.Type,
                seededSeat.Class,
                seededSeat.FlightId
            )
        );
        await firstContext.SaveChangesAsync();
        firstContext.ChangeTracker.Clear();

        var command = new ReserveSeat(seededSeat.FlightId, seededSeat.SeatNumber);

        // Act: both requests read the seat as available before either one commits
        await new ReserveSeatCommandHandler(firstContext).Handle(command, CancellationToken.None);
        await new ReserveSeatCommandHandler(secondContext).Handle(command, CancellationToken.None);

        await firstContext.SaveChangesAsync();
        var secondCommit = async () =>
        {
            await secondContext.SaveChangesAsync();
        };

        // Assert: the second writer must lose instead of silently overwriting the first reservation
        await secondCommit.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verifyContext = DbContextFactory.Create(databaseName);
        var entity = await verifyContext.Seats.IgnoreQueryFilters().SingleAsync(x => x.Id == seededSeat.Id);
        entity.IsDeleted.Should().BeTrue();
        entity.Version.Should().Be(1);
    }
}
