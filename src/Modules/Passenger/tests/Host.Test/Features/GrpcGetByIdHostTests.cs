using BuildingBlocks.TestBase;
using Contracts.Grpc.Passenger.V1;
using FluentAssertions;
using Integration.Test.Fakes;
using Passenger.Data;
using Xunit;

namespace Host.Test.Features;

public class GrpcGetByIdHostTests : PassengerHostTestBase
{
    public GrpcGetByIdHostTests(
        TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_serve_grpc_get_by_id_from_passenger_host()
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
