using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Seat.Features;

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
    public async Task should_reject_unauthenticated_reserve_seat_from_grpc_service()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.UnauthenticatedChannel);

        // Act
        var act = async () =>
            await flightGrpcClient.ReserveSeatAsync(
                new ReserveSeatRequest()
                {
                    FlightId = seatCommand.FlightId.ToString(),
                    SeatNumber = seatCommand.SeatNumber,
                }
            );

        // Assert
        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should()
            .Be(StatusCode.Unauthenticated);

        var seat = await Fixture.FindAsync<global::Flight.Seats.Models.Seat, global::Flight.Seats.ValueObjects.SeatId>(
            global::Flight.Seats.ValueObjects.SeatId.Of(seatCommand.Id)
        );
        seat?.IsDeleted.Should().BeFalse();
    }
}
