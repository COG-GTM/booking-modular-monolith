using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Unit.Test.Seat.Features;

using global::Flight.Data;
using global::Flight.Flights.ValueObjects;
using global::Flight.Seats.Enums;
using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.ReservingSeat.V1;
using global::Flight.Seats.ValueObjects;
using MassTransit;

public class ReserveSeatConcurrencyTests : IDisposable
{
    private readonly DbContextOptions<FlightDbContext> _options = new DbContextOptionsBuilder<FlightDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private readonly Guid _flightId = NewId.NextGuid();
    private const string SeatNumberValue = "33C";

    public ReserveSeatConcurrencyTests()
    {
        using var context = new FlightDbContext(_options);
        context.Seats.Add(global::Flight.Seats.Models.Seat.Create(SeatId.Of(NewId.NextGuid()),
            SeatNumber.Of(SeatNumberValue), SeatType.Aisle, SeatClass.Economy, FlightId.Of(_flightId)));
        context.SaveChanges();
    }

    [Fact]
    public async Task second_writer_with_stale_seat_version_should_get_concurrency_exception()
    {
        // Arrange: both requests read the free seat before either one saves
        await using var first = new FlightDbContext(_options);
        await using var second = new FlightDbContext(_options);

        var firstResult = new ReserveSeatCommandHandler(first)
            .Handle(new ReserveSeat(_flightId, SeatNumberValue), CancellationToken.None);
        var secondResult = new ReserveSeatCommandHandler(second)
            .Handle(new ReserveSeat(_flightId, SeatNumberValue), CancellationToken.None);
        await Task.WhenAll(firstResult, secondResult);

        // Act
        await first.SaveChangesAsync();
        var act = () => second.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task reserving_an_already_reserved_seat_should_throw_conflict()
    {
        // Arrange
        await using (var first = new FlightDbContext(_options))
        {
            await new ReserveSeatCommandHandler(first)
                .Handle(new ReserveSeat(_flightId, SeatNumberValue), CancellationToken.None);
            await first.SaveChangesAsync();
        }

        await using var second = new FlightDbContext(_options);

        // Act
        var act = () => new ReserveSeatCommandHandler(second)
            .Handle(new ReserveSeat(_flightId, SeatNumberValue), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<SeatAlreadyReservedException>();
    }

    public void Dispose()
    {
        using var context = new FlightDbContext(_options);
        context.Database.EnsureDeleted();
    }
}
