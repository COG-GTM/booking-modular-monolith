using Api;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Health.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Passenger;
using Xunit;

namespace Integration.Test;

public class GrpcHealthServiceTests : FlightIntegrationTestBase
{
    public GrpcHealthServiceTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    public static TheoryData<string> MappedServices =>
        new() { string.Empty, FlightGrpcService.Descriptor.FullName, PassengerGrpcService.Descriptor.FullName };

    [Theory]
    [MemberData(nameof(MappedServices))]
    public async Task grpc_health_service_ignores_checks_on_other_grpc_services(string service)
    {
        var dependencies = await Fixture
            .ServiceProvider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Tags.Contains(GrpcClientExtensions.GrpcDependencyHealthTag));
        dependencies.Entries.Keys.Should().BeEquivalentTo("flight-grpc", "passenger-grpc");
        dependencies.Status.Should().Be(HealthStatus.Unhealthy, "no gRPC listener runs behind the test server");

        var client = new Health.HealthClient(Fixture.Channel);
        var response = await client.CheckAsync(new HealthCheckRequest { Service = service });

        response.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }
}
