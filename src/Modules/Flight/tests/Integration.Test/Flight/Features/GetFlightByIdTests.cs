using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Flight.Features;

using global::Flight.Flights.Features.GettingFlightById.V1;

public class GetFlightByIdTests : FlightIntegrationTestBase
{
    public GetFlightByIdTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_retrive_a_flight_by_id_currectly()
    {
        //Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var query = new GetFlightById(command.Id);

        // Act
        var response = await Fixture.SendAsync(query);

        // Assert
        response.Should().NotBeNull();
        response?.FlightDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_retrive_a_flight_by_id_from_grpc_service()
    {
        //Arrange
        var command = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await flightGrpcClient.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() }).ResponseAsync;

        // Assert
        response?.Should().NotBeNull();
        response?.FlightDto.Id.Should().Be(command.Id.ToString());
    }

    [Fact]
    public async Task should_reject_anonymous_get_flight_by_id_request()
    {
        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.AnonymousChannel);

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => flightGrpcClient.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() }).ResponseAsync
        );

        exception.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }
}
