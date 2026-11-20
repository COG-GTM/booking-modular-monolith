using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Seat.Features;


public class GetAvailableSeatsTests : FlightIntegrationTestBase
{
    public GetAvailableSeatsTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_return_available_seats_from_grpc_service()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();

        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await flightGrpcClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = flightCommand.Id.ToString() });

        // Assert
        response?.Should().NotBeNull();
        response?.SeatDtos?.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_return_only_non_deleted_seats_of_requested_flight()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();
        var otherFlightCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(flightCommand);
        await Fixture.SendAsync(otherFlightCommand);

        var availableSeat = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();
        var deletedSeat = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate() with { IsDeleted = true };
        var otherFlightSeat = new FakeCreateSeatMongoCommand(otherFlightCommand.Id).Generate();

        await Fixture.SendAsync(availableSeat);
        await Fixture.SendAsync(deletedSeat);
        await Fixture.SendAsync(otherFlightSeat);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await flightGrpcClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = flightCommand.Id.ToString() });

        // Assert
        response.SeatDtos.Should().ContainSingle();
        response.SeatDtos[0].FlightId.Should().Be(flightCommand.Id.ToString());
        response.SeatDtos[0].SeatNumber.Should().Be(availableSeat.SeatNumber);
    }
}
