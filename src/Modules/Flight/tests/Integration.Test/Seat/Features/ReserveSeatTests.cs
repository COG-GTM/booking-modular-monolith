using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using Flight.Seats.ValueObjects;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Seat.Features;

public class ReserveSeatTests : FlightIntegrationTestBase
{
    public ReserveSeatTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

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
        var response = await flightGrpcClient.ReserveSeatAsync(new ReserveSeatRequest()
        {
            FlightId = seatCommand.FlightId.ToString(),
            SeatNumber = seatCommand.SeatNumber
        });

        // Assert
        response?.Should().NotBeNull();
        response?.Id.Should().Be(seatCommand?.Id.ToString());
    }

    [Fact]
    public async Task should_reject_reserving_an_already_reserved_seat()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);
        var request = new ReserveSeatRequest()
        {
            FlightId = seatCommand.FlightId.ToString(),
            SeatNumber = seatCommand.SeatNumber
        };

        await flightGrpcClient.ReserveSeatAsync(request);

        // Act
        var act = async () => { await flightGrpcClient.ReserveSeatAsync(request); };

        // Assert
        await act.Should().ThrowAsync<RpcException>();
    }

    [Fact]
    public async Task should_reserve_a_seat_only_once_when_reserved_concurrently()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var attempts = Enumerable.Range(0, 10)
            .Select(async _ =>
            {
                try
                {
                    await flightGrpcClient.ReserveSeatAsync(new ReserveSeatRequest()
                    {
                        FlightId = seatCommand.FlightId.ToString(),
                        SeatNumber = seatCommand.SeatNumber
                    });

                    return true;
                }
                catch (RpcException)
                {
                    return false;
                }
            });

        var results = await Task.WhenAll(attempts);

        // Assert
        results.Count(succeeded => succeeded).Should().Be(1);

        var seat = await Fixture.ExecuteDbContextAsync(db =>
            db.Seats.IgnoreQueryFilters().SingleAsync(x => x.Id == SeatId.Of(seatCommand.Id)));

        seat.IsDeleted.Should().BeTrue();
        seat.Version.Should().Be(1);
    }
}