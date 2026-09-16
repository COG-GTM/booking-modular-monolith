using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Passenger;
using Passenger.Data;
using Passenger.Passengers.Exceptions;
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
    public async Task should_throw_not_found_when_passenger_does_not_exist()
    {
        // Arrange
        var query = new GetPassengerById(Guid.NewGuid());

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(query);

        // Assert
        await act.Should().ThrowAsync<PassengerNotFoundException>();
    }
}
