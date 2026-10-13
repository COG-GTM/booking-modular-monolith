using System.Threading.Tasks;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Host;
using FluentAssertions;
using Grpc.Health.V1;
using Xunit;

namespace Integration.Test.Flight.GrpcServer;

// The Flight host serves grpc.health.v1 for itself and the gRPC service it hosts (Passenger's gRPC
// service now lives in the Passenger host; the composing monolith was decommissioned in AB-505).
public class GrpcHealthServiceTests : FlightIntegrationTestBase
{
    public GrpcHealthServiceTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Theory]
    [InlineData("")]
    [InlineData("flight.FlightGrpcService")]
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
