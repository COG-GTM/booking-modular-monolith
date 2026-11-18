using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using global::Passenger.Passengers.Exceptions;
using Integration.Test.Fakes;
using Passenger;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.GettingPassengerById.V1;

public class GetPassengerByIdTests : PassengerIntegrationTestBase
{
    public GetPassengerByIdTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_retrive_a_passenger_by_id_currectly()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();

        await Fixture.SendAsync(command);

        var query = new GetPassengerById(command.Id, command.UserId, false);

        // Act
        var response = await Fixture.SendAsync(query);

        // Assert
        response.Should().NotBeNull();
        response?.PassengerDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_not_retrive_a_passenger_by_id_for_non_owner()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(command);

        var query = new GetPassengerById(command.Id, Guid.NewGuid(), false);

        var act = async () => await Fixture.SendAsync(query);

        await act.Should().ThrowAsync<PassengerNotFoundException>();
    }

    [Fact]
    public async Task should_retrive_a_passenger_by_id_for_admin()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = null };
        await Fixture.SendAsync(command);

        var query = new GetPassengerById(command.Id, null, true);

        var response = await Fixture.SendAsync(query);

        response?.PassengerDto?.Id.Should().Be(command.Id);
    }

    [Fact]
    public async Task should_not_retrive_a_passenger_by_id_without_identity_or_admin_role()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = null };
        await Fixture.SendAsync(command);

        var query = new GetPassengerById(command.Id, null, false);

        var act = async () => await Fixture.SendAsync(query);

        await act.Should().ThrowAsync<PassengerNotFoundException>();
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
