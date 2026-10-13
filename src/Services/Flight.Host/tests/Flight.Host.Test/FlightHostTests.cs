using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using Contracts.Grpc.Flight;
using Flight.Data;
using Flight.Data.Seed;
using FlightStatus = Flight.Flights.Enums.FlightStatus;
using Flight.Flights.Features.CreatingFlight.V1;
using FluentAssertions;
using Grpc.Health.V1;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flight.Host.Test;

// In-process (WebApplicationFactory) checks of the standalone Flight host composition.
public class FlightHostTests : FlightHostTestBase
{
    public FlightHostTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task root_endpoint_should_return_flight_service_name()
    {
        var response = await Fixture.HttpClient.GetStringAsync("/");

        response.Should().Be("Flight-Service");
    }

    [Fact]
    public void host_should_compose_only_the_flight_module()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var eventMappers = scope.ServiceProvider.GetServices<IEventMapper>().ToList();

        eventMappers.Should().ContainSingle().Which.Should().BeOfType<FlightEventMapper>();
    }

    [Fact]
    public async Task grpc_health_should_report_flight_grpc_service_serving()
    {
        var client = new Health.HealthClient(Fixture.Channel);

        var response = await client.CheckAsync(
            new HealthCheckRequest { Service = FlightGrpcService.Descriptor.FullName }
        );

        response.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Fact]
    public async Task rest_create_flight_should_persist_flight_to_flight_service_db()
    {
        var request = new CreateFlightRequestDto(
            FlightNumber: "FH1001",
            AircraftId: InitialData.Aircrafts.First().Id,
            DepartureAirportId: InitialData.Airports.First().Id,
            DepartureDate: new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            ArriveDate: new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ArriveAirportId: InitialData.Airports.Last().Id,
            DurationMinutes: 120,
            FlightDate: new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status: FlightStatus.Flying,
            Price: 8000
        );

        var response = await Fixture.HttpClient.PostAsJsonAsync("/api/v1.0/flight", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateFlightResponseDto>();
        created!.Id.Should().NotBeEmpty();

        var flight = await Fixture.ExecuteDbContextAsync(db => db.Flights.FindAsync(Flights.ValueObjects.FlightId.Of(created.Id)));
        flight.Should().NotBeNull();
        flight!.FlightNumber.Value.Should().Be("FH1001");
    }
}
