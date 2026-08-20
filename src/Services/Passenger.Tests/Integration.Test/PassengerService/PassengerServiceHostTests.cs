using System.Net;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Passenger;
using Passenger.Data;
using PassengerService;
using Xunit;

namespace Integration.Test.Host;

using global::Passenger.Passengers.Features.GettingPassengerById.V1;

public class PassengerServiceHostTests : PassengerServiceIntegrationTestBase
{
    public PassengerServiceHostTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task root_endpoint_should_return_passenger_service_app_name()
    {
        // Act
        var response = await Fixture.HttpClient.GetStringAsync(new Uri("/", UriKind.Relative));

        // Assert
        response.Should().Be("Passenger-Service");
    }

    [Fact]
    public async Task should_resolve_passenger_module_and_retrieve_passenger_by_id_through_mediator()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var query = new GetPassengerById(command.Id);

        // Act
        var response = await Fixture.SendAsync(query);

        // Assert
        response.Should().NotBeNull();
        response?.PassengerDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_retrieve_passenger_by_id_through_http_minimal_endpoint()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        var response = await Fixture.HttpClient.GetAsync(new Uri($"api/v1.0/passenger/{command.Id}", UriKind.Relative));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_retrieve_passenger_by_id_through_grpc_service()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var passengerGrpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(Fixture.Channel);

        // Act
        var response = await passengerGrpcClient.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        // Assert
        response?.Should().NotBeNull();
        response?.PassengerDto?.Id.Should().Be(command.Id.ToString());
    }
}
