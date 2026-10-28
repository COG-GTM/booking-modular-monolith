using BuildingBlocks.TestBase;
using Flight.Data;
using Grpc.Health.V1;
using Xunit;

namespace Flight.Host.Test.Grpc;

public sealed class FlightGrpcHealthTests(
    TestFixture<global::Flight.Host.Program, FlightDbContext, FlightReadDbContext> fixture
) : FlightHostTestBase(fixture)
{
    [Theory]
    [InlineData("")]
    [InlineData("flight.FlightGrpcService")]
    public async Task health_service_reports_serving(string service)
    {
        var health = new global::Grpc.Health.V1.Health.HealthClient(Fixture.Channel);
        var response = await health.CheckAsync(new HealthCheckRequest { Service = service });

        Assert.Equal(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);
    }
}
