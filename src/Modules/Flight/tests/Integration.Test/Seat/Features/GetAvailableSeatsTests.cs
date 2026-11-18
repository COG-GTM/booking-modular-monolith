using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using MongoDB.Driver;
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
    public async Task should_return_only_seats_of_requested_flight()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var otherFlightCommand = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(otherFlightCommand);

        var seatCommand = new FakeCreateSeatMongoCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        for (var i = 0; i < 5; i++)
        {
            await Fixture.SendAsync(new FakeCreateSeatMongoCommand(otherFlightCommand.Id).Generate());
        }

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await flightGrpcClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = flightCommand.Id.ToString() });

        // Assert
        response.SeatDtos.Should().ContainSingle();
        response.SeatDtos.Single().FlightId.Should().Be(flightCommand.Id.ToString());
    }

    [Fact]
    public async Task should_reject_empty_flight_id_from_grpc_service()
    {
        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        var act = async () => await flightGrpcClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = Guid.Empty.ToString() });

        await act.Should().ThrowAsync<RpcException>();
    }

    [Fact]
    public async Task should_have_flight_id_index_on_seat_collection()
    {
        var indexNames = await Fixture.ExecuteReadContextAsync(async readDbContext =>
        {
            var indexes = await readDbContext.Seat.Indexes.ListAsync();
            return (await indexes.ToListAsync()).Select(x => x["name"].AsString).ToList();
        });

        indexNames.Should().Contain("flightId_isDeleted");
    }
}
