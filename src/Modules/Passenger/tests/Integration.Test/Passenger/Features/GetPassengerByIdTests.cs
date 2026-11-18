using System.Net;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Passenger;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.GettingPassengerById.V1;

public class GetPassengerByIdTests : PassengerIntegrationTestBase
{
    public GetPassengerByIdTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_retrive_a_passenger_by_id_currectly()
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
    public async Task should_retrive_a_passenger_by_id_from_grpc_service()
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
        response?.PassengerDto?.UserId.Should().Be(command.UserId.ToString());
    }

    [Fact]
    public async Task should_forbid_reading_another_users_passenger_over_http()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var httpClient = Fixture.CreateHttpClient(IdentityConstant.Role.User, Guid.NewGuid());

        // Act
        var result = await httpClient.GetAsync($"api/v1.0/passenger/{command.Id}");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_allow_owner_and_admin_to_read_passenger_over_http()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var ownerClient = Fixture.CreateHttpClient(IdentityConstant.Role.User, command.UserId);

        // Act
        var ownerResult = await ownerClient.GetAsync($"api/v1.0/passenger/{command.Id}");
        var adminResult = await Fixture.HttpClient.GetAsync($"api/v1.0/passenger/{command.Id}");

        // Assert
        ownerResult.StatusCode.Should().Be(HttpStatusCode.OK);
        adminResult.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}