using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
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
    public async Task should_return_forbidden_when_non_admin_user_gets_another_passenger_by_id()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var nonAdminClient = Fixture.CreateHttpClient(new Dictionary<string, object>
        {
            { ClaimTypes.Name, "attacker@sample.com" },
            { ClaimTypes.Role, IdentityConstant.Role.User },
            { "scope", "flight-api" },
        });

        // Act
        var result = await nonAdminClient.GetAsync($"api/v1/passenger/{command.Id}");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_return_ok_when_admin_gets_passenger_by_id()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        var result = await Fixture.HttpClient.GetAsync($"api/v1/passenger/{command.Id}");

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
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
    }
}