using System.Net;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using Flight.Data.Seed;
using Flight.Host.Extensions;
using FluentAssertions;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyModel;
using Xunit;
using Integration.Test;

namespace Host.Test;

public class FlightHostCompositionTests
{
    [Fact]
    public void runtime_dependencies_include_flight_but_no_other_module_or_aggregate()
    {
        var libraries = DependencyContext
            .Load(typeof(Program).Assembly)!
            .RuntimeLibraries.Select(library => library.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        libraries.Should().Contain("Flight");
        libraries.Should().NotContain("Api");
        libraries.Should().NotContain("Identity");
        libraries.Should().NotContain("Passenger");
        libraries.Should().NotContain("Booking");
        libraries.Should().NotContain("BuildingBlocks");
        libraries.Should().NotContain("BuildingBlocks.EventStoreDB");
    }
}

public class FlightHostEndpointTests : FlightIntegrationTestBase
{
    public FlightHostEndpointTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task flight_rest_route_succeeds_and_non_flight_routes_are_not_mapped()
    {
        var flightId = (Guid)InitialData.Flights[0].Id;
        using var client = Fixture.HttpClient;
        using var flightResponse = await client.GetAsync($"/api/v1/flight/{flightId}");
        flightResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var passengerResponse = await client.GetAsync("/api/v1/passenger");
        passengerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var bookingResponse = await client.GetAsync("/api/v1/booking");
        bookingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var identityResponse = await client.GetAsync("/api/v1/identity");
        identityResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task grpc_flight_methods_and_health_checks_report_serving()
    {
        using var client = Fixture.HttpClient;
        using var channel = GrpcChannel.ForAddress(
            client.BaseAddress!,
            new GrpcChannelOptions { HttpClient = client }
        );
        var flightClient = new FlightGrpcService.FlightGrpcServiceClient(channel);
        var seededFlightId = ((Guid)InitialData.Flights[0].Id).ToString();

        var flight = await flightClient.GetByIdAsync(new GetByIdRequest { Id = seededFlightId });
        flight.FlightDto.Id.Should().Be(seededFlightId);

        var seats = await flightClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = seededFlightId });
        seats.SeatDtos.Should().NotBeEmpty();

        var healthClient = new Health.HealthClient(channel);
        var overallStatus = await healthClient.CheckAsync(new HealthCheckRequest { Service = string.Empty });
        overallStatus.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);

        var flightStatus = await healthClient.CheckAsync(
            new HealthCheckRequest { Service = FlightGrpcService.Descriptor.FullName }
        );
        flightStatus.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Fact]
    public async Task liveness_and_readiness_succeed_when_test_dependencies_are_running()
    {
        using var client = Fixture.HttpClient;
        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

}

public class FlightReadinessEndpointTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("docker")]
    public async Task ready_checks_include_untagged_dependencies_but_live_remains_healthy(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .AddCheck("failing-dependency", () => HealthCheckResult.Unhealthy());

        await using var app = builder.Build();
        app.MapFlightHealthEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        live.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
