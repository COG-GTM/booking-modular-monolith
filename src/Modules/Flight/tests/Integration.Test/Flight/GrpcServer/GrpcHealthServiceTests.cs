using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Grpc.Health.V1;
using Xunit;

namespace Integration.Test.Flight.GrpcServer;

// The API host serves grpc.health.v1 for itself and the gRPC services it hosts. Booking's outbound
// readiness checks ("grpc" tag) point at https://localhost:3000, which is unreachable in the test
// host, so these only pass if those checks are excluded from the mapping.
public class GrpcHealthServiceTests : FlightIntegrationTestBase
{
    public GrpcHealthServiceTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Theory]
    [InlineData("")]
    [InlineData("flight.FlightGrpcService")]
    [InlineData("passenger.PassengerGrpcService")]
    public async Task should_report_serving_for_the_hosted_grpc_services(string service)
    {
        // Arrange
        var healthClient = new Health.HealthClient(Fixture.Channel);

        // Act
        var response = await healthClient.CheckAsync(new HealthCheckRequest { Service = service });

        // Assert
        response.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }
}
