using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
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
        response?.UserId.Should().Be(command.UserId);
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

    [Fact]
    public async Task should_allow_owner_to_get_their_own_passenger_over_http()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var client = CreateUserHttpClient(command.UserId);

        // Act
        var result = await client.GetAsync(Route(command.Id));

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_forbid_non_owner_from_getting_another_passenger_over_http()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var client = CreateUserHttpClient(Guid.CreateVersion7());

        // Act
        var result = await client.GetAsync(Route(command.Id));

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_allow_admin_to_get_any_passenger_over_http()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        // Act
        var result = await Fixture.HttpClient.GetAsync(Route(command.Id));

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_forbid_non_owner_from_getting_another_passenger_from_grpc_service()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var client = CreateUserHttpClient(Guid.CreateVersion7());
        var passengerGrpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(
            GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions { HttpClient = client }));

        // Act
        var act = async () => await passengerGrpcClient.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task should_allow_owner_to_get_their_own_passenger_from_grpc_service()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var client = CreateUserHttpClient(command.UserId);
        var passengerGrpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(
            GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions { HttpClient = client }));

        // Act
        var response = await passengerGrpcClient.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        // Assert
        response.PassengerDto.Id.Should().Be(command.Id.ToString());
    }

    private static string Route(Guid passengerId) => $"api/v1/passenger/{passengerId}";

    private HttpClient CreateUserHttpClient(Guid userId)
    {
        return Fixture.CreateHttpClient(new Dictionary<string, object>
        {
            { "sub", userId.ToString() },
            { ClaimTypes.Name, "user@sample.com" },
            { ClaimTypes.Role, "user" },
            { "scope", "flight-api" },
        });
    }
}
