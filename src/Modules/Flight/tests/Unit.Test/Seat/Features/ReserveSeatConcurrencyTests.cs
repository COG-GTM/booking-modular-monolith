using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Unit.Test.Seat.Features;

using global::Flight.Data;
using global::Flight.Flights.ValueObjects;
using global::Flight.Seats.Enums;
using global::Flight.Seats.Features.ReservingSeat.V1;
using global::Flight.Seats.ValueObjects;
using MassTransit;

public class ReserveSeatConcurrencyTests
{
    [Fact]
    public async Task concurrent_reservations_of_same_seat_should_fail_closed_with_concurrency_exception()
    {
        // Arrange
        var databaseName = Guid.NewGuid().ToString();
        var flightId = NewId.NextGuid();
        var seatId = SeatId.Of(NewId.NextGuid());

        await using (var seedContext = CreateContext(databaseName))
        {
            seedContext.Seats.Add(
                global::Flight.Seats.Models.Seat.Create(
                    seatId,
                    SeatNumber.Of("7A"),
                    SeatType.Window,
                    SeatClass.Economy,
                    FlightId.Of(flightId)
                )
            );
            await seedContext.SaveChangesAsync();
        }

        var command = new ReserveSeat(flightId, "7A");

        await using var firstContext = CreateContext(databaseName);
        await using var secondContext = CreateContext(databaseName);

        // Both requests read the seat at Version 0 before either one saves.
        await new ReserveSeatCommandHandler(firstContext).Handle(command, CancellationToken.None);
        await new ReserveSeatCommandHandler(secondContext).Handle(command, CancellationToken.None);

        // Act
        await firstContext.SaveChangesAsync();
        var act = async () =>
        {
            await secondContext.SaveChangesAsync();
        };

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verifyContext = CreateContext(databaseName);
        var seat = await verifyContext.Seats.IgnoreQueryFilters().SingleAsync(x => x.Id == seatId);

        seat.IsDeleted.Should().BeTrue();
        seat.Version.Should().Be(1);
    }

    [Fact]
    public async Task save_changes_without_conflicting_writer_should_increment_version_and_succeed()
    {
        // Arrange
        var databaseName = Guid.NewGuid().ToString();
        var flightId = NewId.NextGuid();
        var seatId = SeatId.Of(NewId.NextGuid());

        await using (var seedContext = CreateContext(databaseName))
        {
            seedContext.Seats.Add(
                global::Flight.Seats.Models.Seat.Create(
                    seatId,
                    SeatNumber.Of("7B"),
                    SeatType.Aisle,
                    SeatClass.Economy,
                    FlightId.Of(flightId)
                )
            );
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(databaseName);
        await new ReserveSeatCommandHandler(context).Handle(new ReserveSeat(flightId, "7B"), CancellationToken.None);

        // Act
        var affectedRows = await context.SaveChangesAsync();

        // Assert
        affectedRows.Should().Be(1);

        await using var verifyContext = CreateContext(databaseName);
        var seat = await verifyContext.Seats.IgnoreQueryFilters().SingleAsync(x => x.Id == seatId);

        seat.IsDeleted.Should().BeTrue();
        seat.Version.Should().Be(1);
    }

    private static FlightDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<FlightDbContext>().UseInMemoryDatabase(databaseName).Options;

        return new FlightDbContext(options, currentUserProvider: null, null);
    }
}
