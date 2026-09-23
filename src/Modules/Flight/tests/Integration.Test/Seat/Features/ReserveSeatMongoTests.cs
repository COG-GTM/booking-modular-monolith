using System;
using System.Linq;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Xunit;

namespace Integration.Test.Seat.Features;

using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.GettingAvailableSeats.V1;

public class ReserveSeatMongoTests : FlightIntegrationTestBase
{
    public ReserveSeatMongoTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_mark_seat_read_model_as_deleted_and_exclude_it_from_available_seats()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var reservedSeatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id)
            .RuleFor(r => r.SeatNumber, _ => "12A")
            .Generate();
        var otherSeatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id)
            .RuleFor(r => r.SeatNumber, _ => "12B")
            .Generate();

        await Fixture.SendAsync(reservedSeatCommand);
        await Fixture.SendAsync(otherSeatCommand);

        var reserveCommand = new FakeReserveSeatMongoCommand(reservedSeatCommand).Generate();

        // Act
        await Fixture.SendAsync(reserveCommand);

        // Assert
        var reservedSeat = await Fixture.ExecuteReadContextAsync(db =>
            db.Seat.AsQueryable().SingleAsync(x => x.SeatId == reservedSeatCommand.Id)
        );

        reservedSeat.IsDeleted.Should().BeTrue();
        reservedSeat.SeatNumber.Should().Be(reservedSeatCommand.SeatNumber);
        reservedSeat.FlightId.Should().Be(flightCommand.Id);

        var otherSeat = await Fixture.ExecuteReadContextAsync(db =>
            db.Seat.AsQueryable().SingleAsync(x => x.SeatId == otherSeatCommand.Id)
        );

        otherSeat.IsDeleted.Should().BeFalse();

        var availableSeats = (await Fixture.SendAsync(new GetAvailableSeats(flightCommand.Id)))?.SeatDtos?.ToList();

        availableSeats.Should().NotBeNull();
        availableSeats.Should().ContainSingle(x => x.SeatNumber == otherSeatCommand.SeatNumber);
        availableSeats.Should().NotContain(x => x.SeatNumber == reservedSeatCommand.SeatNumber);
    }

    [Fact]
    public async Task should_throw_all_seats_full_exception_when_only_seat_is_reserved()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        // Act
        await Fixture.SendAsync(new FakeReserveSeatMongoCommand(seatCommand).Generate());

        // Assert
        Func<Task> act = async () => await Fixture.SendAsync(new GetAvailableSeats(flightCommand.Id));

        await act.Should().ThrowAsync<AllSeatsFullException>();
    }

    [Fact]
    public async Task should_not_modify_any_seat_read_model_when_seat_does_not_exist()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var unknownSeatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        // Act
        await Fixture.SendAsync(new FakeReserveSeatMongoCommand(unknownSeatCommand).Generate());

        // Assert
        var seats = await Fixture.ExecuteReadContextAsync(db =>
            db.Seat.AsQueryable().Where(x => x.FlightId == flightCommand.Id).ToListAsync()
        );

        seats.Should().ContainSingle();
        seats.Single().SeatId.Should().Be(seatCommand.Id);
        seats.Single().IsDeleted.Should().BeFalse();
    }
}
