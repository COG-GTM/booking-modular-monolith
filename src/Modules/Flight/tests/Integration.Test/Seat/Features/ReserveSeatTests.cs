using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
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
    public async Task should_reject_anonymous_reserve_seat_request()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.AnonymousChannel);

        // Act
        var exception = await Assert.ThrowsAsync<RpcException>(
            () =>
                flightGrpcClient
                    .ReserveSeatAsync(
                        new ReserveSeatRequest
                        {
                            FlightId = seatCommand.FlightId.ToString(),
                            SeatNumber = seatCommand.SeatNumber,
                        }
                    )
                    .ResponseAsync
        );

        // Assert
        exception.StatusCode.Should().Be(StatusCode.Unauthenticated);
        var seat = await Fixture.ExecuteDbContextAsync<global::Flight.Seats.Models.Seat>(db =>
            db.Seats.SingleAsync(x => x.Id == seatCommand.Id)
        );
        seat.IsDeleted.Should().BeFalse();
    }
}
