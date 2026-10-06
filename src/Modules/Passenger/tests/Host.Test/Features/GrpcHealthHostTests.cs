using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Health.V1;
using Passenger.Data;
using Xunit;

namespace Host.Test.Features;

public class GrpcHealthHostTests : PassengerHostTestBase
{
    public GrpcHealthHostTests(
        TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task grpc_health_service_should_report_serving_from_passenger_host()
    {
        // Arrange
        var healthClient = new Health.HealthClient(Fixture.Channel);

        // Act
        var response = await healthClient.CheckAsync(new HealthCheckRequest());

        // Assert
        response.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }
}
