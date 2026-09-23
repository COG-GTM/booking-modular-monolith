using System;
using System.Linq;
using System.Threading.Tasks;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Integration.Test.Seat.Features;

using global::Flight.Seats.Exceptions;
using global::Flight.Seats.Features.ReservingSeat.V1;
using global::Flight.Seats.ValueObjects;

public class ReserveSeatTests : FlightIntegrationTestBase
{
    public ReserveSeatTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_return_valid_reserve_seat_from_grpc_service()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await flightGrpcClient.ReserveSeatAsync(
            new ReserveSeatRequest() { FlightId = seatCommand.FlightId.ToString(), SeatNumber = seatCommand.SeatNumber }
        );

        // Assert
        response?.Should().NotBeNull();
        response?.Id.Should().Be(seatCommand?.Id.ToString());
    }

    [Fact]
    public async Task should_mark_seat_as_reserved_in_db()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var command = new ReserveSeat(seatCommand.FlightId, seatCommand.SeatNumber);

        // Act
        var response = await Fixture.SendAsync(command);

        var reservedSeat = (
            await Fixture.ExecuteDbContextAsync(db =>
                db.Seats.Where(x => x.Id == SeatId.Of(seatCommand.Id)).IgnoreQueryFilters().ToListAsync()
            )
        ).FirstOrDefault();

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be(seatCommand.Id);
        reservedSeat.Should().NotBeNull();
        reservedSeat!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_seat_number_incorrect_exception_when_seat_number_does_not_exist()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var command = new ReserveSeat(seatCommand.FlightId, $"{seatCommand.SeatNumber}-missing");

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<SeatNumberIncorrectException>();
    }

    [Fact]
    public async Task should_throw_seat_number_incorrect_exception_when_flight_id_does_not_match()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var command = new ReserveSeat(NewId.NextGuid(), seatCommand.SeatNumber);

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<SeatNumberIncorrectException>();
    }
}
