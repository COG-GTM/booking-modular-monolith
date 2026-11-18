using System.Net;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Core;
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
    }

    [Fact]
    public async Task should_reject_unauthenticated_grpc_call()
    {
        var passengerGrpcClient = new PassengerGrpcService.PassengerGrpcServiceClient(Fixture.AnonymousChannel);

        var act = async () => await passengerGrpcClient.GetByIdAsync(new GetByIdRequest { Id = "1" }).ResponseAsync;

        await act.Should().ThrowAsync<RpcException>()
            .Where(exception => exception.StatusCode == StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task should_allow_anonymous_access_to_public_endpoints()
    {
        using var anonymousHttpClient = Fixture.AnonymousHttpClient;
        var rootResponse = await anonymousHttpClient.GetAsync("/");
        rootResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var developmentAnonymousHttpClient = Fixture.DevelopmentAnonymousHttpClient;
        var healthResponse = await developmentAnonymousHttpClient.GetAsync("/health");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}